# API 명세서

이 문서는 현재 구현 기준의 AiDesk API 계약을 요약합니다.

- Base URL: http://localhost:8080
- 인증 방식: JWT Bearer
- 익명 허용 엔드포인트: POST /api/knowledgebase/ask

---

## 1. 핵심 요약

현재 API에서 중요한 변경점은 다음과 같습니다.

1. RAG 응답의 topSimilarity는 최상위 후보의 semanticScore 기준입니다.
2. 임계치 통과 여부와 isLowSimilarity 판정도 semanticScore 기준입니다.
3. 키워드 보정은 후보 정렬과 진단용으로만 반영되며, retrievalDiagnostics.candidates[].adjustedSimilarity에 남습니다.
4. topMatchedQuestion 필드는 더 이상 응답 계약에서 쓰지 않고 topMatchedEvidenceText를 사용합니다.
5. LowSimilarityQuestions 테이블은 DB 컬럼명을 TopMatchedQuestion으로 유지하지만, 애플리케이션 모델 속성은 TopMatchedEvidenceText입니다.

---

## 2. KnowledgeBase API

### 2.1 질문/답변

#### POST /api/knowledgebase/ask

- 인증: 익명 허용
- 용도: 질문 1건에 대해 RAG 검색, 후보 선정, 답변 생성, 선택적 저장 처리

요청 필드

| 필드 | 타입 | 필수 | 설명 |
|---|---|---|---|
| question | string | 예 | 질문 본문, 최대 300자 |
| role | string | 아니오 | user 또는 admin, 기본값 user |
| platform | string | 아니오 | 공통, 전체 플랫폼, 또는 특정 플랫폼명 |
| sessionId | int | 아니오 | 기존 세션을 이어서 사용할 때 지정 |
| createSession | bool | 아니오 | sessionId가 없을 때 새 세션 생성 여부 |
| noSave | bool | 아니오 | true면 로그 저장과 ViewCount 증가를 생략 |
| historyMessageCount | int | 아니오 | 최근 대화 이력 메시지 수, 기본 6, 최대 6 |
| historyTurnCount | int | 아니오 | 레거시 호환용 턴 수, 내부에서 1턴=2메시지로 변환 |
| history | array | 아니오 | sessionId 없이 인라인 이력을 전달할 때 사용 |
| promptOverride.systemPrompt | string | 아니오 | 시스템 프롬프트 임시 덮어쓰기 |
| promptOverride.rulesPrompt | string | 아니오 | 답변 규칙 프롬프트 임시 덮어쓰기 |
| promptOverride.lowSimilarityMessage | string | 아니오 | 저유사도 응답 메시지 임시 덮어쓰기 |
| promptOverride.similarityThreshold | float | 아니오 | 임계치 임시 덮어쓰기, 0.1~0.95로 clamp |
| username | string | 아니오 | 외부 위젯 표시명 |
| userLoginId | string | 아니오 | 외부 위젯 로그인 ID |

요청 예시

```json
{
  "question": "인증서 조회가 되지 않아요",
  "role": "user",
  "platform": "공통",
  "createSession": true,
  "historyMessageCount": 6,
  "promptOverride": {
    "similarityThreshold": 0.5
  }
}
```

응답 필드

| 필드 | 타입 | 설명 |
|---|---|---|
| answer | string | 최종 사용자 답변 |
| topSimilarity | float | 최상위 후보의 semanticScore |
| isLowSimilarity | bool | semanticScore 기준 임계치 미달 여부 |
| topMatchedEvidenceText | string | 최상위 후보의 매칭 근거 텍스트 |
| topMatchedKbTitle | string | 최상위 후보 KB 제목 |
| topMatchedKbContent | string | 최상위 후보 KB 본문 |
| relatedKBs | array | semantic 임계치 통과 후보 목록 |
| conflictDetected | bool | 현재는 확장 포인트, 기본 false |
| decisionRule | string | 후보 선정 정책 설명 |
| retrievalDiagnostics | object | 검색 상세 진단 정보 |
| sessionId | int nullable | 저장된 세션 ID |

응답 예시

```json
{
  "answer": "인증서 조회가 되지 않을 때는 인증서 저장 위치와 브라우저 권한을 먼저 확인해 주세요.",
  "topSimilarity": 0.858,
  "isLowSimilarity": false,
  "topMatchedEvidenceText": "인증서가 안 보여요",
  "topMatchedKbTitle": "인증서 조회 오류 해결",
  "topMatchedKbContent": "인증서 저장 위치를 확인하고 브라우저를 재시작합니다.",
  "relatedKBs": [
    {
      "id": 43,
      "title": "인증서 조회 오류 해결",
      "content": "인증서 저장 위치를 확인하고 브라우저를 재시작합니다.",
      "similarity": 0.858,
      "matchedEvidenceText": "인증서가 안 보여요",
      "isSelected": true
    }
  ],
  "conflictDetected": false,
  "decisionRule": "semantic 임계치 통과 후보 중 상위 3개 선택",
  "retrievalDiagnostics": {
    "similarityThreshold": 0.5,
    "questionTokens": ["인증서", "조회"],
    "candidates": [
      {
        "id": 43,
        "title": "인증서 조회 오류 해결",
        "matchedEvidenceText": "인증서가 안 보여요",
        "baseSimilarity": 0.858,
        "keywordBoost": 0.02,
        "adjustedSimilarity": 0.878,
        "keywordMatchCount": 2,
        "matchedKeywords": ["인증서", "조회"],
        "includedByKeyword": true,
        "passedThreshold": true,
        "selectedForAnswer": true
      }
    ]
  },
  "sessionId": 12
}
```

