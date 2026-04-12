using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json.Serialization;
using AiDeskApi.Data;
using AiDeskApi.Models;
using AiDeskApi.Services;

var builder = WebApplication.CreateBuilder(args);

// API 문서/컨트롤러 직렬화 설정
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

// DB 컨텍스트 등록 (기본: SQLite, 설정값으로 MSSQL 전환 가능)
var dbProvider = builder.Configuration["Database:Provider"]?.Trim().ToLowerInvariant();
var crmConnectionString = builder.Configuration.GetConnectionString("AiDeskDb");

builder.Services.AddDbContext<AiDeskContext>(options =>
{
    if (dbProvider == "mssql" && !string.IsNullOrWhiteSpace(crmConnectionString))
    {
        options.UseSqlServer(crmConnectionString);
        return;
    }

    var dbPath = Path.Combine(builder.Environment.ContentRootPath, "aidesk.db");
    options.UseSqlite($"Data Source={dbPath}");
});

// 외부 AI API 호출 서비스 등록
builder.Services.AddHttpClient<IGeminiService, GeminiService>();
builder.Services.AddHttpClient<IGptService, GptService>();
builder.Services.AddHttpClient<IEmbeddingService, OpenAiEmbeddingService>();
builder.Services.AddHttpClient<IKnowledgeExtractorService, KnowledgeExtractorService>();
builder.Services.AddHttpClient<IRagService, OpenAiRagService>();
builder.Services.AddSingleton<ISummaryPromptTemplateService, SummaryPromptTemplateService>();
builder.Services.AddSingleton<IChatbotPromptTemplateService, ChatbotPromptTemplateService>();

// 로컬 프론트 연동을 위한 CORS 허용
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.MapControllers();

