# API 명세서 (현재 코드 기준)

Base URL: `http://localhost:8080`

인증:
- 기본은 JWT 인증
- `POST /api/knowledgebase/ask`는 익명 허용

## 1. KnowledgeBase API

### 1.1 질문/답변

- `POST /api/knowledgebase/ask` (익명 허용)
  - RAG 질의
  - 요청 주요 필드: `question`, `role`, `platform`, `sessionId`, `createSession`, `noSave`, `historyMessageCount`
  - 응답 주요 필드: `answer`, `topSimilarity`, `isLowSimilarity`, `relatedKBs`, `retrievalDiagnostics`, `sessionId`

### 1.2 KB CRUD

- `POST /api/knowledgebase` (admin)
- `PUT /api/knowledgebase/{id}` (admin)
- `GET /api/knowledgebase/{id}`
- `DELETE /api/knowledgebase/{id}` (admin)
- `GET /api/knowledgebase/list`
- `GET /api/knowledgebase/{id}/history`
- `GET /api/knowledgebase/stats`

### 1.3 작성 보조

- `POST /api/knowledgebase/generate-expected-questions`
- `POST /api/knowledgebase/generate-keywords`
- `POST /api/knowledgebase/refine-solution`
- `POST /api/knowledgebase/recommend-title`
- `GET /api/knowledgebase/recommend-title`

### 1.4 프롬프트/임계치 설정

- `GET /api/knowledgebase/chatbot-prompt-template`
- `PUT /api/knowledgebase/chatbot-prompt-template`
- `GET /api/knowledgebase/writer-prompt-template`
- `PUT /api/knowledgebase/writer-prompt-template`

### 1.5 저유사도 문의

- `GET /api/knowledgebase/low-similarity-questions`
- `PUT /api/knowledgebase/low-similarity-questions/{id}/resolve`

### 1.6 벡터 인덱스/일괄 작업

- `POST /api/knowledgebase/rebuild-vector-index`
- `POST /api/knowledgebase/reindex-all`
- `GET /api/knowledgebase/bulk-import/template`
- `POST /api/knowledgebase/bulk-import`

노트:
- 과거 문서에 있던 `documents/upload`, PDF 다운로드 API는 현재 제거되었습니다.

## 2. Chat API

- `GET /api/chat/sessions`
- `POST /api/chat/sessions`
- `GET /api/chat/sessions/{id}`
- `DELETE /api/chat/sessions/{id}`
- `GET /api/chat/questions-summary`

## 3. Health/Ready

- `GET /health`
- `GET /ready`

## 4. 대표 응답 예시

### 4.1 `POST /api/knowledgebase/ask`

```json
{
  "answer": "인증서 점검 순서는 다음과 같습니다...",
  "topSimilarity": 0.858,
  "isLowSimilarity": false,
  "topMatchedQuestion": "인증서가 안보여",
  "topMatchedKbTitle": "인증서 사용 안내",
  "relatedKBs": [
    {
      "id": 43,
      "title": "인증서 사용 안내",
      "similarity": 0.858,
      "isSelected": true
    }
  ],
  "retrievalDiagnostics": {
    "similarityThreshold": 0.5,
    "candidates": []
  },
  "sessionId": 12
}
```

### 4.2 `GET /api/chat/questions-summary`

```json
{
  "totalQuestions": 120,
  "topQuestions": [],
  "topKeywords": [],
  "topReferencedKbs": [],
  "dailyCounts": [],
  "totalAnswers": 118,
  "avgSimilarity": 0.81,
  "lowSimilarityCount": 9,
  "lowSimilarityRate": 0.076,
  "highConfidenceRate": 0.64
}
```
