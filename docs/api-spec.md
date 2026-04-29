# API 명세서 (KB/채팅 범위)

Base URL: `http://localhost:8080`

인증:
- 기본적으로 JWT 기반 인증을 사용합니다.
- 헤더: `Authorization: Bearer {token}`

참고:
- 본 문서는 제출용으로 KB/문서/채팅 API만 포함합니다.
- 전체 시스템 API는 별도 문서에서 관리할 수 있습니다.

---

## 1. KnowledgeBase

### 1.1 POST `/api/knowledgebase/ask`
RAG 챗봇 질의.

Request
```json
{
  "question": "인증서 조회가 안됨",
  "role": "user",
  "platform": "공통",
  "sessionId": 123,
  "createSession": false,
  "noSave": false,
  "historyTurnCount": 6,
  "promptOverride": {
    "promptOnly": false,
    "systemPrompt": null,
    "rulesPrompt": null,
    "lowSimilarityMessage": null,
    "similarityThreshold": null
  }
}
```

Response 200 (예시)
```json
{
  "answer": "...",
  "topSimilarity": 0.44,
  "isLowSimilarity": false,
  "relatedKBs": [
    {
      "id": 3,
      "title": "인증서 저장위치",
      "solution": "...",
      "similarity": 0.44,
      "matchedQuestion": "인증서 저장위치",
      "isSelected": true
    }
  ],
  "relatedDocuments": [],
  "conflictDetected": false,
  "decisionRule": "단일 해법",
  "retrievalDiagnostics": {
    "similarityThreshold": 0.4,
    "questionTokens": ["인증서", "조회가", "안돼"],
    "candidates": []
  },
  "sessionId": 123
}
```

오류
- 400: question 누락
- 404: sessionId 지정했으나 세션 없음
- 500: 내부 오류
- 503: OpenAI API 키 문제

---

### 1.2 POST `/api/knowledgebase`
KB 생성.

Request
```json
{
  "title": "인증서 저장위치",
  "content": "인증서의 저장 위치는 다음과 같습니다...",
  "visibility": "user",
  "platforms": ["공통"],
  "keywords": "인증서 저장 위치, 윈도우 인증서 경로",
  "expectedQuestions": [
    "인증서는 어디에 저장되나요?",
    "윈도우 인증서 경로는?"
  ]
}
```

Response 200
```json
{ "id": 3, "message": "KB가 생성되었습니다." }
```

노트
- `platforms` 또는 `platform` 둘 다 허용
- `expectedQuestions` 최대 10개
- 임베딩 생성 후 벡터 인덱스 동기화 수행

---

### 1.3 PUT `/api/knowledgebase/{id}`
KB 수정.

Request: 생성과 동일 구조

Response 200
```json
{ "id": 3, "message": "KB가 수정되었습니다." }
```

---

### 1.4 GET `/api/knowledgebase/{id}`
KB 단건 조회.

Response 200 (요약)
```json
{
  "id": 3,
  "title": "인증서 저장위치",
  "content": "...",
  "visibility": "user",
  "platform": "공통",
  "platforms": ["공통"],
  "keywords": "...",
  "expectedQuestions": [
    { "id": 10, "question": "인증서는 어디에 저장되나요?" }
  ],
  "viewCount": 42,
  "createdAt": "...",
  "updatedAt": "..."
}
```

---

### 1.5 DELETE `/api/knowledgebase/{id}`
KB 삭제.

Response 200
```json
{ "message": "KB가 삭제되었습니다." }
```

노트
- DB 삭제 후 벡터 인덱스도 삭제 시도

---

### 1.6 GET `/api/knowledgebase/list`
KB 목록 조회.

Query
- `page` (기본 1)
- `pageSize` (기본 20, 최대 100)
- `keyword` (제목/내용/키워드/예상질문 검색)
- `visibility` (`user` | `admin`)
- `platform` (예: `공통`, `windows`)

