using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using CrmApi.Data;
using CrmApi.Models;
using CrmApi.Services;

var builder = WebApplication.CreateBuilder(args);

// API 문서/컨트롤러 직렬화 설정
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

// SQLite CRM DB 컨텍스트 등록
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "crm.db");
builder.Services.AddDbContext<CrmContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// 외부 AI API 호출 서비스 등록
builder.Services.AddHttpClient<IGeminiService, GeminiService>();
builder.Services.AddHttpClient<IGptService, GptService>();
builder.Services.AddHttpClient<IEmbeddingService, OpenAiEmbeddingService>();
builder.Services.AddHttpClient<IKnowledgeExtractorService, KnowledgeExtractorService>();
builder.Services.AddHttpClient<IRagService, OpenAiRagService>();
builder.Services.AddSingleton<ISummaryPromptTemplateService, SummaryPromptTemplateService>();

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
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.MapControllers();

// 앱 시작 시 CRM 핵심 테이블/인덱스 보장
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CrmContext>();
    db.Database.EnsureCreated();

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

    try { db.Database.ExecuteSqlRaw("ALTER TABLE Interactions ADD COLUMN IsExternalProvided INTEGER NOT NULL DEFAULT 0;"); } catch { }

    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS IX_Interactions_CustomerId
        ON Interactions (CustomerId);
    ");

    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS KnowledgeBases (
            Id INTEGER NOT NULL CONSTRAINT PK_KnowledgeBases PRIMARY KEY AUTOINCREMENT,
            SourceInteractionId INTEGER NULL,
            Problem TEXT NOT NULL,
            Solution TEXT NOT NULL,
            ProblemEmbedding TEXT NULL,
            CreatedAt TEXT NOT NULL,
            ViewCount INTEGER NOT NULL DEFAULT 0,
            Visibility TEXT NOT NULL DEFAULT 'internal',
            SourceType TEXT NOT NULL DEFAULT 'case',
            IsApproved INTEGER NOT NULL DEFAULT 0,
            Tags TEXT NULL,
            ApprovedBy TEXT NULL,
            ApprovedAt TEXT NULL
        );
    ");

    // 기존 DB에 새 컬럼이 없으면 추가 (idempotent)
    try { db.Database.ExecuteSqlRaw("ALTER TABLE KnowledgeBases ADD COLUMN Visibility TEXT NOT NULL DEFAULT 'internal';"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE KnowledgeBases ADD COLUMN SourceType TEXT NOT NULL DEFAULT 'case';"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE KnowledgeBases ADD COLUMN IsApproved INTEGER NOT NULL DEFAULT 0;"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE KnowledgeBases ADD COLUMN Tags TEXT NULL;"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE KnowledgeBases ADD COLUMN ApprovedBy TEXT NULL;"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE KnowledgeBases ADD COLUMN ApprovedAt TEXT NULL;"); } catch { }

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
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL,
            MessageCount INTEGER NOT NULL DEFAULT 0
        );
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
            CONSTRAINT FK_ChatMessages_ChatSessions_SessionId
                FOREIGN KEY (SessionId) REFERENCES ChatSessions (Id) ON DELETE CASCADE
        );
    ");

    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS IX_ChatMessages_SessionId
        ON ChatMessages (SessionId);
    ");
}

app.Run();
