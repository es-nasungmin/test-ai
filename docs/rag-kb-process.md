# RAG + KB 전체 프로세스 문서

이 문서는 현재 코드 기준으로 KB 생성부터 사용자 질문, 답변 생성, 로그/분석 집계까지 전체 흐름을 설명합니다.

## 1. 목표와 구성

이 시스템은 다음 두 채널을 함께 사용합니다.

1. FAQ형 KB 채널
- 관리자가 작성한 KB(제목, 내용, 키워드, 예상질문)를 벡터로 검색

2. 문서형 KB 채널
- 업로드한 문서(PDF/텍스트)를 청크로 나누어 벡터 검색

최종 답변은 OpenAI Chat 모델이 생성하며, 근거 부족 시 저유사도 안내문으로 안전하게 종료합니다.

## 2. KB 생성/수정 파이프라인

### 2.1 입력 항목

KB 생성/수정 시 핵심 입력은 아래와 같습니다.

1. Title
2. Content(Solution)
3. Visibility(user/admin)
4. Platform(공통 또는 특정 플랫폼)
5. Keywords
6. ExpectedQuestions

현재 예상질문은 최대 10개까지 허용됩니다.

### 2.2 임베딩 소스 구성

저장 시 단일 텍스트를 조합해 임베딩합니다. 구성 순서는 아래와 같습니다.

1. 제목
2. 내용
3. 키워드
4. 예상질문 목록

즉, 단순히 제목/내용만 임베딩하지 않고 키워드와 예상질문까지 포함해 검색 리콜을 높입니다.

### 2.3 임베딩 생성 모델

OpenAI Embedding API를 사용하며 모델은 text-embedding-3-small 입니다.

### 2.4 벡터 인덱싱(Qdrant)

KB 저장/수정 후 Qdrant 컬렉션으로 upsert 됩니다.

1. 컬렉션: aidesk_kb
2. distance: Cosine
3. payload 주요 필드: kbId, visibility, platforms, keywords, expectedQuestions

애플리케이션 기동 시 SyncAllKnowledgeBasesAsync 로 전체 KB를 재동기화합니다.

## 3. 사용자 질문 처리 파이프라인

엔드포인트: /api/knowledgebase/ask

### 3.1 질문 정규화

임베딩 전에 질문 표현을 정규화합니다.

1. 안됨/불가 계열 표현 통합
2. 공백/표현 흔들림 축소

목적은 의미가 같은 문장의 임베딩 분산을 줄이는 것입니다.

### 3.2 질문 임베딩 생성

정규화된 질문을 text-embedding-3-small 로 임베딩합니다.

### 3.3 1차 벡터 검색(semantic)

Qdrant에서 semantic top 후보를 조회합니다.

1. topK: 15
2. role 기반 필터
- user 역할: visibility=user 만
- admin 역할: 전체 접근
3. platform 기반 필터
- 공통/전체/특정 플랫폼 반영

### 3.4 임계치 1차 통과 후보

semantic 후보 중 similarityThreshold 이상만 semanticPassed 로 분류합니다.

기본 임계치는 현재 0.4입니다.

### 3.5 키워드 후보 검색

질문 토큰을 추출해 KB 텍스트와 매칭 점수를 계산합니다.

1. 제목/키워드 일치: +2
2. 본문(Solution) 일치: +1
3. 상위 10개 후보를 keywordTop 으로 선택

중요: 키워드 점수는 후보 리콜 확장용이며, 최종 similarity 점수 자체를 직접 올리지는 않습니다.

### 3.6 후보 병합

candidateKbIds = semanticPassed + keywordTop 의 합집합

이 단계에서 누락되기 쉬운 표현을 키워드로 보완합니다.

### 3.7 GPT 재정렬(re-rank)

병합 후보를 GPT에 전달해 관련성 순으로 재정렬합니다.

1. 입력: 질문 + 후보 요약(id, title, semantic, content)
2. 출력: 후보 id 배열(JSON)
3. 상위 5개까지 사용

