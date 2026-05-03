# RAG + KB 전체 프로세스 문서

이 문서는 현재 코드 기준으로 KB 생성부터 질문 처리, 답변 생성, 집계까지의 동작을 설명합니다.

관련 소스: `AiDeskApi/Services/OpenAiRagService.cs`

---

## 1. 목표와 구성

시스템은 단일 KB 채널을 사용하며, 검색용 벡터 포인트를 두 종류로 운용합니다.

1. KB 본문 포인트 (`document` 타입)
   - 관리자가 작성한 KB의 제목+내용 기반 임베딩

2. 예상질문 포인트 (`expected` 타입)
   - KB별 예상질문 임베딩

핵심 원칙:
- 본문(제목+내용) 벡터와 예상질문 벡터를 분리해 인덱싱
- 키워드는 보조 신호(약한 가산)로만 사용
- 최종 답변 후보는 조건부 LLM 재정렬 후 상위 집합에서 선택

---

## 2. KB 생성/수정 파이프라인

### 2.1 입력 항목

| 항목 | 설명 |
|------|------|
| Title | KB 제목 (필수) |
| Content (Solution) | 답변 내용 (필수) |
| Visibility | `user`(공개) / `admin`(내부) |
| Platform | `공통` 또는 특정 플랫폼명 |
| Keywords | 검색 보조 키워드 (쉼표/세미콜론 구분) |
| ExpectedQuestions | 예상질문 최대 10개 |

### 2.2 임베딩 생성

저장 시 임베딩은 다음과 같이 생성됩니다.

1. 본문 임베딩
   - 모델: `text-embedding-3-small` (OpenAI)
   - 입력: 제목 + 내용 결합 텍스트
   - 저장: `KnowledgeBase.ProblemEmbedding` (JSON 문자열)

2. 예상질문 임베딩
   - 모델: 동일
   - 입력: 예상질문 각각을 개별 임베딩
   - 저장: `KnowledgeBaseSimilarQuestion.QuestionEmbedding`

### 2.3 벡터 인덱싱 (Qdrant)

KB 저장/수정 후 Qdrant 컬렉션으로 upsert 됩니다.

| 항목 | 값 |
|------|------|
| 컬렉션명 | `aidesk_kb` |
| 거리 함수 | Cosine |
| document 포인트 | KB당 1개 (본문 벡터) |
| expected 포인트 | 예상질문 수만큼 N개 (질문 벡터) |

payload 주요 필드:
- `kbId`, `type`(`document`|`expected`), `question`, `visibility`, `platforms`, `keywords`, `updatedAt`

참고:
- 여기서 `document`는 업로드 문서 타입이 아니라 "KB 본문 임베딩 포인트"를 의미합니다.

> 앱 기동 시 `SyncAllKnowledgeBasesAsync`로 전체 KB 재동기화를 수행합니다.

---

## 3. 사용자 질문 처리 파이프라인

엔드포인트: `POST /api/knowledgebase/ask`

### 파이프라인 단계별 수치 요약

```
사용자 질문
    ↓ [1단계] 질문 정규화
    ↓ [2단계] OpenAI 임베딩 (text-embedding-3-small)
    ↓ [3단계] Qdrant 벡터 검색 → Raw 40개 추출
                ├─ document 타입 → 상위 15개 (DocumentVectorTopK)
                └─ expected 타입 → 상위 20개 (ExpectedVectorTopK)
    ↓ [4단계] KB 단위 병합 + 키워드 보조 가산
                → 상위 10개 후보로 압축 (MergeTopK)
    ↓ [5단계] LLM Rerank (조건부)
                고신뢰 조건 해당 시: 점수 내림차순 정렬만 (LLM 호출 없음)
                일반 조건 시: GPT에 10개 → 상위 8개로 재정렬 (ReRankTopK)
    ↓ [6단계] FinalTopK 5개 → 유사도 임계치(0.5) 필터링
    ↓ [7단계] 충돌 감지 + 투표 → selectedResults 확정
    ↓ [8단계] 답변 생성 (GPT 1회 호출)
    ↓ 최종 응답
```

### 3.1 질문 정규화

임베딩 전 질문 표현을 정규화합니다.

| 원본 표현 | 정규화 결과 |
|-----------|-------------|
| 안됨, 안돼요, 불가, 조회 불가 | 안돼 |
| 안보임, 안보여요 | 안보여 |
| 연속 공백 | 단일 공백 |

