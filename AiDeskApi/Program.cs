using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json.Serialization;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using AiDeskApi.Data;
using AiDeskApi.Models;
using AiDeskApi.Services;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);

// API 문서/컨트롤러 직렬화 설정
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true; // camelCase/PascalCase 자동 매칭
    options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
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
builder.Services.AddHttpClient<IVectorSearchService, QdrantVectorSearchService>();
builder.Services.AddScoped<IDocumentKnowledgeService, DocumentKnowledgeService>();
builder.Services.AddSingleton<ISummaryPromptTemplateService, SummaryPromptTemplateService>();
builder.Services.AddSingleton<IChatbotPromptTemplateService, ChatbotPromptTemplateService>();
builder.Services.AddScoped<IKnowledgeBaseWriterPromptTemplateService, KnowledgeBaseWriterPromptTemplateService>();

// JWT 인증 설정
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = Encoding.UTF8.GetBytes(jwtSettings["SecretKey"] ?? "your-secret-key-that-is-at-least-32-characters-long");

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(secretKey),
            ValidateIssuer = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSettings["Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

var corsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()?
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .Select(x => x.Trim())
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray() ?? Array.Empty<string>();

if (corsOrigins.Length == 0)
{
    if (builder.Environment.IsDevelopment())
    {
        corsOrigins = new[]
        {
            "http://localhost:5173",
            "http://127.0.0.1:5173"
        };
    }
    else
    {
        throw new InvalidOperationException("Cors:AllowedOrigins 설정이 필요합니다. 운영 환경에서는 허용 Origin을 명시해야 합니다.");
    }
}

// CORS 설정 (운영: 명시된 Origin만 허용)
builder.Services.AddCors(options =>
{
    options.AddPolicy("ConfiguredCors", policy =>
    {
        policy.WithOrigins(corsOrigins)
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

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors("ConfiguredCors");

// JWT 인증/권한 미들웨어 추가
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// 앱 시작 시 데이터베이스 및 기본 데이터 초기화
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AiDeskContext>();
    DatabaseInitializer.InitializeDatabase(db);

    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
    var startupLogger = loggerFactory.CreateLogger("Startup");

    _ = Task.Run(async () =>
    {
        try
        {
            using var syncScope = app.Services.CreateScope();
            var vector = syncScope.ServiceProvider.GetRequiredService<IVectorSearchService>();
            await vector.SyncAllKnowledgeBasesAsync();
            startupLogger.LogInformation("벡터 인덱스 초기 동기화 완료");
        }
        catch (Exception ex)
        {
            startupLogger.LogWarning(ex, "⚠️ 벡터 인덱스 초기 동기화 실패(비치명)");
        }
    });
}

app.Run();
