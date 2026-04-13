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
        }

        /// <summary>
        /// SQLite 데이터베이스의 테이블 및 기본 데이터 초기화
        /// </summary>
        private static void InitializeSqliteDatabase(AiDeskContext db)
        {
            // 1. Users 테이블 명시적 생성
            db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER NOT NULL CONSTRAINT PK_Users PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL UNIQUE,
                Email TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL,
                Role TEXT NOT NULL,
                IsActive INTEGER NOT NULL,
                IsApproved INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                ApprovedAt TEXT NULL,
                LastLoginAt TEXT NULL
            );");

            // 2. 기본 管理者 사용자가 없으면 생성
            EnsureAdminUserExists(db);

            // 3. Customers 테이블
            db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS Customers (
                Id INTEGER NOT NULL CONSTRAINT PK_Customers PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                PhoneNumber TEXT NULL,
                Email TEXT NULL,
                Company TEXT NULL,
                Position TEXT NULL,
                Notes TEXT NULL,
                CreatedAt TEXT NOT NULL,
                LastContactDate TEXT NULL,
                Status TEXT NULL
            );");

            // 4. Interactions 테이블
            db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS Interactions (
                Id INTEGER NOT NULL CONSTRAINT PK_Interactions PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                Type TEXT NOT NULL,
                Content TEXT NOT NULL,
                Outcome TEXT NULL,
                CreatedAt TEXT NOT NULL,
                ScheduledDate TEXT NULL,
                IsCompleted INTEGER NOT NULL,
                IsExternalProvided INTEGER NOT NULL DEFAULT 0,
                CONSTRAINT FK_Interactions_Customers_CustomerId
                    FOREIGN KEY (CustomerId) REFERENCES Customers (Id) ON DELETE CASCADE
            );");

            EnsureColumnExists(db, "Interactions", "IsExternalProvided",
                "ALTER TABLE Interactions ADD COLUMN IsExternalProvided INTEGER NOT NULL DEFAULT 0;");

            db.Database.ExecuteSqlRaw(@"
            CREATE INDEX IF NOT EXISTS IX_Interactions_CustomerId
            ON Interactions (CustomerId);");

            // 5. KnowledgeBases 테이블
            db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS KnowledgeBases (
                Id INTEGER NOT NULL CONSTRAINT PK_KnowledgeBases PRIMARY KEY AUTOINCREMENT,
                Title TEXT NULL,
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
            SET CreatedBy = COALESCE(NULLIF(TRIM(CreatedBy), ''), '시스템'),
                UpdatedBy = COALESCE(NULLIF(TRIM(UpdatedBy), ''), COALESCE(NULLIF(TRIM(CreatedBy), ''), '시스템'));");

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
                Platform TEXT NOT NULL DEFAULT 'web',
                TopSimilarity REAL NOT NULL,
                TopMatchedQuestion TEXT NULL,
                CreatedAt TEXT NOT NULL,
                IsResolved INTEGER NOT NULL DEFAULT 0,
                ResolvedAt TEXT NULL
            );");

            EnsureColumnExists(db, "LowSimilarityQuestions", "Platform",
                "ALTER TABLE LowSimilarityQuestions ADD COLUMN Platform TEXT NOT NULL DEFAULT 'web';");

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

            // 10. ChatSessions 테이블 (다양한 필드 포함)
            db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ChatSessions (
                Id INTEGER NOT NULL CONSTRAINT PK_ChatSessions PRIMARY KEY AUTOINCREMENT,
                Title TEXT NULL,
                UserRole TEXT NOT NULL DEFAULT 'user',
                Platform TEXT NOT NULL DEFAULT 'web',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                MessageCount INTEGER NOT NULL DEFAULT 0
            );");

            EnsureColumnExists(db, "ChatSessions", "Platform",
                "ALTER TABLE ChatSessions ADD COLUMN Platform TEXT NOT NULL DEFAULT 'web';");

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
                RetrievalDebugMeta TEXT NULL,
                TopSimilarity REAL NULL,
                IsLowSimilarity INTEGER NOT NULL DEFAULT 0,
                CONSTRAINT FK_ChatMessages_ChatSessions_SessionId
                    FOREIGN KEY (SessionId) REFERENCES ChatSessions (Id) ON DELETE CASCADE
            );");

            EnsureColumnExists(db, "ChatMessages", "RelatedKbMeta",
                "ALTER TABLE ChatMessages ADD COLUMN RelatedKbMeta TEXT NULL;");
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
            const string adminUsername = "admin";
            const string adminEmail = "admin@aidesk.com";
            const string adminPassword = "esgroup00";

            // 기존 admin 사용자 확인
            var adminUser = db.Users.FirstOrDefault(u => u.Username == adminUsername);

            if (adminUser == null)
            {
                // admin 사용자 생성
                adminUser = new User
                {
                    Username = adminUsername,
                    Email = adminEmail,
                    PasswordHash = HashPassword(adminPassword),
                    Role = "admin",
                    IsActive = true,
                    IsApproved = true,
                    CreatedAt = DateTime.UtcNow
                };

                db.Users.Add(adminUser);
                db.SaveChanges();
            }
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
