# AiDesk

> ASP.NET Core 10 + Vue 3 기반 KB/RAG 챗봇 시스템
> 지식베이스 관리, 문서 벡터 검색, 채팅 로그/질문 분석 기능을 제공합니다.

---

## 목차

- [프로젝트 개요](#프로젝트-개요)
- [빠른 시작](#빠른-시작)
- [기술 스택](#기술-스택)
- [아키텍처](#아키텍처)
- [프로젝트 구조](#프로젝트-구조)
- [핵심 기능](#핵심-기능)
- [주요 API](#주요-api)
- [운영 메모](#운영-메모)
- [관련 문서](#관련-문서)

---

## 프로젝트 개요

이 저장소는 KB 중심 RAG 챗봇 운영을 위한 웹 애플리케이션입니다.

주요 범위:
- KB 생성/수정/삭제 및 공개 범위 관리
- 예상질문/키워드 기반 임베딩 및 벡터 인덱싱
- 사용자 질문에 대한 하이브리드 검색(벡터 + 키워드 + rerank)
- 채팅 세션/메시지 관리 및 질문 분석 리포트
- 문서(PDF/텍스트) 기반 보조 근거 검색

주의:
- 이 README는 제출용으로 KB/채팅 범위만 설명합니다.

---

## 빠른 시작

사전 설정은 [docs/setup.md](docs/setup.md)를 참고하세요.

```bash
# 1) 프로젝트 루트에서 실행
./scripts/run-all.sh

# 2) 접속
# Frontend: http://localhost:5173
# Backend:  http://localhost:8080

# 3) 종료
./scripts/stop-all.sh
```

로그:
- /tmp/aideskapi.log
- /tmp/aideskclient.log

---

## 기술 스택

| 영역 | 스택 |
|------|------|
| Frontend | Vue 3, Vite, Axios |
| Backend | ASP.NET Core (.NET 10), EF Core, JWT |
| Database | SQLite(기본), MSSQL(설정 전환) |
| AI | OpenAI Embedding(text-embedding-3-small), OpenAI Chat(gpt-4o-mini), Gemini(보조) |
| Vector DB | Qdrant (Cosine) |

상세는 [docs/tech-stack.md](docs/tech-stack.md)를 참고하세요.

---

## 아키텍처

```text
[Vue 3 Client]
      <->
[ASP.NET Core API]
      <->
[SQLite / MSSQL]
      <->
[Qdrant]
      <->
[OpenAI Embedding + Chat]
```

RAG 처리 흐름:

```text
질문 입력
  -> 질문 정규화
  -> 임베딩 생성
  -> Qdrant 벡터 검색(top-k)
  -> 키워드 후보 확장
  -> GPT rerank
  -> 임계치 판정
  -> 답변 생성(또는 저유사도 안내)
```

상세 동작은 [docs/rag-kb-process.md](docs/rag-kb-process.md)를 참고하세요.

---

## 프로젝트 구조

```text
AIDeskPJ/
├── AiDeskApi/
│   ├── Controllers/
│   │   ├── KnowledgeBaseController.cs  # KB CRUD + ask + 문서 업로드
│   │   └── ChatController.cs           # 세션/메시지/질문분석
│   ├── Services/
│   │   ├── OpenAiRagService.cs         # RAG 파이프라인
│   │   ├── OpenAiEmbeddingService.cs   # 임베딩 생성
│   │   ├── QdrantVectorSearchService.cs# 벡터 검색/동기화
│   │   └── DocumentKnowledgeService.cs # 문서 파싱/청킹
│   ├── Data/
│   │   ├── AiDeskContext.cs
│   │   └── DatabaseInitializer.cs
│   └── Program.cs
│
├── AiDeskClient/
│   ├── src/
│   │   ├── components/Management/
│   │   │   ├── KBManagement.vue
│   │   │   └── ChatLogManagement.vue
│   │   └── views/ManagementPage.vue
│   └── public/chat-widget.js
│
├── docs/
│   ├── deploy.md
│   ├── rag-kb-process.md
│   ├── tech-stack.md
│   ├── api-spec.md
│   └── setup.md
│
└── scripts/
    ├── run-all.sh
    ├── run-backend.sh
    ├── run-frontend.sh
    └── stop-all.sh
```

---

## 핵심 기능

### 1) KB 관리
- KB 생성/수정/삭제
- Visibility(user/admin) 기반 공개 범위
- 플랫폼(공통/특정) 기반 필터
- 예상질문 최대 10개 관리

### 2) RAG 챗봇
- 질문 정규화 후 임베딩
- 벡터 검색 + 키워드 후보 확장 + GPT rerank
- 임계치 기반 저유사도 차단
- 문서형 KB 보조 검색

### 3) 채팅 로그/분석
- 채팅 세션 목록/상세/삭제
- 키워드/역할/플랫폼 필터
- 질문 분석 리포트(top referenced KB, 키워드, 일별 집계)

### 4) 문서형 KB
- 문서 업로드
- OCR/텍스트 추출
- 청크 임베딩 및 벡터 인덱싱

---

## 주요 API

상세 요청/응답은 [docs/api-spec.md](docs/api-spec.md)를 참고하세요.

### KnowledgeBase

| 메서드 | 경로 | 설명 |
|--------|------|------|
| POST | /api/knowledgebase/ask | RAG 챗봇 질문 |
| POST | /api/knowledgebase | KB 생성 |
| PUT | /api/knowledgebase/{id} | KB 수정 |
| GET | /api/knowledgebase/{id} | KB 단건 조회 |
| DELETE | /api/knowledgebase/{id} | KB 삭제 |
| GET | /api/knowledgebase/list | KB 목록 조회 |
| GET | /api/knowledgebase/stats | KB 통계 |
| POST | /api/knowledgebase/generate-similar-questions | 유사 질문 생성 |
| POST | /api/knowledgebase/generate-keywords | 키워드 생성 |
| POST | /api/knowledgebase/refine-solution | 답변 다듬기 |
| GET | /api/knowledgebase/chatbot-prompt-template | 챗봇 프롬프트 조회 |
| PUT | /api/knowledgebase/chatbot-prompt-template | 챗봇 프롬프트 수정 |
| GET | /api/knowledgebase/writer-prompt-template | 작성 프롬프트 조회 |
| PUT | /api/knowledgebase/writer-prompt-template | 작성 프롬프트 수정 |

### Document KB

| 메서드 | 경로 | 설명 |
|--------|------|------|
| POST | /api/knowledgebase/documents/upload | 문서 업로드 |
| GET | /api/knowledgebase/documents | 문서 목록 |
| PUT | /api/knowledgebase/documents/{id} | 문서 정보 수정 |
| DELETE | /api/knowledgebase/documents/{id} | 문서 삭제 |
| POST | /api/knowledgebase/documents/{id}/reindex | 문서 재인덱싱 |
| GET | /api/knowledgebase/documents/{id}/download | 문서 다운로드 |

### Chat

| 메서드 | 경로 | 설명 |
|--------|------|------|
| GET | /api/chat/sessions | 세션 목록 |
| POST | /api/chat/sessions | 세션 생성 |
| GET | /api/chat/sessions/{id} | 세션 + 메시지 조회 |
| DELETE | /api/chat/sessions/{id} | 세션 삭제 |
| GET | /api/chat/questions-summary | 질문 요약 통계 |

---

## 운영 메모

- API 키/DB 비밀번호는 커밋하지 말고 환경 변수/Secret Manager 사용을 권장합니다.
- Production에서는 CORS 허용 도메인을 명시해야 합니다.
- 앱 시작 시 DB 초기화/벡터 동기화가 수행됩니다.
- Qdrant 컬렉션(aidesk_kb) 삭제 시 벡터 인덱스를 재구성해야 합니다.
- 프론트 API 주소는 AiDeskClient/.env 의 VITE_API_BASE_URL 로 설정합니다.

---

## 관련 문서

| 문서 | 설명 |
|------|------|
| [docs/deploy.md](docs/deploy.md) | 개발/운영 서버 배포 가이드 |
| [docs/rag-kb-process.md](docs/rag-kb-process.md) | KB 생성부터 답변까지 RAG 상세 흐름 |
| [docs/tech-stack.md](docs/tech-stack.md) | 기술 스택/모델/인프라 요약 |
| [docs/api-spec.md](docs/api-spec.md) | API 요청/응답 상세 명세 |
| [docs/setup.md](docs/setup.md) | 로컬/개발 환경 설정 가이드 |
| [docs/erd.md](docs/erd.md) | DB ERD |
