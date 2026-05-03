# AiDesk ERD (Entity Relationship Diagram)

> Mermaid ERD 기준. SQLite(개발) / MSSQL(운영) 공통 스키마.

```mermaid
erDiagram

    %% ─────────────────────────────────────────
    %% 인증
    %% ─────────────────────────────────────────
    Users {
        int     Id          PK
        string  LoginId     "UNIQUE, max 50"
        string  Username    "max 50"
        string  PasswordHash
        string  Role        "admin | user"
        bool    IsActive
        bool    IsApproved
        datetime CreatedAt
        datetime ApprovedAt  "nullable"
        datetime LastLoginAt "nullable"
    }

    %% ─────────────────────────────────────────
    %% 지식베이스 (KB)
    %% ─────────────────────────────────────────
    KnowledgeBases {
        int     Id               PK
        string  Title            "max 200, nullable"
        string  Problem          "max 500"
        string  Solution
        string  ProblemEmbedding "벡터 JSON, nullable"
        datetime CreatedAt
        datetime UpdatedAt
        string  CreatedBy        "max 100"
        string  UpdatedBy        "max 100"
        int     ViewCount
        string  Visibility       "admin | user"
        string  Platform         "공통 | windows | ..."
        string  Keywords         "Tags 컬럼명, nullable"
    }

    KnowledgeBaseSimilarQuestions {
        int     Id               PK
        int     KnowledgeBaseId  FK
        string  Question         "max 500"
        string  QuestionEmbedding "벡터 JSON, nullable"
        datetime CreatedAt
    }

    KnowledgeBases ||--o{ KnowledgeBaseSimilarQuestions : "has"

    KbPlatforms {
        int     Id        PK
        string  Name      "UNIQUE, max 50"
        bool    IsActive
        datetime CreatedAt
    }


    LowSimilarityQuestions {
        int     Id                PK
        string  Question          "max 1000"
        string  Role              "max 20"
        string  ActorName         "max 100"
        string  Platform          "max 50"
        float   TopSimilarity
        string  TopMatchedQuestion  "max 500, nullable"
        string  TopMatchedKbTitle   "max 200, nullable"
        string  TopMatchedKbContent "nullable"
        int     SessionId           "FK nullable"
        datetime CreatedAt
        bool    IsResolved
        datetime ResolvedAt       "nullable"
    }

    %% ─────────────────────────────────────────
    %% 챗봇 세션 / 메시지
    %% ─────────────────────────────────────────
    ChatSessions {
        int     Id           PK
        string  Title        "nullable"
        string  UserRole     "admin | user"
        string  ActorName    "max 100"
        string  Platform     "max 50"
        datetime CreatedAt
        datetime UpdatedAt
        int     MessageCount
    }

    ChatMessages {
        int     Id                   PK
        int     SessionId            FK
        string  Role                 "user | bot"
        string  Content
        datetime CreatedAt
        string  RelatedKbIds         "JSON 배열, nullable"
        string  RelatedKbMeta        "JSON 배열, nullable"
        string  RelatedDocumentMeta  "JSON 배열, nullable"
        string  RetrievalDebugMeta   "JSON, nullable"
        float   TopSimilarity        "nullable"
        bool    IsLowSimilarity
    }

    ChatSessions ||--o{ ChatMessages : "has"
```

## 테이블 요약

| 그룹 | 테이블 | 설명 |
|------|--------|------|
| 인증 | `Users` | 사용자 계정 (LoginId + Username, 관리자 승인 기반) |
| 지식베이스 | `KnowledgeBases` | 문제(Problem)+해결(Solution) 형식 KB 항목 |
| 지식베이스 | `KnowledgeBaseSimilarQuestions` | KB별 유사 질문 (벡터 검색용) |
| 지식베이스 | `KbPlatforms` | 플랫폼 목록 (공통, windows, ...) |

| 지식베이스 | `LowSimilarityQuestions` | 유사도 미달 질문 로그 (ActorName, SessionId 포함) |
| 챗봇 | `ChatSessions` | 채팅 세션 (ActorName 포함) |
| 챗봇 | `ChatMessages` | 세션별 메시지 |

## 벡터 저장소 (Qdrant)

SQLite/MSSQL 외에 Qdrant 컬렉션 `aidesk_kb` 에 벡터를 별도 저장합니다.

| 포인트 타입 | 연결 엔티티 | payload 필드 |
|-------------|-------------|-------------|
| KB 대표 질문 | `KnowledgeBases.Id` | `kb_id`, `type="representative"` |
| KB 유사 질문 | `KnowledgeBaseSimilarQuestions.Id` | `kb_id`, `similar_question_id`, `type="similar"` |