응답이 비정상이면 semantic 점수 내림차순으로 fallback 합니다.

### 3.8 문서형 KB 보조 검색

semanticPassed 가 0개일 때만 문서 청크 검색을 추가 수행합니다.

1. DocumentTopK 설정값 사용
2. 문서 후보도 동일 임계치로 필터링

### 3.9 후보 없음 처리

FAQ 후보와 문서 후보가 모두 없으면 즉시 종료합니다.

1. topSimilarity = 0
2. isLowSimilarity = true
3. decisionRule = 후보 없음

### 3.10 최종 채택과 투표 규칙

rerank 결과 중 임계치 통과 후보만 eligibleResults 로 남깁니다.

그 다음 해법(솔루션 텍스트 정규화)을 기준으로 묶어 다수결을 수행합니다.

1. 단일 해법이면 그대로 채택
2. 충돌 시 상위 3개 기준 다수결
3. 동률이면 유사도 합계 우선

채택된 후보가 selectedResults 입니다.

### 3.11 답변 생성

최종 컨텍스트를 구성해 OpenAI Chat Completion 으로 답변을 생성합니다.

1. FAQ 근거 컨텍스트
2. 필요 시 문서 근거 컨텍스트
3. role 별 시스템 프롬프트/규칙 프롬프트 적용
4. 근거 외 정보 금지 규칙 적용

topSimilarity 가 임계치 미만이면 생성 대신 저유사도 안내문을 반환합니다.

## 4. 로그 저장 및 집계 기준

### 4.1 채팅 메시지 저장

질문/답변 저장 시 relatedKbMeta 에 후보 메타가 저장됩니다.

핵심 포인트:

1. isSelected 플래그 포함
2. RelatedKbIds 는 isSelected=true 인 KB만 저장

즉, 집계 기준이 후보 전체가 아니라 채택된 KB 기준입니다.

### 4.2 참조수(view count) 증가

selectedResults 기준으로만 KB 참조수(ViewCount)를 증가시킵니다.

### 4.3 질문 분석 리포트

/api/chat/questions-summary 집계 시에도 selected 기준을 사용합니다.

1. ParseReferencedKbIds 에서 isSelected=false 제외
2. topReferencedKbs 제공
3. rankingBasis = kb-reference

## 5. 설정값 정리

### 5.1 OpenAI

1. Embedding model: text-embedding-3-small
2. Chat model: gpt-4o-mini
3. ChatTemperature, ChatMaxTokens 설정 가능

### 5.2 RAG

1. DocumentTopK
2. VectorTopK, KeywordRecallTopK 등 보조 파라미터

### 5.3 Qdrant

1. Enabled
2. Url
3. CollectionName

## 6. 엔드투엔드 시퀀스 요약

1. 관리자가 KB 저장
2. 임베딩 생성(text-embedding-3-small)
3. Qdrant upsert
4. 사용자가 질문
5. 질문 정규화 + 임베딩
6. semantic 검색 + 키워드 후보 확장
7. GPT rerank
8. 임계치 통과 후보만 채택
9. 필요 시 문서 채널 보조
10. 최종 답변 생성 또는 저유사도 안내
11. selected KB 기준으로 참조수/메타 저장
12. 질문 분석 리포트에서 참조 KB 기준 집계

## 7. 관련 코드 위치

1. KB 생성/수정/관련 메타 저장
- AiDeskApi/Controllers/KnowledgeBaseController.cs

2. RAG 본체(검색, rerank, 투표, 응답 생성)
- AiDeskApi/Services/OpenAiRagService.cs

3. 임베딩 생성
- AiDeskApi/Services/OpenAiEmbeddingService.cs

4. Qdrant 인덱싱/검색
- AiDeskApi/Services/QdrantVectorSearchService.cs

5. 임계치/프롬프트 템플릿
- AiDeskApi/Services/ChatbotPromptTemplateService.cs

6. 질문분석 집계
- AiDeskApi/Controllers/ChatController.cs
