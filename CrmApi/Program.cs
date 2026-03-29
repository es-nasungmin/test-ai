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
            CONSTRAINT FK_Interactions_Customers_CustomerId
                FOREIGN KEY (CustomerId) REFERENCES Customers (Id) ON DELETE CASCADE
        );
    ");

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
            ViewCount INTEGER NOT NULL DEFAULT 0
        );
    ");

    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS IX_KnowledgeBases_CreatedAt
        ON KnowledgeBases (CreatedAt DESC);
    ");

    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS IX_KnowledgeBases_ViewCount
        ON KnowledgeBases (ViewCount DESC);
    ");
}

app.Run();
