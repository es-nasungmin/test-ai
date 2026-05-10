# 기술 스택 요약

## 1. 애플리케이션 구성

- Frontend: Vue 3 + Vite + Axios
- Backend: ASP.NET Core Web API (.NET 10)
- Database: SQLite(기본), MSSQL(선택)
- Vector DB: Qdrant
- LLM/AI: OpenAI (임베딩 + 챗)

## 2. Frontend

- Vue `^3.5.30`
- Vite `^8.0.1`
- `@vitejs/plugin-vue` `^6.0.5`
- Axios `^1.6.0`

주요 파일:
- `AiDeskClient/package.json`
- `AiDeskClient/src/main.js`

## 3. Backend

- TargetFramework: `net10.0`
- ORM: Entity Framework Core 10
- 인증: JWT Bearer
- API 문서: Swagger

주요 패키지:
- `Microsoft.AspNetCore.Authentication.JwtBearer` `10.0.5`
- `Microsoft.EntityFrameworkCore.Sqlite` `10.0.5`
- `Microsoft.EntityFrameworkCore.SqlServer` `10.0.5`
- `Swashbuckle.AspNetCore` `7.2.0`
- `UglyToad.PdfPig` `1.7.0-custom-5`

노트:
- PDF 문서 업로드 기능은 현재 API 경로에서 제거되었으며,
  PdfPig 패키지는 잔존 의존성일 수 있습니다(정리 대상).

## 4. RAG/검색

- Embedding 모델: `text-embedding-3-small`
- Chat 모델: `gpt-4o-mini`
- Vector Search: Qdrant Cosine
- 검색 흐름: document/expected 분리 검색 -> KB 병합 -> 조건부 rerank -> final 선택

핵심 설정 위치:
- `AiDeskApi/appsettings*.json`
- `AiDeskApi/Services/OpenAiRagService.cs`
- `AiDeskApi/Services/QdrantVectorSearchService.cs`
- `AiDeskApi/Services/ChatbotPromptTemplateService.cs`

## 5. 실행 스크립트

- 전체 실행: `scripts/run-all.sh`
- 백엔드 실행: `scripts/run-backend.sh`
- 프론트 실행: `scripts/run-frontend.sh`
- 전체 중지: `scripts/stop-all.sh`
- 100문항 벤치: `scripts/benchmark-100.js`
- 저장 질문 리플레이 벤치: `scripts/benchmark-live-questions.js`
