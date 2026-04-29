# RAG + KB 전체 프로세스 문서

이 문서는 현재 코드 기준으로 KB 생성부터 질문 처리, 답변 생성, 집계까지의 동작을 설명합니다.

## 1. 목표와 구성

시스템은 두 채널을 함께 사용합니다.

1. FAQ형 KB 채널
- 관리자가 작성한 KB(제목, 내용, 키워드, 예상질문)

2. 문서형 KB 채널
- 업로드한 문서(PDF/텍스트)를 청크로 분할한 근거

핵심 원칙:
- 본문(제목+내용) 벡터와 예상질문 벡터를 분리해 인덱싱
- 키워드는 보조 신호(약한 가산)로만 사용
- 최종 답변 후보는 LLM 재정렬 후 상위 집합에서 선택

## 2. KB 생성/수정 파이프라인

### 2.1 입력 항목

1. Title
2. Content(Solution)
3. Visibility(user/admin)
4. Platform(공통 또는 특정 플랫폼)
5. Keywords
6. ExpectedQuestions

예상질문은 최대 10개까지 허용됩니다.

### 2.2 임베딩 생성

저장 시 임베딩은 다음과 같이 생성됩니다.

1. 본문 임베딩
- 입력: 제목 + 내용
- 저장: KnowledgeBase.ProblemEmbedding

2. 예상질문 임베딩
- 입력: 각 예상질문 문장(개별)
- 저장: KnowledgeBaseSimilarQuestion.QuestionEmbedding

### 2.3 벡터 인덱싱(Qdrant)

KB 저장/수정 후 Qdrant 컬렉션으로 upsert 됩니다.

1. 컬렉션: aidesk_kb
2. distance: Cosine
3. 포인트 구조
- document 포인트: KB당 1개 (본문 벡터)
- expected 포인트: 예상질문 수만큼 N개 (질문 벡터)

payload 주요 필드:
- kbId, type(document|expected), question, visibility, platforms, keywords, updatedAt

애플리케이션 기동 시 SyncAllKnowledgeBasesAsync로 전체 KB 재동기화를 수행합니다.

## 3. 사용자 질문 처리 파이프라인

엔드포인트: /api/knowledgebase/ask

### 3.1 질문 정규화

임베딩 전 질문 표현을 정규화합니다.

1. 안됨/불가 계열 표현 통합
2. 공백/표현 흔들림 축소

### 3.2 질문 임베딩 생성

정규화된 질문을 text-embedding-3-small로 임베딩합니다.

### 3.3 벡터 검색(본문/예상질문 분리)

Qdrant에서 raw hit를 조회한 뒤 타입별로 분리합니다.

1. document hit 상위 집합
2. expected hit 상위 집합

그 뒤 KB 단위로 병합하여 각 KB의 대표 semantic score를 계산합니다.

### 3.4 키워드 보조 가산

질문 토큰과 KB의 제목/키워드를 비교해 약한 보정치를 계산합니다.

1. 키워드 매칭 수에 비례한 소량 가산
2. 상한 제한 적용
3. 임계치와 큰 차이가 나는 후보에는 가산 미적용

즉, 키워드는 강제 통과 수단이 아니라 동점 해소/근소 보정 용도입니다.

### 3.5 병합 후보 재정렬(LLM rerank)

상위 병합 후보를 LLM에 전달해 관련성 순으로 재정렬합니다.

1. 입력: 질문 + 후보(id, semantic, final, source, 요약)
2. 출력: id 배열(JSON)
3. 설정된 상위 개수만 최종 컨텍스트 후보로 사용

응답이 비정상이면 final score 내림차순으로 fallback 합니다.

### 3.6 문서형 KB 보조 검색

FAQ 후보가 모두 임계치 미달인 경우에만 문서 청크 검색을 수행합니다.

문서 후보도 동일 임계치로 필터링합니다.

### 3.7 후보 없음 처리

FAQ 후보와 문서 후보가 모두 없으면 즉시 종료합니다.

1. topSimilarity = 0
2. isLowSimilarity = true
3. decisionRule = 후보 없음

### 3.8 최종 채택과 투표

임계치 이상 FAQ 후보만 eligibleResults로 남깁니다.

그 뒤 솔루션 정규화 기준으로 다수결을 수행합니다.

1. 단일 해법이면 그대로 채택
2. 충돌 시 상위 집합 다수결
3. 동률이면 유사도 합계 우선

채택된 후보(selectedResults)만 답변 근거와 집계 대상으로 사용됩니다.

### 3.9 답변 생성

FAQ 근거 + (필요 시) 문서 근거를 컨텍스트로 구성해 답변을 생성합니다.

topSimilarity가 임계치 미만이면 저유사도 안내문을 반환합니다.

## 4. 로그 저장 및 집계 기준

### 4.1 채팅 메시지 저장

relatedKbMeta에 후보 메타를 저장하며, isSelected=true인 KB만 최종 참조 ID로 반영합니다.

### 4.2 참조수(ViewCount)

selectedResults 기준으로만 ViewCount를 증가시킵니다.

### 4.3 질문 분석 리포트

/api/chat/questions-summary 집계도 selected 기준으로 동작합니다.

## 5. 설정값 요약

### 5.1 OpenAI

1. Embedding model: text-embedding-3-small
2. Chat model: gpt-4o-mini
3. ChatTemperature, ChatMaxTokens

### 5.2 RAG

1. Similarity threshold
2. vector topK 계열 상수
3. DocumentTopK

### 5.3 Qdrant

1. Enabled
2. Url
3. CollectionName

## 6. 엔드투엔드 시퀀스

1. 관리자가 KB 저장
2. 본문/예상질문 임베딩 생성
3. Qdrant에 document + expected 포인트 upsert
4. 사용자가 질문
5. 질문 정규화 + 임베딩
6. document/expected 벡터 검색
7. KB 단위 병합 + 키워드 약가산
8. LLM rerank
9. 임계치 통과 후보 채택
10. 필요 시 문서 채널 보조
11. 답변 생성 또는 저유사도 안내
12. selected KB 기준 참조수/집계 반영

## 7. 관련 코드 위치

1. KB 생성/수정/메타 저장
- AiDeskApi/Controllers/KnowledgeBaseController.cs

2. RAG 본체(검색, 병합, rerank, 투표, 응답)
- AiDeskApi/Services/OpenAiRagService.cs

3. 임베딩 생성
- AiDeskApi/Services/OpenAiEmbeddingService.cs

4. Qdrant 인덱싱/검색
- AiDeskApi/Services/QdrantVectorSearchService.cs

5. 임계치/프롬프트 템플릿
- AiDeskApi/Services/ChatbotPromptTemplateService.cs

6. 질문분석 집계
- AiDeskApi/Controllers/ChatController.cs