### 3.2 질문 임베딩 생성

- 모델: `text-embedding-3-small`
- 출력: 1536차원 float 벡터

### 3.3 Qdrant 벡터 검색 — Raw 40개 추출

| 파라미터 | 값 | 설명 |
|----------|-----|------|
| RawVectorTopK | **40** | Qdrant에서 한 번에 조회하는 총 hit 수 |
| DocumentVectorTopK | **15** | document 타입 hit 중 유지할 상위 개수 |
| ExpectedVectorTopK | **20** | expected 타입 hit 중 유지할 상위 개수 |

- role이 `user`이면 `visibility='user'`인 KB만, `admin`이면 전체 KB 대상
- platform 필터: `공통` + 지정 플랫폼 KB만 포함
- document hit과 expected hit 각각 score 내림차순 정렬 후 개수 제한 적용

### 3.4 KB 단위 병합 + 키워드 보조 가산 — 상위 10개 후보

| 파라미터 | 값 | 설명 |
|----------|-----|------|
| MergeTopK | **10** | 병합 후 유지할 KB 후보 수 |
| KeywordBoostPerMatch | 0.01 | 키워드 매칭 1개당 가산 점수 |
| MaxKeywordBoost | 0.03 | 키워드 가산 상한 |
| KeywordBoostHardFloorGap | 0.05 | SemanticScore < (threshold − 0.05) 이면 가산 미적용 |

병합 로직:
1. document hit과 expected hit을 kbId 기준으로 그룹화
2. 각 KB에서 document 최고점 vs expected 최고점 비교 → 높은 쪽이 SemanticScore
3. 질문 토큰과 KB 제목/키워드 매칭 → KeywordBoost 계산
4. FinalScore = SemanticScore + KeywordBoost
5. FinalScore 내림차순으로 상위 10개만 유지

키워드는 임계치 근방의 근소 차이를 보정하는 약한 신호입니다. 임계치와 크게 차이 나는 후보에는 적용하지 않습니다.

### 3.5 조건부 LLM Rerank — 최대 8개 재정렬

| 파라미터 | 값 | 설명 |
|----------|-----|------|
| ReRankTopK | **8** | LLM rerank 대상 및 결과 최대 개수 |
| ReRankSkipScoreThreshold | **0.82** | 1위 FinalScore ≥ 이 값이면 rerank 스킵 |
| ReRankSkipGapThreshold | **0.15** | 1위-2위 점수 차 ≥ 이 값이면 rerank 스킵 |

**Rerank 스킵 조건 (LLM 호출 없음):**
- 1위 후보 FinalScore ≥ 0.82 → 이미 충분히 명확한 매칭
- 1위와 2위의 점수 차 ≥ 0.15 → 경쟁 후보 없음

**Rerank 실행 조건 (LLM 1회 호출):**
- 위 조건 모두 미해당 → 상위 10개 후보를 GPT에게 전달해 질문 관련성 순 재정렬
- 입력: 질문 + 후보 요약 (id, title, semantic, source, content 180자 이내)
- 출력: id 정수 배열 (JSON)
- 응답 이상 시: FinalScore 내림차순 fallback

### 3.6 FinalTopK 5개 확정 + 임계치 필터링

| 파라미터 | 값 | 설명 |
|----------|-----|------|
| FinalTopK | **5** | 답변 컨텍스트에 사용할 최대 KB 수 |
| SimilarityThreshold | **0.5** | 유사도 임계치 (ChatbotPromptTemplateService에서 관리) |

- reranked 결과 상위 5개를 topResults로 확정
- topResults 중 FinalScore ≥ 0.5 인 것만 eligibleResults (답변 근거 대상)
- topResults가 0개이거나 1위 점수가 0.5 미만이면 저유사도 안내문 반환

### 3.7 충돌 감지 + 투표 — selectedResults 확정

eligibleResults 내 해법(Solution)을 정규화 후 다수결 처리합니다.

1. 단일 해법: 그대로 채택
2. 복수 해법(충돌): 동일 해법 그룹별 케이스 수 비교 → 최다 그룹 채택
3. 동률: 유사도 합계가 높은 그룹 채택

채택된 `selectedResults`만 ViewCount 증가, 답변 근거, 집계 대상으로 사용됩니다.

### 3.8 답변 생성 (GPT 1회 호출)