// 앱 시작 시 CRM 핵심 테이블/인덱스 보장
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AiDeskContext>();
    db.Database.EnsureCreated();

    if (db.Database.IsSqlite())
    {
        // Ensure CRM tables exist for databases created before CRM was added.
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
        );
    ");

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
        );
    ");

    EnsureColumnExists(db, "Interactions", "IsExternalProvided", "ALTER TABLE Interactions ADD COLUMN IsExternalProvided INTEGER NOT NULL DEFAULT 0;");

    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS IX_Interactions_CustomerId
        ON Interactions (CustomerId);
    ");

    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS KnowledgeBases (
            Id INTEGER NOT NULL CONSTRAINT PK_KnowledgeBases PRIMARY KEY AUTOINCREMENT,
            Problem TEXT NOT NULL,
            Solution TEXT NOT NULL,
            ProblemEmbedding TEXT NULL,
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL,
            ViewCount INTEGER NOT NULL DEFAULT 0,
            Visibility TEXT NOT NULL DEFAULT 'admin',
            Platform TEXT NOT NULL DEFAULT '공통',
            Tags TEXT NULL
        );
    ");

    // 기존 DB에 새 컬럼이 없으면 추가 (idempotent)
    EnsureColumnExists(db, "KnowledgeBases", "Visibility", "ALTER TABLE KnowledgeBases ADD COLUMN Visibility TEXT NOT NULL DEFAULT 'admin';");
    EnsureColumnExists(db, "KnowledgeBases", "Platform", "ALTER TABLE KnowledgeBases ADD COLUMN Platform TEXT NOT NULL DEFAULT '공통';");
    EnsureColumnExists(db, "KnowledgeBases", "Tags", "ALTER TABLE KnowledgeBases ADD COLUMN Tags TEXT NULL;");
    EnsureColumnExists(db, "KnowledgeBases", "UpdatedAt", "ALTER TABLE KnowledgeBases ADD COLUMN UpdatedAt TEXT NULL;");

    db.Database.ExecuteSqlRaw(@"
        UPDATE KnowledgeBases
        SET UpdatedAt = COALESCE(UpdatedAt, CreatedAt)
        WHERE UpdatedAt IS NULL;
    ");

    // 레거시 가시성 값 정리
    db.Database.ExecuteSqlRaw(@"
        UPDATE KnowledgeBases
        SET Visibility = CASE
            WHEN Visibility IN ('common', 'user') THEN 'user'
            ELSE 'admin'
        END;
    ");

    db.Database.ExecuteSqlRaw(@"
        UPDATE KnowledgeBases
        SET Platform = CASE
            WHEN Platform IS NULL OR TRIM(Platform) = '' THEN '공통'
            WHEN LOWER(TRIM(Platform)) = 'common' THEN '공통'
            ELSE TRIM(Platform)
        END;
    ");

    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS KbPlatforms (
            Id INTEGER NOT NULL CONSTRAINT PK_KbPlatforms PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            IsActive INTEGER NOT NULL DEFAULT 1,
            CreatedAt TEXT NOT NULL
        );
    ");

    db.Database.ExecuteSqlRaw(@"
        CREATE UNIQUE INDEX IF NOT EXISTS IX_KbPlatforms_Name
        ON KbPlatforms (Name);
    ");

    db.Database.ExecuteSqlRaw(@"
                DELETE FROM KbPlatforms
                WHERE LOWER(TRIM(Name)) = 'common'
                    AND EXISTS (SELECT 1 FROM KbPlatforms WHERE Name = '공통');
        ");

        db.Database.ExecuteSqlRaw(@"
        UPDATE KbPlatforms
        SET Name = '공통'
        WHERE LOWER(TRIM(Name)) = 'common';
    ");

    db.Database.ExecuteSqlRaw(@"
        INSERT INTO KbPlatforms (Name, IsActive, CreatedAt)
        SELECT '공통', 1, datetime('now')
        WHERE NOT EXISTS (SELECT 1 FROM KbPlatforms WHERE Name = '공통');
    ");

    // 대표질문에 연결되는 유사질문 테이블
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS KnowledgeBaseSimilarQuestions (
            Id INTEGER NOT NULL CONSTRAINT PK_KnowledgeBaseSimilarQuestions PRIMARY KEY AUTOINCREMENT,
            KnowledgeBaseId INTEGER NOT NULL,
            Question TEXT NOT NULL,
            QuestionEmbedding TEXT NULL,
            CreatedAt TEXT NOT NULL,
            CONSTRAINT FK_KnowledgeBaseSimilarQuestions_KnowledgeBases_KnowledgeBaseId
                FOREIGN KEY (KnowledgeBaseId) REFERENCES KnowledgeBases (Id) ON DELETE CASCADE
        );
    ");

    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS IX_KnowledgeBaseSimilarQuestions_KnowledgeBaseId
        ON KnowledgeBaseSimilarQuestions (KnowledgeBaseId);
    ");

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
        );
    ");

    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS IX_LowSimilarityQuestions_IsResolved_CreatedAt
        ON LowSimilarityQuestions (IsResolved, CreatedAt DESC);
    ");

    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS IX_KnowledgeBases_CreatedAt
        ON KnowledgeBases (CreatedAt DESC);
    ");

    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS IX_KnowledgeBases_ViewCount
        ON KnowledgeBases (ViewCount DESC);
    ");

    // 챗봇 세션 테이블
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS ChatSessions (
            Id INTEGER NOT NULL CONSTRAINT PK_ChatSessions PRIMARY KEY AUTOINCREMENT,
            Title TEXT NULL,
            UserRole TEXT NOT NULL DEFAULT 'user',
            Platform TEXT NOT NULL DEFAULT 'web',
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL,
            MessageCount INTEGER NOT NULL DEFAULT 0
        );
    ");

    EnsureColumnExists(db, "LowSimilarityQuestions", "Platform", "ALTER TABLE LowSimilarityQuestions ADD COLUMN Platform TEXT NOT NULL DEFAULT 'web';");
    EnsureColumnExists(db, "ChatSessions", "Platform", "ALTER TABLE ChatSessions ADD COLUMN Platform TEXT NOT NULL DEFAULT 'web';");

    db.Database.ExecuteSqlRaw(@"
        UPDATE LowSimilarityQuestions
        SET Platform = '공통'
        WHERE LOWER(TRIM(Platform)) = 'common';
    ");

    db.Database.ExecuteSqlRaw(@"
        UPDATE ChatSessions
        SET Platform = '공통'
        WHERE LOWER(TRIM(Platform)) = 'common';
    ");

    // 챗봇 메시지 테이블
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS ChatMessages (
            Id INTEGER NOT NULL CONSTRAINT PK_ChatMessages PRIMARY KEY AUTOINCREMENT,
            SessionId INTEGER NOT NULL,
            Role TEXT NOT NULL,
            Content TEXT NOT NULL,
            CreatedAt TEXT NOT NULL,
            RelatedKbIds TEXT NULL,
            TopSimilarity REAL NULL,
            IsLowSimilarity INTEGER NOT NULL DEFAULT 0,
            CONSTRAINT FK_ChatMessages_ChatSessions_SessionId
                FOREIGN KEY (SessionId) REFERENCES ChatSessions (Id) ON DELETE CASCADE
        );
    ");

    EnsureColumnExists(db, "ChatMessages", "TopSimilarity", "ALTER TABLE ChatMessages ADD COLUMN TopSimilarity REAL NULL;");
    EnsureColumnExists(db, "ChatMessages", "IsLowSimilarity", "ALTER TABLE ChatMessages ADD COLUMN IsLowSimilarity INTEGER NOT NULL DEFAULT 0;");

    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS IX_ChatMessages_SessionId
        ON ChatMessages (SessionId);
    ");
    }
}

app.Run();

static void EnsureColumnExists(AiDeskContext db, string tableName, string columnName, string alterSql)
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

    db.Database.ExecuteSqlRaw(alterSql);
}
