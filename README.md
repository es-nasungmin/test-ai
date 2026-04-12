# AiDesk

ASP.NET Core 백엔드와 Vue 프론트엔드로 구성된 AI 상담관리 및 지식베이스 시스템입니다.  
상담내역 저장 시 AI가 문제/해결을 추출해 지식베이스(KB)를 만들고,  
챗봇이 유사 사례를 벡터 검색(RAG)으로 찾아 답변합니다.  
관리자와 일반 사용자에 따라 접근 가능한 KB와 챗봇 UI가 분리됩니다.

---

## 전체 흐름

```
[상담원이 상담내역 저장]
    ↓
[AI가 문제/해결 자동 추출 → KB 저장]
  sourceType = "case"
  visibility = "internal"   ← 관리자만 볼 수 있음
  isApproved = false
    ↓
[상담내역에서 외부제공 ON]
    ├─ ✅ ON  → 해당 상담에서 추출된 case KB를 common으로 전환
    │         ↓
    │   일반 사용자 챗봇도 참고 가능
    └─ ❌ OFF → internal 유지 (관리자 챗봇만 참고)

[관리자가 공식 KB 직접 작성]
  sourceType = "official"
  visibility = "common" (즉시 공개) 또는 "internal"
  isApproved = true
    ↓
  같은 의미의 정보가 있으면 공식 KB에 가중치 적용 (우선 참고)
```

---

## 권한 구조

| 구분 | 관리자 (admin) | 일반 사용자 (user) |
|------|--------------|------------------|
| 챗봇 위치 | 우측 하단 두 번째 🛠️ | 우측 하단 첫 번째 🤖 |
| 챗봇 색상 | 주황/빨강 | 보라/파랑 |
| 참고 가능 KB | **전체** (internal + common) | **공개만** (common + isApproved=true) |
| KB 관리 탭 | ✅ 접근 가능 (공식 KB 전용) | ❌ 없음 |
| 채팅 이력 저장 | ✅ (admin 세션) | ✅ (user 세션) |
| 공식 KB 작성 | ✅ | ❌ |

### KB 필드 설명

| 필드 | 값 | 의미 |
|------|----|------|
| `sourceType` | `case` | 상담내역에서 AI 자동 추출 |
| `sourceType` | `official` | 관리자가 직접 작성한 공식 답변 |
| `visibility` | `internal` | 관리자 챗봇만 참고 (기본값) |
| `visibility` | `common` | 일반 사용자 챗봇도 참고 가능 |
| `isApproved` | `false` | 검토 대기 상태 (내부 참고용) |
| `isApproved` | `true` | 승인 완료 (공개 가능) |

---

## 프로젝트 구조

```text
AiDesk/
├── AiDeskApi/                        # ASP.NET Core API (백엔드)
│   ├── Controllers/
│   │   ├── CustomerController.cs
│   │   ├── InteractionController.cs
│   │   ├── KnowledgeBaseController.cs  # KB CRUD + ask + official + approve
│   │   └── ChatController.cs           # 채팅 세션 관리
│   ├── Data/
│   │   └── AiDeskContext.cs            # EF Core DbContext
│   ├── Models/
│   │   ├── Customer.cs
│   │   ├── Interaction.cs
│   │   ├── KnowledgeBase.cs            # visibility/sourceType/isApproved 포함
│   │   ├── ChatSession.cs              # 채팅 세션 모델
│   │   └── ChatMessage.cs             # 채팅 메시지 모델
│   ├── Services/
│   │   ├── OpenAiRagService.cs         # RAG + visibility 필터
│   │   ├── OpenAiEmbeddingService.cs
│   │   ├── KnowledgeExtractorService.cs
│   │   ├── GptService.cs
│   │   └── GeminiService.cs
│   ├── Program.cs                      # DI + DB 초기화 (raw SQL)
│   └── aidesk.db                       # SQLite DB
└── AiDeskClient/                     # Vue 3 + Vite 프론트엔드
    └── src/
    ├── components/
  │   ├── Management/
    │   │   ├── CustomerList.vue
    │   │   ├── CustomerForm.vue
    │   │   ├── InteractionList.vue
    │   │   ├── InteractionForm.vue
  │   │   └── KBManagement.vue       # KB 목록/승인/공식작성
    │   └── FloatingChatbot.vue     # role prop으로 admin/user 분기
  ├── views/ManagementPage.vue      # 운영관리 탭 + KB관리 탭
    └── App.vue
```