판정 규칙 메모

- 정렬 순서는 adjustedSimilarity 기준입니다.
- 답변 가능 여부는 semanticScore 기준입니다.
- relatedKBs.similarity는 semanticScore입니다.

### 2.2 KB CRUD

- POST /api/knowledgebase (admin)
- PUT /api/knowledgebase/{id} (admin)
- GET /api/knowledgebase/{id}
- DELETE /api/knowledgebase/{id} (admin)
- GET /api/knowledgebase/list
- GET /api/knowledgebase/{id}/history
- GET /api/knowledgebase/stats

주요 요청 필드

| 필드 | 타입 | 설명 |
|---|---|---|
| title | string | KB 제목 |
| content | string | 해결 내용 |
| visibility | string | user 또는 admin |
| platforms | array | 다중 플랫폼 입력 |
| platform | string | 단일 플랫폼 레거시 입력 |
| keywords | string | 쉼표 또는 세미콜론 구분 키워드 |
| expectedQuestions | array | 예상질문 목록 |

### 2.3 작성 보조 API

- POST /api/knowledgebase/generate-expected-questions
- POST /api/knowledgebase/generate-keywords
- POST /api/knowledgebase/refine-solution
- POST /api/knowledgebase/recommend-title
- GET /api/knowledgebase/recommend-title

### 2.4 프롬프트/임계치 설정

- GET /api/knowledgebase/chatbot-prompt-template
- PUT /api/knowledgebase/chatbot-prompt-template
- GET /api/knowledgebase/writer-prompt-template
- PUT /api/knowledgebase/writer-prompt-template

노트

- SimilarityThreshold 기본값은 ChatbotPromptTemplates 테이블의 값으로 관리됩니다.
- ask 요청의 promptOverride.similarityThreshold는 요청 단위 임시 override입니다.

### 2.5 저유사도 문의

- GET /api/knowledgebase/low-similarity-questions
- PUT /api/knowledgebase/low-similarity-questions/{id}/resolve

저장 기준

- ask 요청에서 isLowSimilarity가 true이고 noSave가 false일 때 적재됩니다.
- TopMatchedEvidenceText 속성은 DB 컬럼 TopMatchedQuestion에 매핑됩니다.

### 2.6 벡터 인덱스/일괄 작업

- POST /api/knowledgebase/rebuild-vector-index
- POST /api/knowledgebase/reindex-all
- GET /api/knowledgebase/bulk-import/template
- POST /api/knowledgebase/bulk-import

노트

- 과거 문서의 documents/upload, PDF 다운로드 관련 API는 현재 기준에서 제거되었습니다.

---

## 3. Chat API

### 3.1 세션

- GET /api/chat/sessions
- POST /api/chat/sessions
- GET /api/chat/sessions/{id}
- DELETE /api/chat/sessions/{id}

세션 상세 응답에는 메시지별 메타가 포함됩니다.

| 필드 | 설명 |
|---|---|
| relatedKbIds | 최종 선택 KB ID 목록 JSON |
| relatedKbMeta | 선택 KB와 후보 메타 요약 JSON |
| retrievalDebugMeta | RetrievalDiagnostics 전체 JSON |
| topSimilarity | 봇 응답 당시 최상위 semanticScore |
| isLowSimilarity | 저유사도 여부 |

### 3.2 질문 요약

#### GET /api/chat/questions-summary

쿼리 파라미터

| 필드 | 기본값 | 설명 |
|---|---:|---|
| days | 7 | 조회 기간 |
| top | 10 | 상위 항목 개수 |
| role | null | 세션 role 필터 |
| platform | null | 세션 platform 필터 |

응답 예시

```json
{
  "days": 7,
  "from": "2026-05-04T00:00:00Z",
  "to": "2026-05-11T00:00:00Z",
  "totalQuestions": 120,
  "uniqueQuestions": 89,
  "uniqueReferencedKbs": 31,
  "rankingBasis": "kb-reference",
  "totalAnswers": 118,
  "avgSimilarity": 0.81,
  "lowSimilarityCount": 9,
  "lowSimilarityRate": 0.0763,
  "highConfidenceCount": 75,
  "highConfidenceRate": 0.6356,
  "similarityDistribution": [],
  "topReferencedKbs": [],
  "topQuestions": [],
  "topKeywords": [],
  "dailyCounts": []
}
```

집계 기준

1. topKeywords는 질문 원문이 아니라 bot 메시지의 relatedKbMeta.matchedKeywords 기준입니다.
2. topReferencedKbs는 relatedKbMeta 또는 relatedKbIds에서 selected KB만 집계합니다.
3. avgSimilarity, highConfidenceRate는 bot 메시지에 저장된 topSimilarity 기준입니다.

---

## 4. Health API

- GET /health
- GET /ready

---

## 5. 운영 검증 스냅샷

2026-05-10 기준 최신 리포트 요약

### 5.1 100문항 라이브 벤치

- 시스템 성공률: 100.00%
- 품질 성공률: 98.00%
- Positive 품질 성공률: 100.00%
- Negative 품질 성공률: 71.43%
- 평균 응답시간: 2386.1ms
- P95 응답시간: 5036.0ms

### 5.2 저장 질문 리플레이

- 순차 품질 일관성률: 100.00%
- 순차 P95: 3997.0ms
- 동시 처리량: 3.96 req/s

해석

- 현재 운영 리스크는 positive recall보다 negative 오탐에 더 가깝습니다.
- 그래서 현재 코드에서는 semanticScore를 임계치 판정 기준으로 사용합니다.
