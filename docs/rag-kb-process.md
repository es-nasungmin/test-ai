# RAG + KB 전체 프로세스

이 문서는 현재 코드 기준의 KB 저장, 검색, 후보 선택, 답변 생성, 로그 저장, 운영 검증 흐름을 설명합니다.

대상 소스

- AiDeskApi/Services/OpenAiRagService.cs
- AiDeskApi/Controllers/KnowledgeBaseController.cs
- AiDeskApi/Controllers/ChatController.cs
- AiDeskApi/Services/QdrantVectorSearchService.cs

---

## 1. 이번 버전의 핵심 변화

현재 버전에서 발표 시 꼭 강조해야 할 포인트는 다음과 같습니다.

1. KB 검색은 document 벡터와 expected 벡터를 분리해 조회합니다.
2. KB 후보 정렬에는 semanticScore와 keyword boost가 함께 쓰입니다.
3. 하지만 최종 답변 가능 여부는 semanticScore 기준으로만 판단합니다.
4. 따라서 키워드 보정이 threshold 판정을 뒤집지 않습니다.
5. RetrievalDiagnostics에는 semantic, keyword, adjusted 점수를 모두 남겨 추적이 가능합니다.

발표용 한 줄 설명

- 키워드는 후보 순서를 보정하는 약한 신호로만 사용하고, 최종 채택 여부는 semanticScore로만 판단합니다.

이 구조는 negative 오탐을 줄이면서도, 경계 구간에서 후보 순서 보정 정보는 유지하기 위한 선택입니다.

---

## 2. 엔드투엔드 흐름 한눈에 보기

```text
관리자 KB 저장
  -> 본문 document 포인트 생성
  -> 예상질문 expected 포인트 생성
  -> Qdrant upsert

사용자 질문
  -> 질문 정규화
  -> 조건부 질문 정제
  -> 임베딩 생성
  -> Qdrant raw search
  -> document/expected 분리
  -> KB 단위 병합
  -> keyword boost 계산
  -> adjustedSimilarity 기준 정렬
  -> 조건부 LLM rerank
  -> FinalTopK 확정
  -> semantic threshold 필터
  -> 상위 3개 selectedResults 확정
  -> GPT 답변 생성 또는 저유사도 즉시 반환
  -> ViewCount, ChatMessage, LowSimilarity 로그 저장
```

---

## 3. KB 생성/수정 파이프라인

### 3.1 입력 데이터

| 항목 | 설명 |
|---|---|
| Title | KB 제목 |
| Content | 해결 내용 |
| Visibility | user 또는 admin |
| Platform | 공통 또는 특정 플랫폼 |
| Keywords | 검색 보조 키워드 |
| ExpectedQuestions | 예상질문 목록 |

### 3.2 임베딩 생성

본문 임베딩

- 입력: Title + Content 결합 텍스트
- 결과: document 포인트 1개

예상질문 임베딩

- 입력: 예상질문 각 문장
- 결과: expected 포인트 N개

현재 구현 특징

1. 임베딩 벡터는 RDB 컬럼에 저장하지 않습니다.
2. Qdrant upsert 시점에 생성합니다.
3. 전체 재동기화는 관리자 API로 수행합니다.

---

## 4. 질문 처리 파이프라인

### 4.1 단계별 개요

| 단계 | 설명 | 주요 상수 |
|---|---|---|
| 0 | 질문 정규화 및 조건부 질문 정제 | HistoryTurnLimit=3 |
| 1 | 임베딩 생성 | - |
| 2 | Qdrant raw search | RawVectorTopK=40 |
| 3 | document/expected 분리 유지 | 15 / 20 |
| 4 | KB 단위 병합 + keyword boost | MergeTopK=10 |
| 5 | 조건부 LLM rerank | ReRankTopK=8 |
| 6 | FinalTopK 확정 | FinalTopK=5 |
| 7 | semantic threshold 필터 | SimilarityThreshold 기본 0.5 |
| 8 | selectedResults 확정 | AnswerContextTopK=3 |
| 9 | GPT 답변 또는 low-similarity 응답 | - |

---

