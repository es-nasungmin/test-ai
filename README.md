# CRM 상담관리 + RAG 챗봇 프로젝트

ASP.NET Core 백엔드와 Vue 프론트엔드로 구성된 CRM 상담관리 시스템입니다.
상담내역 저장 시 AI가 문제/해결을 추출해 KB를 만들고, 챗봇이 유사 사례를 검색해 답변합니다.

## 프로젝트 구조

```text
AITestPJ/
├── CrmApi/                   # ASP.NET Core API (백엔드)
│   ├── Controllers/          # Customer/Interaction/KnowledgeBase API
│   ├── Data/                 # CrmContext (EF Core DbContext)
│   ├── Models/               # Customer/Interaction/KnowledgeBase 모델
│   ├── Services/             # 추출/임베딩/RAG 서비스
│   ├── Program.cs            # DI, DB 초기화, HTTP 파이프라인
│   ├── appsettings.json      # OpenAI/Gemini 키 및 서버 설정
│   ├── CrmApi.csproj
│   └── crm.db                # SQLite DB 파일
└── CrmClient/                # Vue 3 + Vite 프론트엔드
    ├── src/
    │   ├── components/CRM/   # CRM 화면 컴포넌트
    │   ├── components/FloatingChatbot.vue
    │   ├── views/CRMPage.vue
    │   ├── App.vue
    │   └── main.js
    └── package.json
```

## 실행 방법

### 1) 백엔드 실행

```bash
cd CrmApi
dotnet run
```

- 기본 주소: http://localhost:8080
- DB: CrmApi/crm.db

### 2) 프론트 실행

```bash
cd CrmClient
npm install
npm run dev
```

- 기본 주소: http://localhost:5173

### 3) 권장: 스크립트로 한 번에 실행/중지

```bash
cd /Users/nasungmin/Projects/AITestPJ

# 백엔드 + 프론트 한 번에 실행
./scripts/run-all.sh

# 전체 중지
./scripts/stop-all.sh
```

- `run-all.sh`는 8080/5173 포트가 이미 사용 중이면 기존 프로세스를 종료한 뒤 재실행합니다.
- 로그 파일:
    - 백엔드: `/tmp/crmapi.log`
    - 프론트: `/tmp/crmclient.log`

개별 실행 스크립트:

```bash
./scripts/run-backend.sh
./scripts/run-frontend.sh
```

## 핵심 기능

- 고객(업체) 등록/수정/삭제
- 상담내역 등록/수정/삭제/완료 처리
- 상담 저장 후 KB 자동 추출(문제/해결)
- 문제 텍스트 임베딩 저장(벡터)
- 질문 임베딩 기반 유사 사례 검색(RAG)
- 충돌 답변 감지 + 다수결/유사도 합산 우선 규칙

## 주요 API

### Customer
- GET /api/customer
- GET /api/customer/{id}
- POST /api/customer
- PUT /api/customer/{id}
- DELETE /api/customer/{id}

### Interaction
- GET /api/interaction
- GET /api/interaction/{id}
- POST /api/interaction
- PUT /api/interaction/{id}
- DELETE /api/interaction/{id}
- PATCH /api/interaction/{id}/complete

### KnowledgeBase / Chatbot
- POST /api/knowledgebase/ask
- GET /api/knowledgebase/list
- GET /api/knowledgebase/{id}
- DELETE /api/knowledgebase/{id}

## 운영 메모

- 오래된 Todo 기능과 관련 파일은 제거되었습니다.
- todo.db는 사용하지 않으며, 현재 시스템은 crm.db만 사용합니다.
- 상담 저장 직후 KB 분석은 비동기 처리이므로 반영까지 수 초 걸릴 수 있습니다.