Response 200
```json
{
  "total": 1,
  "page": 1,
  "pageSize": 20,
  "data": [
    {
      "id": 3,
      "title": "인증서 저장위치",
      "content": "...",
      "visibility": "user",
      "platform": "공통",
      "platforms": ["공통"],
      "keywords": "...",
      "expectedQuestions": [
        { "id": 10, "question": "인증서는 어디에 저장되나요?" }
      ]
    }
  ]
}
```

---

### 1.7 GET `/api/knowledgebase/stats`
KB 통계.

Response 200
```json
{
  "totalKBs": 10,
  "totalViews": 120,
  "byVisibility": [
    { "visibility": "user", "count": 8 },
    { "visibility": "admin", "count": 2 }
  ],
  "byPlatform": [
    { "platform": "공통", "count": 7 }
  ]
}
```

---

### 1.8 POST `/api/knowledgebase/generate-similar-questions`
유사 질문 생성.

Request
```json
{ "title": "인증서 저장위치", "content": "...", "count": 5 }
```

Response 200
```json
{ "items": ["...", "..."] }
```

노트
- `count`는 1~5 범위로 제한

---

### 1.9 POST `/api/knowledgebase/generate-keywords`
키워드 생성.

Request
```json
{
  "title": "인증서 저장위치",
  "content": "...",
  "expectedQuestions": ["..."],
  "count": 5
}
```

Response 200
```json
{
  "items": ["키워드1", "키워드2"],
  "combined": ["키워드1", "키워드2"]
}
```

---

### 1.10 POST `/api/knowledgebase/refine-solution`
답변 문구 정리.

Request
```json
{ "title": "...", "content": "초안 답변" }
```

Response 200
```json
{ "solution": "다듬어진 답변" }
```

---

### 1.11 GET/PUT `/api/knowledgebase/chatbot-prompt-template`
챗봇 프롬프트 템플릿 조회/수정.

GET Response 200
```json
{
  "userSystemPrompt": "...",
  "adminSystemPrompt": "...",
  "userRulesPrompt": "...",
  "adminRulesPrompt": "...",
  "userLowSimilarityMessage": "...",
  "adminLowSimilarityMessage": "...",
  "similarityThreshold": 0.4
}
```

PUT Request
```json
{
  "userSystemPrompt": "...",
  "adminSystemPrompt": "...",
  "userRulesPrompt": "...",
  "adminRulesPrompt": "...",
  "userLowSimilarityMessage": "...",
  "adminLowSimilarityMessage": "...",
  "similarityThreshold": 0.4
}
```

---

### 1.12 GET/PUT `/api/knowledgebase/writer-prompt-template`
KB 작성 보조 프롬프트 조회/수정.

---

### 1.13 GET `/api/knowledgebase/low-similarity-questions`
저유사도 문의 목록.

Query
- `includeResolved` (기본 false)
- `platform`
- `page` (기본 1)
- `pageSize` (기본 20)

Response 200
```json
{
  "total": 10,
  "page": 1,
  "pageSize": 20,
  "data": [
    {
      "id": 1,
      "question": "연차 신청은 어디서 해?",
      "role": "user",
      "platform": "공통",
      "topSimilarity": 0.0,
      "isResolved": false,
      "sessionId": 321,
      "createdAt": "..."
    }
  ]
}
```

---

## 2. Document KB

### 2.1 POST `/api/knowledgebase/documents/upload`
문서 업로드 및 인덱싱.

Content-Type: `multipart/form-data`
- `file` (필수, 현재 PDF만 허용)
- `displayName` (선택)
- `visibility` (선택, 기본 admin)
- `platform` (선택, 기본 공통)

Response 200
```json
{
  "documentId": 5,
  "displayName": "가이드 문서",
  "status": "ready",
  "chunkCount": 30
}
```

---

### 2.2 GET `/api/knowledgebase/documents`
문서 목록 조회.

Query
- `role` (기본 admin)
- `platform` (선택)

Response 200
```json
{ "data": [], "total": 0 }
```

---

### 2.3 PUT `/api/knowledgebase/documents/{id}`
문서 메타 수정.

Request
```json
{
  "displayName": "새 문서명",
  "visibility": "user",
  "platform": "공통"
}
```