### 4.2 질문 정규화와 정제

정규화 목적

- 표기 흔들림을 줄여 임베딩 분산을 줄임

예시

| 원문 | 정규화 |
|---|---|
| 안됨, 안돼요, 불가 | 안돼 |
| 안보임, 안보여요 | 안보여 |

질문 정제 목적

- 대화 이력이 있을 때 후속 질문을 단일 의미 질문으로 풀어줌

예시

```text
이전 대화:
사용자: 인증서가 뭐야?
봇: 인증서는 사용자 인증용 디지털 증명서입니다.
사용자: 어디에 저장해야 되는데

정제 결과:
인증서는 어디에 저장해야 하나요?
```

---

### 4.3 Qdrant raw search

| 항목 | 값 |
|---|---:|
| RawVectorTopK | 40 |
| DocumentVectorTopK | 15 |
| ExpectedVectorTopK | 20 |

필터 규칙

1. role=user면 visibility=user KB만 조회
2. role=admin이면 전체 조회
3. platform=공통이면 공통 KB만
4. 특정 플랫폼이면 공통 + 해당 플랫폼 KB
5. 전체 플랫폼이면 플랫폼 제한 없음

---

### 4.4 KB 단위 병합과 점수 구성

각 KB 후보는 아래 세 점수를 갖습니다.

| 점수 | 의미 |
|---|---|
| SemanticScore | document 또는 expected 중 더 높은 벡터 점수 |
| KeywordBoost | 질문 토큰과 KB 제목, 키워드, 본문 매칭 기반 가산점 |
| AdjustedSimilarity | SemanticScore + KeywordBoost |

현재 상수

| 항목 | 값 |
|---|---:|
| KeywordBoostPerMatch | 0.01 |
| MaxKeywordBoost | 0.03 |
| KeywordBoostHardFloorGap | 0.05 |

중요한 정책

1. keyword boost는 semanticScore가 threshold 근처일 때만 적용됩니다.
2. adjustedSimilarity는 후보 정렬과 rerank 입력용입니다.
3. threshold 통과 판정에는 semanticScore만 사용합니다.
4. 즉, keyword boost는 후보 순서와 진단 설명에는 영향을 주지만, 답변 가능 여부 자체를 바꾸지는 않습니다.

이 정책이 필요한 이유

- 키워드 보정은 순서 미세 조정에는 유용하지만, threshold를 뒤집으면 negative 오탐이 늘기 쉽습니다.

---

### 4.5 조건부 LLM rerank

| 항목 | 값 |
|---|---:|
| ReRankTopK | 8 |
| ReRankSkipScoreThreshold | 0.82 |
| ReRankSkipGapThreshold | 0.15 |

스킵 규칙

1. 1위 adjustedSimilarity가 0.82 이상이면 rerank 생략
2. 1위와 2위 adjustedSimilarity 차이가 0.15 이상이면 rerank 생략

실행 규칙

- 위 조건에 해당하지 않으면 GPT에 후보 요약을 보내 관련성 순으로 재정렬합니다.

---

### 4.6 FinalTopK와 semantic threshold

후보 확정 순서

1. reranked 상위 5개를 topResults로 확정
2. topResults 중 semanticScore가 threshold 이상인 후보만 eligibleResults
3. eligibleResults 상위 3개를 selectedResults로 확정

현재 판정 기준

| 항목 | 기준 |
|---|---|
| topSimilarity | topResults 1위의 semanticScore |
| isLowSimilarity | topResults 1위 semanticScore < threshold |
| relatedKBs | eligibleResults 기준 |
| relatedKBs.similarity | semanticScore |
| retrievalDiagnostics.candidates[].adjustedSimilarity | semantic + keyword |

이전 버전과의 차이

- 예전에는 keyword boost가 포함된 finalScore가 threshold 판정에도 쓰일 수 있었습니다.
- 현재는 semanticScore만 threshold 판정에 사용합니다.

---

### 4.7 답변 생성과 low-similarity 응답

답변 생성 조건

- topSimilarity가 threshold 이상일 때만 GPT 답변 생성

low-similarity 조건

