# API 명세서

Base URL: `http://localhost:8080`  
인증: `Authorization: Bearer {JWT토큰}` (로그인 후 발급)

---

## Auth

### POST `/api/auth/login`
```json
// Request
{ "username": "admin", "password": "password" }

// Response 200
{ "success": true, "token": "eyJ...", "user": { "id": 1, "username": "admin", "role": "admin" } }
```

### POST `/api/auth/register`
```json
// Request
{ "username": "user1", "email": "user1@example.com", "password": "password" }

// Response 200
{ "success": true, "message": "가입 완료. 관리자 승인 대기 중." }
```

### POST `/api/auth/validate`
```json
// Request Header: Authorization: Bearer {token}
// Response 200
{ "valid": true, "username": "admin", "role": "admin" }
```

### GET `/api/auth/users`
관리자 전용. 전체 사용자 목록 반환.

### GET `/api/auth/pending-users`
승인 대기 중인 사용자 목록.

### POST `/api/auth/approve-user/{userId}`
사용자 승인 처리.

### POST `/api/auth/reject-user/{userId}`
사용자 거부/비활성화 처리.

---

## Customer

### GET `/api/customer`
전체 고객 목록 (Interactions 포함, LastContactDate 내림차순).

### GET `/api/customer/{id}`
단건 조회.

### POST `/api/customer`
```json
{
  "name": "홍길동",
  "phoneNumber": "010-1234-5678",
  "email": "hong@example.com",
  "company": "(주)예시",
  "position": "팀장",
  "notes": "VIP 고객",
  "status": "Active"
}
```

### PUT `/api/customer/{id}`
위와 동일한 구조로 전체 업데이트.

### DELETE `/api/customer/{id}`
삭제 (연결된 Interactions cascade 삭제).

---

## Interaction (상담)

### GET `/api/interaction`
전체 상담 목록.

### GET `/api/interaction/{id}`
단건 조회.

### POST `/api/interaction`
```json
{
  "customerId": 1,
  "type": "Call",
  "content": "제품 설치 문의",
  "outcome": "원격 지원 예약",
  "scheduledDate": "2026-04-20T10:00:00Z"
}
```
> `type`: `Call` | `Email` | `Meeting` | `Note`

### PUT `/api/interaction/{id}`
전체 업데이트.

### DELETE `/api/interaction/{id}`
삭제.

### PATCH `/api/interaction/{id}/complete`
완료 처리 (`IsCompleted = true`).

### POST `/api/interaction/summarize`
```json
// Request
{ "content": "상담 내용 텍스트..." }
// Response
{ "summary": "요약된 내용" }
```

### POST `/api/interaction/{id}/summarize`
특정 상담 ID 기준 AI 요약.

### POST `/api/interaction/customer/{customerId}/summarize-all`
고객의 전체 상담 이력을 종합 요약.

### GET `/api/interaction/prompt-template`
현재 요약 프롬프트 템플릿 조회.

### PUT `/api/interaction/prompt-template`
요약 프롬프트 템플릿 수정.

---

## KnowledgeBase

### POST `/api/knowledgebase/ask`
RAG 챗봇 질문.
```json
// Request
{
  "question": "설치가 안돼요",
  "role": "user",
  "platform": "web",
  "sessionId": 1,
  "historyTurnCount": 6,
  "noSave": false,
  "promptOverride": {
    "systemPrompt": null,
    "rulesPrompt": null,
    "similarityThreshold": null
  }
}

// Response
{
  "answer": "답변 텍스트",
  "relatedKbIds": [1, 2],
  "relatedKbMeta": [{ "id": 1, "similarity": 0.92 }],
  "relatedDocumentMeta": [],
  "topSimilarity": 0.92,
  "isLowSimilarity": false,
  "sessionId": 1
}
```

### POST `/api/knowledgebase`
KB 생성.
```json
{
  "title": "설치 오류 해결",
  "problem": "프로그램이 설치되지 않습니다",
  "solution": "관리자 권한으로 실행 후 재설치",
  "visibility": "user",
  "platform": "windows",
  "keywords": "설치,오류,권한"
}
```

### PUT `/api/knowledgebase/{id}`
KB 수정 (동일 구조).

### GET `/api/knowledgebase/{id}`
KB 단건 조회 (SimilarQuestions 포함).

### DELETE `/api/knowledgebase/{id}`
KB 삭제 (Qdrant 벡터 포함).

### GET `/api/knowledgebase/list`
```
Query: ?page=1&pageSize=20&visibility=user&platform=windows&keyword=설치
```

### GET `/api/knowledgebase/stats`
KB 통계 (총 개수, visibility별 분포 등).

### POST `/api/knowledgebase/generate-similar-questions`
```json
{ "problem": "설치가 안됩니다", "count": 5 }
// Response: { "questions": ["설치 실패", "설치 오류", ...] }
```

### POST `/api/knowledgebase/generate-keywords`
```json
{ "problem": "...", "solution": "..." }
// Response: { "keywords": "설치,오류,권한" }
```

### POST `/api/knowledgebase/refine-solution`
```json
{ "problem": "...", "solution": "초안 답변" }
// Response: { "refinedSolution": "다듬어진 답변" }
```

### GET/PUT `/api/knowledgebase/chatbot-prompt-template`
챗봇 응답 생성 시 사용하는 시스템/룰 프롬프트 조회 및 수정.

### GET/PUT `/api/knowledgebase/writer-prompt-template`
KB 작성 보조(키워드 추출, 답변 다듬기) 프롬프트 조회 및 수정.

---

## Document KB

### POST `/api/knowledgebase/documents/upload`
```
Content-Type: multipart/form-data
file: (PDF 또는 텍스트 파일)
visibility: user
platform: 공통
```

### GET `/api/knowledgebase/documents`
```
Query: ?page=1&pageSize=20&visibility=user&platform=공통
```

### PUT `/api/knowledgebase/documents/{id}`
```json
{ "displayName": "새 이름", "visibility": "admin", "platform": "windows" }
```

### DELETE `/api/knowledgebase/documents/{id}`
문서 삭제 (파일 + 청크 + Qdrant 벡터 포함).

### POST `/api/knowledgebase/documents/{id}/reindex`
문서 재파싱 및 벡터 재인덱싱.

### GET `/api/knowledgebase/documents/{id}/download`
원본 파일 다운로드.

---

## Chat

### GET `/api/chat/sessions`
```
Query: ?role=admin&platform=web&page=1&pageSize=10
```

### POST `/api/chat/sessions`
```json
{ "title": "새 대화", "userRole": "user", "platform": "web" }
```

### GET `/api/chat/sessions/{id}`
세션 정보 + 전체 메시지 목록 반환.

### DELETE `/api/chat/sessions/{id}`
세션 삭제 (메시지 cascade 삭제).

### GET `/api/chat/questions-summary`
유사도 미달 질문(LowSimilarityQuestions) 요약 통계.