## 실행 방법

### 1) 백엔드 실행

```bash
cd AiDeskApi
dotnet run
```

- 기본 주소: http://localhost:8080
- DB: AiDeskApi/aidesk.db

### 2) 프론트 실행

```bash
cd AiDeskClient
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
    - 백엔드: `/tmp/aideskapi.log`
    - 프론트: `/tmp/aideskclient.log`

개별 실행 스크립트:

```bash
./scripts/run-backend.sh
./scripts/run-frontend.sh
```

## 핵심 기능

- 고객/업체 등록·수정·삭제
- 상담내역 등록/수정/삭제/완료 처리
- 상담 저장 후 KB 자동 추출(문제/해결) → `sourceType=case, visibility=internal`
- 문제 텍스트 임베딩 저장(text-embedding-3-small 벡터)
- 질문 임베딩 기반 유사 사례 검색(RAG, 코사인 유사도 Top-3)
- 충돌 답변 감지 + 다수결/유사도 합산 우선 규칙
- **상담내역 외부제공 토글**: 상담 추출 KB(case)의 공개/내부 전환
- **KB 관리 탭**: 공식 KB 작성/편집/삭제 · 공개 범위 토글
- **채팅 세션 저장**: 대화 이력 DB 보관, 다음 접속 시 이어서 대화 가능
- **챗봇 분리**: 관리자 챗봇(전체 KB) / 일반 사용자 챗봇(공개 KB만)
- **공식 KB 우선 정책**: 유사도 점수에 공식 KB 15% 가중치 적용

## 주요 API

### Customer
- `GET    /api/customer`
- `GET    /api/customer/{id}`
- `POST   /api/customer`
- `PUT    /api/customer/{id}`
- `DELETE /api/customer/{id}`

### Interaction
- `GET    /api/interaction`
- `POST   /api/interaction`
- `PUT    /api/interaction/{id}`
- `DELETE /api/interaction/{id}`
- `PATCH  /api/interaction/{id}/complete`

### KnowledgeBase
- `POST   /api/knowledgebase/ask`                   — RAG 질문 (`role`, `sessionId` 포함)
- `POST   /api/knowledgebase/interaction/{id}/extract` — 상담에서 KB 추출
- `POST   /api/knowledgebase/save`                  — 추출 KB 저장
- `POST   /api/knowledgebase/official`              — 공식 KB 직접 작성 (관리자)
- `PUT    /api/knowledgebase/{id}/approve`          — KB 공개 승인 (관리자)
- `PUT    /api/knowledgebase/{id}/visibility`       — 공개/내부 토글 (관리자)
- `GET    /api/knowledgebase/list`                  — KB 목록
- `DELETE /api/knowledgebase/{id}`                  — KB 삭제

### Chat Session
- `GET    /api/chat/sessions`        — 세션 목록 (role 필터 가능)
- `POST   /api/chat/sessions`        — 새 세션 생성
- `GET    /api/chat/sessions/{id}`   — 세션 + 메시지 전체 조회
- `DELETE /api/chat/sessions/{id}`   — 세션 삭제 (메시지 cascade)

---

## DB 테이블 구조

| 테이블 | 주요 컬럼 |
|--------|-----------|
| `Customers` | Id, Name, PhoneNumber, Email, Company, Status |
| `Interactions` | Id, CustomerId, Type, Content, Outcome, IsCompleted |
| `KnowledgeBases` | Id, Problem, Solution, ProblemEmbedding, Visibility, SourceType, IsApproved, ApprovedBy |
| `ChatSessions` | Id, Title, UserRole, CreatedAt, UpdatedAt, MessageCount |
| `ChatMessages` | Id, SessionId, Role, Content, RelatedKbIds |

---

## 운영 메모

- `appsettings.Development.json`에 실제 API 키를 입력하세요. `appsettings.json`의 키는 플레이스홀더입니다.
- DB는 앱 시작 시 자동 생성됩니다 (EF Core `EnsureCreated` + raw SQL).
- 기존 DB에 새 컬럼이 없으면 `ALTER TABLE`이 자동 실행됩니다 (idempotent).
- 상담 저장 직후 KB 분석은 비동기 처리이므로 반영까지 수 초 걸릴 수 있습니다.
- 현재 role은 파라미터로 전달합니다 (별도 로그인 시스템 없음).
