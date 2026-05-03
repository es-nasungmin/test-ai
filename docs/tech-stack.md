# 기술 스택 요약

이 문서는 AiDesk 프로젝트의 현재 기술 스택을 빠르게 확인하기 위한 요약 문서입니다.

## 1. 애플리케이션 구성

- Frontend: Vue 3 + Vite + Axios
- Backend: ASP.NET Core Web API (.NET 10)
- Database: SQLite (기본), MSSQL (설정 전환)
- Vector DB: Qdrant
- LLM/AI: OpenAI (임베딩 + 답변)

## 2. Frontend 스택

- Framework: Vue `^3.5.30`
- Build Tool: Vite `^8.0.1`
- Vue Plugin: `@vitejs/plugin-vue` `^6.0.5`
- HTTP Client: Axios `^1.6.0`

주요 파일:
- `AiDeskClient/package.json`
- `AiDeskClient/src/main.js`
- `AiDeskClient/src/api.js`

## 3. Backend 스택

- Runtime/Framework: .NET `net10.0`
- Web API: ASP.NET Core
- ORM: Entity Framework Core 10
- 인증: JWT Bearer
- API 문서: Swagger (Swashbuckle)
- PDF 처리: PdfPig

주요 패키지:
- `Microsoft.AspNetCore.Authentication.JwtBearer` `10.0.5`
- `Microsoft.EntityFrameworkCore.Sqlite` `10.0.5`
- `Microsoft.EntityFrameworkCore.SqlServer` `10.0.5`
- `Swashbuckle.AspNetCore` `7.2.0`
- `UglyToad.PdfPig` `1.7.0-custom-5`

주요 파일:
- `AiDeskApi/AiDeskApi.csproj`
- `AiDeskApi/Program.cs`

## 4. 임베딩/RAG/검색 스택

- Embedding Model: `text-embedding-3-small`
- Chat Model: `gpt-4o-mini`
- Chat Endpoint: `https://api.openai.com/v1/chat/completions`
- Vector Search: Qdrant cosine distance
- Retrieval 방식: Semantic 검색 + Keyword 후보 확장 + 조건부 GPT re-rank
- Threshold 정책: 기본 `0.5` (ChatbotPromptTemplateService에서 관리)
- Raw hit 수: 40 → document 15 + expected 20 분리 → KB 단위 병합 10 → 최종 5
- Rerank 스킵: 1위 score ≥ 0.82 또는 1~2위 gap ≥ 0.15 시 LLM 호출 없이 정렬만

주요 파일:
- `AiDeskApi/Services/OpenAiEmbeddingService.cs`
- `AiDeskApi/Services/OpenAiRagService.cs`
- `AiDeskApi/Services/QdrantVectorSearchService.cs`
- `AiDeskApi/Services/ChatbotPromptTemplateService.cs`
- `AiDeskApi/appsettings.Development.json`

## 5. 데이터 저장소

- 기본 DB: SQLite (`aidesk.db`)
- 전환 가능 DB: MSSQL (`Database:Provider = mssql`)
- 벡터 인덱스: Qdrant `aidesk_kb` 컬렉션

주요 파일:
- `AiDeskApi/Program.cs`
- `AiDeskApi/appsettings.Development.json`

## 6. 실행/운영 스크립트

- 전체 실행: `scripts/run-all.sh`
- 백엔드 실행: `scripts/run-backend.sh`
- 프론트 실행: `scripts/run-frontend.sh`
- 전체 중지: `scripts/stop-all.sh`

## 7. 참고 문서

- 전체 개요: `README.md`
- RAG 상세: `docs/rag-kb-process.md`
- API 명세: `docs/api-spec.md`
- 환경 설정: `docs/setup.md`
