# AiDesk

> ASP.NET Core 10 + Vue 3 기반 AI 고객 상담 지원 시스템
> JWT 인증, RAG 챗봇, 지식베이스(KB) 관리, 문서 벡터 검색을 제공합니다.

---

## 목차

- [아키텍처 개요](#아키텍처-개요)
- [권한 구조](#권한-구조)
- [프로젝트 구조](#프로젝트-구조)
- [핵심 기능](#핵심-기능)
- [실행 방법](#실행-방법)
- [주요 API](#주요-api)
- [운영 메모](#운영-메모)
- [관련 문서](#관련-문서)

---

## 아키텍처 개요

```
[Vue 3 클라이언트]  ←→  [ASP.NET Core API]  ←→  [SQLite / MSSQL]
                                ↕
                        [OpenAI Embedding]
                        [GPT-4o-mini / Gemini]
                        [Qdrant 벡터 DB]
```

**데이터 흐름 (KB 자동 생성)**

```
상담 저장
  └─▶ AI가 문제/해결 자동 추출 (KnowledgeExtractorService)
        └─▶ KB 저장 + 임베딩 생성
              └─▶ Qdrant 벡터 인덱싱

챗봇 질문
  └─▶ 질문 임베딩
        └─▶ Qdrant 벡터 검색 (Top-K)
              └─▶ 키워드 리랭킹 + 문서 청크 병합
                    └─▶ GPT 답변 생성 (RAG)
```

---

## 권한 구조

| 구분 | 관리자 (`admin`) | 일반 사용자 (`user`) |
|------|:-:|:-:|
| 로그인 | JWT (관리자 승인 필요) | JWT (관리자 승인 필요) |
| KB 열람 | 전체 (`admin` + `user` visibility) | 공개 KB만 (`user` visibility) |
| KB 작성 / 수정 / 삭제 | ✅ | ❌ |
| 문서 KB 업로드 | ✅ | ❌ |
| 고객 / 상담 관리 | ✅ | ❌ |
| 챗봇 이용 | ✅ (전체 KB 참고) | ✅ (공개 KB만 참고) |
| 채팅 이력 열람 | ✅ | 본인 세션만 |

**KB `visibility` 값**

| 값 | 의미 |
|----|------|
| `admin` | 관리자 챗봇만 참고 (기본값) |
| `user` | 관리자 + 일반 사용자 모두 참고 가능 |

---

## 프로젝트 구조

```text
AIDeskPJ/
├── AiDeskApi/                          # ASP.NET Core 10 백엔드
│   ├── Controllers/
│   │   ├── AuthController.cs           # 로그인 · 회원가입 · 사용자 승인
│   │   ├── CustomerController.cs       # 고객 CRUD
│   │   ├── InteractionController.cs    # 상담 CRUD + AI 요약
│   │   ├── KnowledgeBaseController.cs  # KB CRUD + RAG + 문서 KB
│   │   └── ChatController.cs           # 채팅 세션 관리
│   ├── Data/
│   │   ├── AiDeskContext.cs            # EF Core DbContext
│   │   └── DatabaseInitializer.cs      # DB 자동 초기화 (관리자 계정 포함)
│   ├── Models/                         # 도메인 모델 12개
│   ├── Services/
│   │   ├── OpenAiRagService.cs         # RAG 파이프라인 (벡터 + 키워드)
│   │   ├── OpenAiEmbeddingService.cs   # text-embedding-3-small
│   │   ├── QdrantVectorSearchService.cs# Qdrant HTTP 클라이언트
│   │   ├── KnowledgeExtractorService.cs# 상담 → KB 자동 추출
│   │   ├── DocumentKnowledgeService.cs # PDF/텍스트 문서 파싱 + 청킹
│   │   ├── GptService.cs               # OpenAI Chat
│   │   └── GeminiService.cs            # Google Gemini Chat
│   ├── appsettings.json                # 공통 설정 (플레이스홀더)
│   ├── appsettings.Development.json    # 개발용 실제 키 입력
│   └── Program.cs                      # DI 등록 + 미들웨어
│
├── AiDeskClient/                       # Vue 3 + Vite 프론트엔드
│   ├── src/
│   │   ├── views/
│   │   │   ├── LoginPage.vue           # JWT 로그인
│   │   │   └── ManagementPage.vue      # 운영 관리 메인
│   │   ├── components/
│   │   │   ├── FloatingChatbot.vue     # 챗봇 UI (admin/user 분기)
│   │   │   └── Management/             # 고객 · 상담 · KB 관리 컴포넌트
│   │   ├── api.js                      # Axios 기반 API 클라이언트
│   │   └── config.js                   # VITE_API_BASE_URL 설정
│   └── public/
│       ├── chat-widget.js              # 외부 사이트 임베드용 챗봇 위젯
│       └── chat-widget-example.html    # 위젯 사용 예시
│
├── docs/
│   ├── erd.md                          # DB ERD (Mermaid)
│   ├── rag-kb-process.md               # RAG / KB 처리 흐름 명세
│   ├── api-spec.md                     # API 전체 명세
│   └── setup.md                        # 환경 설정 가이드
│
└── scripts/
    ├── run-all.sh                      # 백엔드 + 프론트 한 번에 실행
    ├── run-backend.sh
    ├── run-frontend.sh
    └── stop-all.sh
```

---

## 핵심 기능

### 고객 · 상담 관리
- 고객 등록 / 수정 / 삭제
- 상담 이력 등록 · 완료 처리 · AI 요약

### 지식베이스 (KB)
- 상담 저장 후 AI가 문제/해결 자동 추출 → KB 생성
- 관리자가 KB 직접 작성 / 수정 / 삭제
- `visibility` 토글로 공개 범위 조정
- 유사 질문 자동 생성 및 벡터 인덱싱

### 문서 KB
- PDF · 텍스트 파일 업로드 → OCR → 청크 분할 → 벡터 인덱싱
- RAG 답변 시 문서 청크를 KB와 함께 참고

### RAG 챗봇
- 벡터 검색(Qdrant) + 키워드 리랭킹 혼합 전략
- 세션별 대화 이력 유지
- 유사도 미달 질문 자동 기록 (LowSimilarityQuestions)
- 외부 사이트에 JS 위젯으로 임베드 가능

### 인증
- JWT 기반 로그인 / 회원가입
- 관리자 승인 후 계정 활성화

---

## 실행 방법

### 사전 요구사항

| 항목 | 버전 |
|------|------|
| .NET SDK | 10.0 이상 |
| Node.js | 18 이상 |
| Qdrant | 로컬 실행 또는 클라우드 |

> 환경 변수 및 API 키 설정은 [docs/setup.md](docs/setup.md)를 참고하세요.

### 스크립트로 한 번에 실행 (권장)

```bash
# 백엔드(8080) + 프론트(5173) 동시 실행
./scripts/run-all.sh

# 전체 중지
./scripts/stop-all.sh
```

- 포트가 이미 사용 중이면 기존 프로세스를 종료 후 재실행합니다.
- 로그: `/tmp/aideskapi.log`, `/tmp/aideskclient.log`

### 개별 실행

```bash
# 백엔드
./scripts/run-backend.sh
# 또는
cd AiDeskApi && dotnet run

# 프론트
./scripts/run-frontend.sh
# 또는
cd AiDeskClient && npm install && npm run dev
```

| 서비스 | 주소 |
|--------|------|
| 백엔드 API | http://localhost:8080 |
| 프론트엔드 | http://localhost:5173 |
| Qdrant | http://localhost:6333 |

---

## 주요 API

> 전체 상세 명세는 [docs/api-spec.md](docs/api-spec.md)를 참고하세요.

### Auth
| 메서드 | 경로 | 설명 |
|--------|------|------|
| POST | `/api/auth/login` | 로그인 (JWT 발급) |
| POST | `/api/auth/register` | 회원가입 |
| POST | `/api/auth/validate` | 토큰 유효성 확인 |
| GET  | `/api/auth/users` | 전체 사용자 목록 (관리자) |
| GET  | `/api/auth/pending-users` | 승인 대기 사용자 |
| POST | `/api/auth/approve-user/{userId}` | 사용자 승인 |
| POST | `/api/auth/reject-user/{userId}` | 사용자 거부 |

### Customer
| 메서드 | 경로 | 설명 |
|--------|------|------|
| GET    | `/api/customer` | 목록 조회 |
| GET    | `/api/customer/{id}` | 단건 조회 |
| POST   | `/api/customer` | 등록 |
| PUT    | `/api/customer/{id}` | 수정 |
| DELETE | `/api/customer/{id}` | 삭제 |

### Interaction (상담)
| 메서드 | 경로 | 설명 |
|--------|------|------|
| GET    | `/api/interaction` | 목록 조회 |
| GET    | `/api/interaction/{id}` | 단건 조회 |
| POST   | `/api/interaction` | 등록 |
| PUT    | `/api/interaction/{id}` | 수정 |
| DELETE | `/api/interaction/{id}` | 삭제 |
| PATCH  | `/api/interaction/{id}/complete` | 완료 처리 |
| POST   | `/api/interaction/summarize` | AI 요약 |
| POST   | `/api/interaction/{id}/summarize` | 특정 상담 AI 요약 |
| POST   | `/api/interaction/customer/{customerId}/summarize-all` | 고객 전체 요약 |
| GET    | `/api/interaction/prompt-template` | 요약 프롬프트 조회 |
| PUT    | `/api/interaction/prompt-template` | 요약 프롬프트 수정 |

### KnowledgeBase
| 메서드 | 경로 | 설명 |
|--------|------|------|
| POST   | `/api/knowledgebase/ask` | RAG 챗봇 질문 |
| POST   | `/api/knowledgebase` | KB 생성 |
| PUT    | `/api/knowledgebase/{id}` | KB 수정 |
| GET    | `/api/knowledgebase/{id}` | KB 단건 조회 |
| DELETE | `/api/knowledgebase/{id}` | KB 삭제 |
| GET    | `/api/knowledgebase/list` | KB 목록 조회 |
| GET    | `/api/knowledgebase/stats` | KB 통계 |
| POST   | `/api/knowledgebase/generate-similar-questions` | 유사 질문 생성 |
| POST   | `/api/knowledgebase/generate-keywords` | 키워드 생성 |
| POST   | `/api/knowledgebase/refine-solution` | 답변 다듬기 |
| GET    | `/api/knowledgebase/chatbot-prompt-template` | 챗봇 프롬프트 조회 |
| PUT    | `/api/knowledgebase/chatbot-prompt-template` | 챗봇 프롬프트 수정 |
| GET    | `/api/knowledgebase/writer-prompt-template` | 작성 프롬프트 조회 |
| PUT    | `/api/knowledgebase/writer-prompt-template` | 작성 프롬프트 수정 |

### Document KB
| 메서드 | 경로 | 설명 |
|--------|------|------|
| POST   | `/api/knowledgebase/documents/upload` | 문서 업로드 |
| GET    | `/api/knowledgebase/documents` | 문서 목록 |
| PUT    | `/api/knowledgebase/documents/{id}` | 문서 정보 수정 |
| DELETE | `/api/knowledgebase/documents/{id}` | 문서 삭제 |
| POST   | `/api/knowledgebase/documents/{id}/reindex` | 문서 재인덱싱 |
| GET    | `/api/knowledgebase/documents/{id}/download` | 문서 다운로드 |

### Chat
| 메서드 | 경로 | 설명 |
|--------|------|------|
| GET    | `/api/chat/sessions` | 세션 목록 (role/platform 필터) |
| POST   | `/api/chat/sessions` | 세션 생성 |
| GET    | `/api/chat/sessions/{id}` | 세션 + 메시지 조회 |
| DELETE | `/api/chat/sessions/{id}` | 세션 삭제 |
| GET    | `/api/chat/questions-summary` | 질문 요약 통계 |

---

## 운영 메모

- **API 키**: `appsettings.Development.json`에 실제 키를 입력하세요. `appsettings.json`은 플레이스홀더입니다.
- **CORS**: 운영/스테이징 환경에서는 `appsettings.Production.json`의 `Cors:AllowedOrigins`를 실제 도메인으로 교체하세요.
- **DB 초기화**: 앱 시작 시 자동 생성됩니다. `aidesk.db` 삭제 후 재시작하면 관리자 계정만 재생성됩니다.
- **Qdrant**: `aidesk_kb` 컬렉션을 삭제하면 모든 벡터 인덱스가 초기화됩니다. KB 재인덱싱이 필요합니다.
- **프론트 API 주소**: `AiDeskClient/.env`의 `VITE_API_BASE_URL`로 설정합니다. (기본값: `/api`)

---

## 관련 문서

| 문서 | 설명 |
|------|------|
| [docs/erd.md](docs/erd.md) | DB 전체 ERD (Mermaid) |
| [docs/rag-kb-process.md](docs/rag-kb-process.md) | RAG / KB 처리 흐름 상세 명세 |
| [docs/api-spec.md](docs/api-spec.md) | API 요청/응답 상세 명세 |
| [docs/setup.md](docs/setup.md) | 환경 설정 및 외부 서비스 연동 가이드 |