Response 200
```json
{
  "message": "문서가 수정되었습니다.",
  "data": { "id": 5, "displayName": "새 문서명" }
}
```

---

### 2.4 DELETE `/api/knowledgebase/documents/{id}`
문서 삭제.

Response 200
```json
{ "message": "문서가 삭제되었습니다." }
```

---

### 2.5 POST `/api/knowledgebase/documents/{id}/reindex`
문서 재인덱싱.

Content-Type: `multipart/form-data`
- `file` (필수, PDF)

Response 200
```json
{
  "documentId": 5,
  "displayName": "가이드 문서",
  "status": "ready",
  "chunkCount": 32
}
```

---

### 2.6 GET `/api/knowledgebase/documents/{id}/download`
원본 PDF 다운로드.

Response
- 200: `application/pdf` 스트림
- 404: 원본 파일 없음

---

## 3. Chat

### 3.1 GET `/api/chat/sessions`
세션 목록 조회.

Query
- `role`
- `platform`
- `keyword` (actorName/title/message content 검색)
- `page` (기본 1)
- `pageSize` (기본 10, 최대 100)

Response 200
```json
{
  "total": 10,
  "page": 1,
  "pageSize": 10,
  "data": [
    {
      "id": 100,
      "title": "인증서 조회가 안됨",
      "userRole": "user",
      "actorName": "홍길동",
      "platform": "공통",
      "createdAt": "...",
      "updatedAt": "...",
      "messageCount": 8
    }
  ]
}
```

---

### 3.2 POST `/api/chat/sessions`
세션 생성.

Request
```json
{
  "title": "새 대화",
  "role": "user",
  "platform": "공통"
}
```

Response 200
```json
{
  "id": 101,
  "title": "새 대화",
  "userRole": "user",
  "platform": "공통",
  "createdAt": "..."
}
```

---

### 3.3 GET `/api/chat/sessions/{id}`
세션 상세 + 메시지.

Response 200 (요약)
```json
{
  "id": 101,
  "title": "...",
  "userRole": "user",
  "actorName": "홍길동",
  "platform": "공통",
  "messageCount": 4,
  "messages": [
    {
      "id": 1,
      "role": "user",
      "content": "인증서 조회가 안됨",
      "createdAt": "...",
      "relatedKbIds": null,
      "relatedKbMeta": null,
      "retrievalDebugMeta": null,
      "topSimilarity": null,
      "isLowSimilarity": false
    },
    {
      "id": 2,
      "role": "bot",
      "content": "...",
      "createdAt": "...",
      "relatedKbIds": "[3]",
      "relatedKbMeta": "[{...}]",
      "retrievalDebugMeta": "{...}",
      "topSimilarity": 0.44,
      "isLowSimilarity": false
    }
  ]
}
```

---

### 3.4 DELETE `/api/chat/sessions/{id}`
세션 삭제.

Response 200
```json
{ "message": "세션이 삭제되었습니다." }
```

---

### 3.5 GET `/api/chat/questions-summary`
질문 요약 리포트.

Query
- `days` (기본 7, 1~365)
- `top` (기본 10, 1~30)
- `role` (선택)
- `platform` (선택)

Response 200 (요약)
```json
{
  "days": 7,
  "from": "...",
  "to": "...",
  "totalQuestions": 29,
  "uniqueQuestions": 12,
  "uniqueReferencedKbs": 1,
  "rankingBasis": "kb-reference",
  "topReferencedKbs": [
    {
      "kbId": 3,
      "title": "인증서 저장위치",
      "count": 9,
      "lastReferencedAt": "..."
    }
  ],
  "topQuestions": [],
  "topKeywords": [],
  "dailyCounts": []
}
```

노트
- 참조 집계는 `isSelected=true` 기준 KB만 반영됩니다.

---

## 4. 상태 코드 공통

- 200: 정상
- 400: 요청 검증 실패
- 401/403: 인증/권한 문제
- 404: 리소스 없음
- 500: 서버 내부 오류
- 503: 외부 AI 서비스 설정/호출 문제