- topSimilarity가 threshold 미만이면 GPT 호출 없이 즉시 안내문 반환

전달되는 컨텍스트

1. selectedResults 최대 3개
2. 최근 대화 이력 최대 6메시지
3. role별 시스템 프롬프트와 규칙 프롬프트

---

## 5. 저장 및 집계 규칙

### 5.1 ViewCount

- selectedResults 기준으로만 증가합니다.
- 후보군에만 오른 KB는 증가하지 않습니다.

### 5.2 ChatMessages

bot 메시지에 저장되는 메타

| 필드 | 설명 |
|---|---|
| RelatedKbIds | selected KB ID 목록 |
| RelatedKbMeta | selected KB 메타 요약 |
| RetrievalDebugMeta | 전체 RetrievalDiagnostics |
| TopSimilarity | 최상위 semanticScore |
| IsLowSimilarity | 저유사도 여부 |

### 5.3 LowSimilarityQuestions

저장 조건

- ask 요청 결과 isLowSimilarity=true
- noSave=false

저장되는 주요 정보

1. 질문 원문
2. role
3. actorName
4. platform
5. topSimilarity
6. topMatchedEvidenceText
7. topMatchedKbTitle
8. topMatchedKbContent

---

## 6. RetrievalDiagnostics 읽는 법

운영이나 발표에서 가장 설명하기 좋은 필드는 아래입니다.

| 필드 | 의미 |
|---|---|
| similarityThreshold | 이번 요청에 적용된 threshold |
| questionTokens | 질문에서 추출한 토큰 |
| candidates[].baseSimilarity | semanticScore |
| candidates[].keywordBoost | 키워드 보정치 |
| candidates[].adjustedSimilarity | 정렬용 보정 점수 |
| candidates[].passedThreshold | semantic threshold 통과 여부 |
| candidates[].selectedForAnswer | 답변 컨텍스트 채택 여부 |

발표 시 설명 문장 예시

"정렬은 adjustedSimilarity로 하되, 실제 답변 가능 여부는 baseSimilarity, 즉 semanticScore 기준으로만 판단합니다. 그래서 키워드가 조금 겹쳤다고 근거 없는 답변이 통과하지 않게 했습니다."

같은 의미의 짧은 버전

"키워드는 정렬용이고, semantic은 판정용입니다."

---

## 7. 운영 검증 최신 스냅샷

### 7.1 100문항 라이브 벤치

2026-05-10 결과

- 시스템 성공률: 100.00%
- 품질 성공률: 98.00%
- Positive 품질 성공률: 100.00%
- Negative 품질 성공률: 71.43%
- 평균 응답시간: 2386.1ms
- P95 응답시간: 5036.0ms

해석

- positive recall은 충분히 높았습니다.
- 현재 남은 리스크는 negative 오탐입니다.
- 그래서 threshold 판정은 semanticScore 기준으로 단순화했습니다.

### 7.2 저장 질문 리플레이

2026-05-10 결과

- 순차 품질 일관성률: 100.00%
- 순차 P95: 3997.0ms
- 동시 처리량: 3.96 req/s

의미

- 동일 저장 질문 재실행 시 결과 흔들림은 낮았습니다.
- 현재 변경은 후보 판정 기준을 명확히 하는 방향이라, 다음 검증 포인트는 negative 개선 여부입니다.

---

## 8. 발표용 메시지 정리

짧게 설명하면 아래 순서가 가장 전달력이 좋습니다.

1. document와 expected를 따로 검색합니다.
2. KB 단위로 병합하고 keyword boost로 순서를 보정합니다.
3. 하지만 답변 가능 여부는 semanticScore 기준으로만 판단합니다.
4. selected KB만 ViewCount, 대화 로그, 통계에 반영합니다.
5. retrievalDiagnostics로 후보 선정 근거를 추적할 수 있습니다.

한 줄 버전

"키워드는 정렬을 돕고, semantic이 최종 판정을 담당하는 구조입니다."

조금 더 직설적인 버전

"키워드 보정은 순서 정렬에만 쓰고, 통과 여부는 semanticScore로만 결정합니다."
