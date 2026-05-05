using AiDeskApi.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace AiDeskApi.Data
{
    /// <summary>
    /// 데이터베이스 초기화 및 기본 데이터 생성을 담당하는 클래스
    /// </summary>
    public static class DatabaseInitializer
    {
        /// <summary>
        /// 데이터베이스 초기화 및 기본 관리자 사용자 생성
        /// </summary>
        public static void InitializeDatabase(AiDeskContext db)
        {
            // DB 생성 (테이블 구조가 없으면 생성)
            db.Database.EnsureCreated();

            if (db.Database.IsSqlite())
            {
                InitializeSqliteDatabase(db);
            }

            // SQLite/MSSQL 공통: 기본 관리자 계정 생성
            EnsureAdminUserExists(db);
        }

        /// <summary>
        /// SQLite 데이터베이스의 테이블 및 기본 데이터 초기화
        /// </summary>
        private static void InitializeSqliteDatabase(AiDeskContext db)
        {
            // 1. Users 테이블 스키마 정합성 보장 (loginId + username)
            EnsureUsersTableSchema(db);

            // 2. KnowledgeBases 테이블
            db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS KnowledgeBases (
                Id INTEGER NOT NULL CONSTRAINT PK_KnowledgeBases PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Content TEXT NOT NULL,
                Problem TEXT NOT NULL,
                Solution TEXT NOT NULL,
                ProblemEmbedding TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                CreatedBy TEXT NOT NULL DEFAULT '시스템',
                UpdatedBy TEXT NOT NULL DEFAULT '시스템',
                ViewCount INTEGER NOT NULL DEFAULT 0,
                Visibility TEXT NOT NULL DEFAULT 'admin',
                Platform TEXT NOT NULL DEFAULT '공통',
                Tags TEXT NULL
            );");

            EnsureColumnExists(db, "KnowledgeBases", "Title", "ALTER TABLE KnowledgeBases ADD COLUMN Title TEXT NULL;");
            EnsureColumnExists(db, "KnowledgeBases", "Content", "ALTER TABLE KnowledgeBases ADD COLUMN Content TEXT NULL;");
            EnsureColumnExists(db, "KnowledgeBases", "Visibility", "ALTER TABLE KnowledgeBases ADD COLUMN Visibility TEXT NOT NULL DEFAULT 'admin';");
            EnsureColumnExists(db, "KnowledgeBases", "Platform", "ALTER TABLE KnowledgeBases ADD COLUMN Platform TEXT NOT NULL DEFAULT '공통';");
            EnsureColumnExists(db, "KnowledgeBases", "Tags", "ALTER TABLE KnowledgeBases ADD COLUMN Tags TEXT NULL;");
            EnsureColumnExists(db, "KnowledgeBases", "UpdatedAt", "ALTER TABLE KnowledgeBases ADD COLUMN UpdatedAt TEXT NULL;");
            EnsureColumnExists(db, "KnowledgeBases", "CreatedBy", "ALTER TABLE KnowledgeBases ADD COLUMN CreatedBy TEXT NOT NULL DEFAULT '시스템';");
            EnsureColumnExists(db, "KnowledgeBases", "UpdatedBy", "ALTER TABLE KnowledgeBases ADD COLUMN UpdatedBy TEXT NOT NULL DEFAULT '시스템';");

            db.Database.ExecuteSqlRaw(@"
            UPDATE KnowledgeBases
            SET UpdatedAt = COALESCE(UpdatedAt, CreatedAt)
            WHERE UpdatedAt IS NULL;");

            db.Database.ExecuteSqlRaw(@"
            UPDATE KnowledgeBases
            SET Title = COALESCE(NULLIF(TRIM(Title), ''), NULLIF(TRIM(Problem), ''), '제목 없음')
            WHERE Title IS NULL OR TRIM(Title) = '';");

            db.Database.ExecuteSqlRaw(@"
            UPDATE KnowledgeBases
            SET Content = COALESCE(NULLIF(TRIM(Content), ''), NULLIF(TRIM(Solution), ''), '')
            WHERE Content IS NULL OR TRIM(Content) = '';");

            db.Database.ExecuteSqlRaw(@"
            UPDATE KnowledgeBases
            SET CreatedBy = COALESCE(NULLIF(TRIM(CreatedBy), ''), '시스템'),
                UpdatedBy = COALESCE(NULLIF(TRIM(UpdatedBy), ''), COALESCE(NULLIF(TRIM(CreatedBy), ''), '시스템'));");

                        // 기존 작성자/수정자가 loginId로 저장된 경우 사용자명(username)으로 보정
                        db.Database.ExecuteSqlRaw(@"
                        UPDATE KnowledgeBases
                        SET CreatedBy = (
                                SELECT u.Username
                                FROM Users u
                                WHERE u.LoginId = KnowledgeBases.CreatedBy
                                    AND u.Username IS NOT NULL
                                    AND TRIM(u.Username) <> ''
                                LIMIT 1
                        )
                        WHERE EXISTS (
                                SELECT 1
                                FROM Users u
                                WHERE u.LoginId = KnowledgeBases.CreatedBy
                                    AND u.Username IS NOT NULL
                                    AND TRIM(u.Username) <> ''
                        );

                        UPDATE KnowledgeBases
                        SET UpdatedBy = (
                                SELECT u.Username
                                FROM Users u
                                WHERE u.LoginId = KnowledgeBases.UpdatedBy
                                    AND u.Username IS NOT NULL
                                    AND TRIM(u.Username) <> ''
                                LIMIT 1
                        )
                        WHERE EXISTS (
                                SELECT 1
                                FROM Users u
                                WHERE u.LoginId = KnowledgeBases.UpdatedBy
                                    AND u.Username IS NOT NULL
                                    AND TRIM(u.Username) <> ''
                        );");

            // 레거시 가시성 값 정리
            db.Database.ExecuteSqlRaw(@"
            UPDATE KnowledgeBases
            SET Visibility = CASE
                WHEN Visibility IN ('common', 'user') THEN 'user'
                ELSE 'admin'
            END;");

            db.Database.ExecuteSqlRaw(@"
            UPDATE KnowledgeBases
            SET Platform = CASE
                WHEN Platform IS NULL OR TRIM(Platform) = '' THEN '공통'
                WHEN LOWER(TRIM(Platform)) = 'common' THEN '공통'
                ELSE TRIM(Platform)
            END;");

            // 6. KbPlatforms 테이블
            db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS KbPlatforms (
                Id INTEGER NOT NULL CONSTRAINT PK_KbPlatforms PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL
            );");

            db.Database.ExecuteSqlRaw(@"
            CREATE UNIQUE INDEX IF NOT EXISTS IX_KbPlatforms_Name
            ON KbPlatforms (Name);");

            db.Database.ExecuteSqlRaw(@"
            DELETE FROM KbPlatforms
            WHERE LOWER(TRIM(Name)) = 'common'
                AND EXISTS (SELECT 1 FROM KbPlatforms WHERE Name = '공통');");

            db.Database.ExecuteSqlRaw(@"
            UPDATE KbPlatforms
            SET Name = '공통'
            WHERE LOWER(TRIM(Name)) = 'common';");

            db.Database.ExecuteSqlRaw(@"
            INSERT INTO KbPlatforms (Name, IsActive, CreatedAt)
            SELECT '공통', 1, datetime('now')
            WHERE NOT EXISTS (SELECT 1 FROM KbPlatforms WHERE Name = '공통');");

            // 7. KnowledgeBaseSimilarQuestions 테이블
            db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS KnowledgeBaseSimilarQuestions (
                Id INTEGER NOT NULL CONSTRAINT PK_KnowledgeBaseSimilarQuestions PRIMARY KEY AUTOINCREMENT,
                KnowledgeBaseId INTEGER NOT NULL,
                Question TEXT NOT NULL,
                QuestionEmbedding TEXT NULL,
                CreatedAt TEXT NOT NULL,
                CONSTRAINT FK_KnowledgeBaseSimilarQuestions_KnowledgeBases_KnowledgeBaseId
                    FOREIGN KEY (KnowledgeBaseId) REFERENCES KnowledgeBases (Id) ON DELETE CASCADE
            );");

            db.Database.ExecuteSqlRaw(@"
            CREATE INDEX IF NOT EXISTS IX_KnowledgeBaseSimilarQuestions_KnowledgeBaseId
            ON KnowledgeBaseSimilarQuestions (KnowledgeBaseId);");

            // 8. LowSimilarityQuestions 테이블
            db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS LowSimilarityQuestions (
                Id INTEGER NOT NULL CONSTRAINT PK_LowSimilarityQuestions PRIMARY KEY AUTOINCREMENT,
                Question TEXT NOT NULL,
                Role TEXT NOT NULL,
                ActorName TEXT NOT NULL DEFAULT '알 수 없음',
                Platform TEXT NOT NULL DEFAULT 'web',
                TopSimilarity REAL NOT NULL,
                TopMatchedQuestion TEXT NULL,
                SessionId INTEGER NULL,
                CreatedAt TEXT NOT NULL,
                IsResolved INTEGER NOT NULL DEFAULT 0,
                ResolvedAt TEXT NULL
            );");

            EnsureColumnExists(db, "LowSimilarityQuestions", "Platform",
                "ALTER TABLE LowSimilarityQuestions ADD COLUMN Platform TEXT NOT NULL DEFAULT 'web';");

            EnsureColumnExists(db, "LowSimilarityQuestions", "ActorName",
                "ALTER TABLE LowSimilarityQuestions ADD COLUMN ActorName TEXT NOT NULL DEFAULT '알 수 없음';");

            EnsureColumnExists(db, "LowSimilarityQuestions", "TopMatchedKbTitle",
                "ALTER TABLE LowSimilarityQuestions ADD COLUMN TopMatchedKbTitle TEXT NULL;");

            EnsureColumnExists(db, "LowSimilarityQuestions", "TopMatchedKbContent",
                "ALTER TABLE LowSimilarityQuestions ADD COLUMN TopMatchedKbContent TEXT NULL;");

            EnsureColumnExists(db, "LowSimilarityQuestions", "SessionId",
                "ALTER TABLE LowSimilarityQuestions ADD COLUMN SessionId INTEGER NULL;");

            db.Database.ExecuteSqlRaw(@"
            CREATE INDEX IF NOT EXISTS IX_LowSimilarityQuestions_IsResolved_CreatedAt
            ON LowSimilarityQuestions (IsResolved, CreatedAt DESC);");

            // 9. KnowledgeBases 인덱스 추가
            db.Database.ExecuteSqlRaw(@"
            CREATE INDEX IF NOT EXISTS IX_KnowledgeBases_CreatedAt
            ON KnowledgeBases (CreatedAt DESC);");

            db.Database.ExecuteSqlRaw(@"
            CREATE INDEX IF NOT EXISTS IX_KnowledgeBases_ViewCount
            ON KnowledgeBases (ViewCount DESC);");

            db.Database.ExecuteSqlRaw(@"
            CREATE INDEX IF NOT EXISTS IX_KnowledgeBases_Visibility_Platform_UpdatedAt
            ON KnowledgeBases (Visibility, Platform, UpdatedAt DESC);");

            db.Database.ExecuteSqlRaw(@"
            CREATE INDEX IF NOT EXISTS IX_KnowledgeBases_UpdatedAt
            ON KnowledgeBases (UpdatedAt DESC);");

            // 10. KnowledgeBaseHistories 테이블
            db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS KnowledgeBaseHistories (
                Id INTEGER NOT NULL CONSTRAINT PK_KnowledgeBaseHistories PRIMARY KEY AUTOINCREMENT,
                KnowledgeBaseId INTEGER NOT NULL,
                Action TEXT NOT NULL,
                Actor TEXT NOT NULL,
                ChangedAt TEXT NOT NULL,
                BeforeTitle TEXT NULL,
                BeforeContent TEXT NULL,
                BeforeVisibility TEXT NULL,
                BeforePlatform TEXT NULL,
                BeforeKeywords TEXT NULL,
                AfterTitle TEXT NULL,
                AfterContent TEXT NULL,
                AfterVisibility TEXT NULL,
                AfterPlatform TEXT NULL,
                AfterKeywords TEXT NULL
            );");

            db.Database.ExecuteSqlRaw(@"
            CREATE INDEX IF NOT EXISTS IX_KnowledgeBaseHistories_KnowledgeBaseId_ChangedAt
            ON KnowledgeBaseHistories (KnowledgeBaseId, ChangedAt DESC);");

            // 11. KnowledgeBaseExpectedQuestionHistories 테이블
            db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS KnowledgeBaseExpectedQuestionHistories (
                Id INTEGER NOT NULL CONSTRAINT PK_KnowledgeBaseExpectedQuestionHistories PRIMARY KEY AUTOINCREMENT,
                KnowledgeBaseId INTEGER NOT NULL,
                Action TEXT NOT NULL,
                Actor TEXT NOT NULL,
                ChangedAt TEXT NOT NULL,
                BeforeQuestion TEXT NULL,
                AfterQuestion TEXT NULL
            );");

            db.Database.ExecuteSqlRaw(@"
            CREATE INDEX IF NOT EXISTS IX_KnowledgeBaseExpectedQuestionHistories_KnowledgeBaseId_ChangedAt
            ON KnowledgeBaseExpectedQuestionHistories (KnowledgeBaseId, ChangedAt DESC);");

            db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS KnowledgeBaseWriterPromptTemplates (
                Id INTEGER NOT NULL CONSTRAINT PK_KnowledgeBaseWriterPromptTemplates PRIMARY KEY AUTOINCREMENT,
                KeywordSystemPrompt TEXT NOT NULL,
                KeywordRulesPrompt TEXT NOT NULL,
                SimilarQuestionSystemPrompt TEXT NOT NULL,
                SimilarQuestionRulesPrompt TEXT NOT NULL,
                TopicKeywordSystemPrompt TEXT NOT NULL,
                TopicKeywordRulesPrompt TEXT NOT NULL,
                AnswerRefineSystemPrompt TEXT NOT NULL,
                AnswerRefineRulesPrompt TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );");

            // ChatSessions 테이블 (다양한 필드 포함)
            db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ChatSessions (
                Id INTEGER NOT NULL CONSTRAINT PK_ChatSessions PRIMARY KEY AUTOINCREMENT,
                Title TEXT NULL,
                UserRole TEXT NOT NULL DEFAULT 'user',
                ActorName TEXT NOT NULL DEFAULT '알 수 없음',
                Platform TEXT NOT NULL DEFAULT 'web',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                MessageCount INTEGER NOT NULL DEFAULT 0
            );");

            EnsureColumnExists(db, "ChatSessions", "Platform",
                "ALTER TABLE ChatSessions ADD COLUMN Platform TEXT NOT NULL DEFAULT 'web';");

            EnsureColumnExists(db, "ChatSessions", "ActorName",
                "ALTER TABLE ChatSessions ADD COLUMN ActorName TEXT NOT NULL DEFAULT '알 수 없음';");

            // 11. ChatMessages 테이블 (session-based, not user-based)
            db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ChatMessages (
                Id INTEGER NOT NULL CONSTRAINT PK_ChatMessages PRIMARY KEY AUTOINCREMENT,
                SessionId INTEGER NOT NULL,
                Role TEXT NOT NULL,
                Content TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                RelatedKbIds TEXT NULL,
                RelatedKbMeta TEXT NULL,
                RelatedDocumentMeta TEXT NULL,
                RetrievalDebugMeta TEXT NULL,
                TopSimilarity REAL NULL,
                IsLowSimilarity INTEGER NOT NULL DEFAULT 0,
                CONSTRAINT FK_ChatMessages_ChatSessions_SessionId
                    FOREIGN KEY (SessionId) REFERENCES ChatSessions (Id) ON DELETE CASCADE
            );");

            EnsureColumnExists(db, "ChatMessages", "RelatedKbMeta",
                "ALTER TABLE ChatMessages ADD COLUMN RelatedKbMeta TEXT NULL;");
            EnsureColumnExists(db, "ChatMessages", "RelatedDocumentMeta",
                "ALTER TABLE ChatMessages ADD COLUMN RelatedDocumentMeta TEXT NULL;");
            EnsureColumnExists(db, "ChatMessages", "RetrievalDebugMeta",
                "ALTER TABLE ChatMessages ADD COLUMN RetrievalDebugMeta TEXT NULL;");
            EnsureColumnExists(db, "ChatMessages", "TopSimilarity",
                "ALTER TABLE ChatMessages ADD COLUMN TopSimilarity REAL NULL;");
            EnsureColumnExists(db, "ChatMessages", "IsLowSimilarity",
                "ALTER TABLE ChatMessages ADD COLUMN IsLowSimilarity INTEGER NOT NULL DEFAULT 0;");

            db.Database.ExecuteSqlRaw(@"
            CREATE INDEX IF NOT EXISTS IX_ChatMessages_SessionId
            ON ChatMessages (SessionId);");

            // 플랫폼명 정규화
            db.Database.ExecuteSqlRaw(@"
            UPDATE LowSimilarityQuestions
            SET Platform = '공통'
            WHERE LOWER(TRIM(Platform)) = 'common';");

            db.Database.ExecuteSqlRaw(@"
            UPDATE ChatSessions
            SET Platform = '공통'
            WHERE LOWER(TRIM(Platform)) = 'common';");
        }

        /// <summary>
        /// 기본 관리자 사용자가 없으면 생성
        /// </summary>
        private static void EnsureAdminUserExists(AiDeskContext db)
        {
            const string adminLoginId = "admin";
            const string adminUsername = "관리자";
            const string adminPassword = "esgroup00";

            // 기존 admin 사용자 확인
            var adminUser = db.Users.FirstOrDefault(u => u.LoginId == adminLoginId);

            if (adminUser == null)
            {
                // admin 사용자 생성
                adminUser = new User
                {
                    LoginId = adminLoginId,
                    Username = adminUsername,
                    PasswordHash = HashPassword(adminPassword),
                    Role = "admin",
                    IsActive = true,
                    IsApproved = true,
                    CreatedAt = DateTime.UtcNow
                };

                db.Users.Add(adminUser);
                db.SaveChanges();
            }
            else
            {
                var changed = false;
                if (string.IsNullOrWhiteSpace(adminUser.Username))
                {
                    adminUser.Username = adminUsername;
                    changed = true;
                }

                if (!string.Equals(adminUser.Role, "admin", StringComparison.OrdinalIgnoreCase))
                {
                    adminUser.Role = "admin";
                    changed = true;
                }

                if (!string.Equals(adminUser.Status, "approved", StringComparison.OrdinalIgnoreCase))
                {
                    adminUser.Status = "approved";
                    adminUser.IsApproved = true;
                    adminUser.IsActive = true;
                    adminUser.ApprovedAt ??= DateTime.UtcNow;
                    changed = true;
                }

                if (changed)
                {
                    db.SaveChanges();
                }
            }
        }

        private static void EnsureUsersTableSchema(AiDeskContext db)
        {
            db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER NOT NULL CONSTRAINT PK_Users PRIMARY KEY AUTOINCREMENT,
                LoginId TEXT NOT NULL UNIQUE,
                Username TEXT NOT NULL,
                PasswordHash TEXT NOT NULL,
                Role TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'pending',
                IsActive INTEGER NOT NULL,
                IsApproved INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                ApprovedAt TEXT NULL,
                LastLoginAt TEXT NULL
            );");

            if (!HasColumn(db, "Users", "LoginId"))
            {
                db.Database.ExecuteSqlRaw("ALTER TABLE Users RENAME TO Users_Legacy;");

                db.Database.ExecuteSqlRaw(@"
                CREATE TABLE Users (
                    Id INTEGER NOT NULL CONSTRAINT PK_Users PRIMARY KEY AUTOINCREMENT,
                    LoginId TEXT NOT NULL UNIQUE,
                    Username TEXT NOT NULL,
                    PasswordHash TEXT NOT NULL,
                    Role TEXT NOT NULL,
                    Status TEXT NOT NULL DEFAULT 'pending',
                    IsActive INTEGER NOT NULL,
                    IsApproved INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    ApprovedAt TEXT NULL,
                    LastLoginAt TEXT NULL
                );");

                db.Database.ExecuteSqlRaw(@"
                INSERT INTO Users (Id, LoginId, Username, PasswordHash, Role, Status, IsActive, IsApproved, CreatedAt, ApprovedAt, LastLoginAt)
                SELECT
                    Id,
                    COALESCE(NULLIF(TRIM(Username), ''), 'user_' || Id),
                    COALESCE(NULLIF(TRIM(FullName), ''), NULLIF(TRIM(Username), ''), '사용자' || Id),
                    PasswordHash,
                    CASE WHEN LOWER(Role) = 'admin' THEN 'admin' ELSE 'user' END,
                    CASE
                        WHEN IsActive = 0 THEN 'rejected'
                        WHEN IsApproved = 1 THEN 'approved'
                        ELSE 'pending'
                    END,
                    IsActive,
                    IsApproved,
                    CreatedAt,
                    ApprovedAt,
                    LastLoginAt
                FROM Users_Legacy;");

                db.Database.ExecuteSqlRaw("DROP TABLE Users_Legacy;");
                db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_LoginId ON Users (LoginId);");
            }
            else
            {
                EnsureColumnExists(db, "Users", "Status", "ALTER TABLE Users ADD COLUMN Status TEXT NOT NULL DEFAULT 'pending';");

                db.Database.ExecuteSqlRaw(@"
                UPDATE Users
                SET Role = CASE WHEN LOWER(Role) = 'admin' THEN 'admin' ELSE 'user' END;

                UPDATE Users
                SET Username = COALESCE(NULLIF(TRIM(Username), ''), LoginId)
                WHERE Username IS NULL OR TRIM(Username) = '';

                UPDATE Users
                SET Status = CASE
                    WHEN Status IN ('approved', 'pending', 'rejected', 'deleted') THEN Status
                    WHEN IsActive = 0 THEN 'rejected'
                    WHEN IsApproved = 1 THEN 'approved'
                    ELSE 'pending'
                END;
                ");
            }
        }

        private static bool HasColumn(AiDeskContext db, string tableName, string columnName)
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }

            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({tableName});";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 테이블에 컬럼이 존재하는지 확인하고 없으면 추가
        /// </summary>
        private static void EnsureColumnExists(AiDeskContext db, string tableName, string columnName, string addColumnSql)
        {
            try
            {
                var connection = db.Database.GetDbConnection();
                if (connection.State != ConnectionState.Open)
                {
                    connection.Open();
                }

                using var command = connection.CreateCommand();
                command.CommandText = $"PRAGMA table_info({tableName});";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }

                // 컬럼이 없으면 추가
                db.Database.ExecuteSqlRaw(addColumnSql);
            }
            catch
            {
                // 에러 무시 (컬럼이 이미 존재할 수 있음)
            }
        }

        /// <summary>
        /// 비밀번호를 SHA256으로 해시
        /// </summary>
        private static string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }
    }
}