- 모델: `gpt-4o-mini`
- eligibleResults → 컨텍스트 문자열 구성
- role에 따라 admin/user 전용 시스템 프롬프트 사용
- topSimilarity < 0.5 이면 LLM 호출 없이 저유사도 안내문 즉시 반환

---

## 4. 총 LLM 호출 횟수 요약

| 조건 | 임베딩 | Rerank LLM | Answer LLM | 합계 |
|------|--------|-----------|------------|------|
| 고신뢰 매칭 (rerank 스킵) | 1 | 0 | 1 | **2** |
| 모호한 후보 (rerank 실행) | 1 | 1 | 1 | **3** |
| 저유사도 (answer 스킵) | 1 | 0~1 | 0 | **1~2** |

---

## 5. 로그 저장 및 집계 기준

### 5.1 채팅 메시지 저장

`relatedKbMeta`에 후보 메타를 저장하며, `isSelected=true`인 KB만 최종 참조 ID로 반영됩니다.

### 5.2 참조수 (ViewCount)

`selectedResults` 기준으로만 ViewCount를 증가시킵니다.

### 5.3 질문 분석 리포트

`GET /api/chat/questions-summary` 집계도 selected 기준으로 동작합니다.

---

## 6. 설정값 전체 요약

| 파라미터 | 현재값 | 위치 |
|----------|--------|------|
| RawVectorTopK | 40 | OpenAiRagService.cs |
| DocumentVectorTopK | 15 | OpenAiRagService.cs |
| ExpectedVectorTopK | 20 | OpenAiRagService.cs |
| MergeTopK | 10 | OpenAiRagService.cs |
| ReRankTopK | 8 | OpenAiRagService.cs |
| FinalTopK | 5 | OpenAiRagService.cs |
| ReRankSkipScoreThreshold | 0.82 | OpenAiRagService.cs |
| ReRankSkipGapThreshold | 0.15 | OpenAiRagService.cs |
| KeywordBoostPerMatch | 0.01 | OpenAiRagService.cs |
| MaxKeywordBoost | 0.03 | OpenAiRagService.cs |
| SimilarityThreshold | 0.5 | ChatbotPromptTemplateService.cs |
| Embedding Model | text-embedding-3-small | appsettings.json |
| Chat Model | gpt-4o-mini | appsettings.json |

---

## 7. 엔드투엔드 시퀀스

```
[관리자 작업]
1. 관리자가 KB 작성 (제목/내용/키워드/예상질문)
2. 저장 시 본문 임베딩 1개 + 예상질문 임베딩 N개 생성
3. Qdrant aidesk_kb 컬렉션에 document/expected 포인트 upsert

[사용자 질문]
4. 사용자가 질문 입력
5. 질문 정규화 (표현 통일)
6. OpenAI 임베딩 생성 (1536d 벡터)
7. Qdrant 코사인 검색 → raw 40개 hit 수신
8. document 15개 / expected 20개로 분리
9. KB 단위 병합 + 키워드 가산 → 상위 10개 후보
10. Rerank 스킵 판단
    ├─ 스킵 → 점수 내림차순 정렬 (LLM 호출 없음)
    └─ 실행 → GPT 재정렬 요청 → 상위 8개
11. 상위 5개(FinalTopK) 확정
12. 유사도 임계치(0.5) 필터 → eligibleResults
    ├─ 0개 또는 임계치 미달 → 저유사도 안내문 반환 (LLM 없음)
    └─ 1개 이상 → 다음 단계
13. 충돌 감지 + 다수결 → selectedResults
14. 컨텍스트 구성 + GPT 답변 생성
15. selectedResults 기준 ViewCount 증가 + 집계 반영
16. 클라이언트에 응답 반환
```

---

## 8. 관련 코드 위치

| 역할 | 파일 |
|------|------|
| RAG 본체 (검색/병합/rerank/투표/응답) | `AiDeskApi/Services/OpenAiRagService.cs` |
| 임베딩 생성 | `AiDeskApi/Services/OpenAiEmbeddingService.cs` |
| Qdrant 인덱싱/검색 | `AiDeskApi/Services/QdrantVectorSearchService.cs` |
| 임계치/프롬프트 템플릿 | `AiDeskApi/Services/ChatbotPromptTemplateService.cs` |
| KB 생성/수정/메타 저장 | `AiDeskApi/Controllers/KnowledgeBaseController.cs` |
| 질문분석 집계 | `AiDeskApi/Controllers/ChatController.cs` |
