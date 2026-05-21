using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Text.Json.Serialization;
using System.Text;
using System.Diagnostics;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using AiDeskApi.Data;
using AiDeskApi.Models;
using AiDeskApi.Services;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);

// API 문서/컨트롤러 직렬화 설정
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
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
    options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));

    if (dbProvider == "mssql" && !string.IsNullOrWhiteSpace(crmConnectionString))
    {
        options.UseSqlServer(crmConnectionString);
        return;
    }

    var dbPath = Path.Combine(builder.Environment.ContentRootPath, "aidesk.db");
    options.UseSqlite($"Data Source={dbPath}");
});

// 외부 AI API 호출 서비스 등록
builder.Services.AddHttpClient<IEmbeddingService, OpenAiEmbeddingService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient<IKnowledgeExtractorService, KnowledgeExtractorService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient<IRagService, OpenAiRagService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(40);
});
builder.Services.AddHttpClient<IVectorSearchService, QdrantVectorSearchService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<KnowledgeBaseVectorSyncQueue>();
builder.Services.AddSingleton<IKnowledgeBaseVectorSyncQueue>(sp => sp.GetRequiredService<KnowledgeBaseVectorSyncQueue>());
builder.Services.AddHostedService<KnowledgeBaseVectorSyncWorker>();
builder.Services.AddScoped<IChatbotPromptTemplateService, ChatbotPromptTemplateService>();
builder.Services.AddScoped<IKnowledgeBaseWriterPromptTemplateService, KnowledgeBaseWriterPromptTemplateService>();

// JWT 인증 설정
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = Encoding.UTF8.GetBytes(jwtSettings["SecretKey"] ?? "your-secret-key-that-is-at-least-32-characters-long");

if (builder.Environment.IsProduction())
{
    ValidateRequiredProductionSettings(builder.Configuration, jwtSettings);
}

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

// 전역 예외를 일관된 JSON으로 반환
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GlobalExceptionHandler");

        if (feature?.Error != null)
        {
            logger.LogError(feature.Error, "처리되지 않은 예외 발생. Path={Path}", context.Request.Path);
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Internal Server Error",
            Detail = app.Environment.IsDevelopment() ? feature?.Error?.Message : "요청 처리 중 오류가 발생했습니다.",
            Instance = context.Request.Path
        };

        await context.Response.WriteAsJsonAsync(problem);
    });
});

// 요청 단위 로깅(요청 경로/상태코드/지연시간)
// 별도의 extension 로 변경 ****
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RequestLog");
    var sw = Stopwatch.StartNew();
    await next();
    sw.Stop();

    logger.LogInformation(
        "HTTP {Method} {Path} => {StatusCode} ({ElapsedMs}ms) TraceId={TraceId}",
        context.Request.Method,
        context.Request.Path,
        context.Response.StatusCode,
        sw.ElapsedMilliseconds,
        context.TraceIdentifier);
});

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
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestampUtc = DateTime.UtcNow
}));

// 컨트롤러 쪽에서 생성 , 약식 표현쓰는 이유...
app.MapGet("/ready", async (AiDeskContext db, IConfiguration config) =>
{
    var checks = new Dictionary<string, string>();

    var dbReady = await db.Database.CanConnectAsync();
    checks["database"] = dbReady ? "ok" : "failed";

    var qdrantEnabled = config.GetValue<bool?>("Qdrant:Enabled") ?? true;
    if (!qdrantEnabled)
    {
        checks["qdrant"] = "disabled";
    }
    else
    {
        checks["qdrant"] = await CheckQdrantAsync(config) ? "ok" : "failed";
    }

    var isReady = checks.Values.All(v => v == "ok" || v == "disabled");
    return isReady
        ? Results.Ok(new { status = "ready", checks, timestampUtc = DateTime.UtcNow })
        : Results.Json(new { status = "not_ready", checks, timestampUtc = DateTime.UtcNow }, statusCode: StatusCodes.Status503ServiceUnavailable);
});

// 앱 시작 시 데이터베이스 및 기본 데이터 초기화
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AiDeskContext>();
    var startupLogger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("StartupMigration");
    // SQLite는 마이그레이션 자동 적용, SQL Server는 모델 기준 자동 생성으로 부트스트랩
    // (현재 마이그레이션이 SQLite 타입으로 생성되어 SQL Server 초기 생성 시 실패할 수 있음)
    if (db.Database.IsSqlite())
    {
        ReconcileCategoryMigrationHistoryForSqlite(db, startupLogger);
        db.Database.Migrate();
    }
    else if (db.Database.IsSqlServer())
    {
        db.Database.EnsureCreated();
    }
    else
    {
        db.Database.Migrate();
    }

    EnsureChatbotCategoryTable(db, startupLogger);

    DatabaseInitializer.InitializeDatabase(db);
}

app.Run();

static void ValidateRequiredProductionSettings(IConfiguration configuration, IConfigurationSection jwtSettings)
{
    var jwtSecret = jwtSettings["SecretKey"];
    if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Contains("your-secret-key", StringComparison.OrdinalIgnoreCase) || jwtSecret.Length < 32)
    {
        throw new InvalidOperationException("운영 환경에서는 JwtSettings:SecretKey를 32자 이상 안전한 값으로 설정해야 합니다.");
    }

    var openAiApiKey = configuration["OpenAI:ApiKey"];
    if (string.IsNullOrWhiteSpace(openAiApiKey) || openAiApiKey.Equals("YOUR_OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("운영 환경에서는 OpenAI:ApiKey를 환경변수 또는 Secret Manager로 설정해야 합니다.");
    }
}

static async Task<bool> CheckQdrantAsync(IConfiguration configuration)
{
    var qdrantUrl = configuration["Qdrant:Url"];
    if (string.IsNullOrWhiteSpace(qdrantUrl))
    {
        return false;
    }

    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        using var response = await http.GetAsync($"{qdrantUrl.TrimEnd('/')}/collections");
        return response.IsSuccessStatusCode;
    }
    catch
    {
        return false;
    }
}

static void ReconcileCategoryMigrationHistoryForSqlite(AiDeskContext db, ILogger logger)
{
    const string migrationId = "20260517083000_AddKnowledgeBaseCategories";
    const string productVersion = "10.0.5";

    using var connection = db.Database.GetDbConnection();
    var shouldClose = connection.State != ConnectionState.Open;
    if (shouldClose)
    {
        connection.Open();
    }

    try
    {
        var hasMigration = false;
        using (var migrationCommand = connection.CreateCommand())
        {
            migrationCommand.CommandText = "SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = $migrationId LIMIT 1;";
            var migrationParam = migrationCommand.CreateParameter();
            migrationParam.ParameterName = "$migrationId";
            migrationParam.Value = migrationId;
            migrationCommand.Parameters.Add(migrationParam);
            hasMigration = migrationCommand.ExecuteScalar() != null;
        }

        if (hasMigration)
        {
            return;
        }

        long categoryColumnCount;
        using (var columnsCommand = connection.CreateCommand())
        {
            columnsCommand.CommandText = @"
SELECT COUNT(*)
FROM pragma_table_info('KnowledgeBases')
WHERE name IN ('CategoryLarge', 'CategoryMedium', 'CategorySmall');";
            categoryColumnCount = Convert.ToInt64(columnsCommand.ExecuteScalar() ?? 0);
        }

        if (categoryColumnCount == 3)
        {
            using var insertCommand = connection.CreateCommand();
            insertCommand.CommandText = "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ($migrationId, $productVersion);";

            var insertMigrationParam = insertCommand.CreateParameter();
            insertMigrationParam.ParameterName = "$migrationId";
            insertMigrationParam.Value = migrationId;
            insertCommand.Parameters.Add(insertMigrationParam);

            var versionParam = insertCommand.CreateParameter();
            versionParam.ParameterName = "$productVersion";
            versionParam.Value = productVersion;
            insertCommand.Parameters.Add(versionParam);

            insertCommand.ExecuteNonQuery();
            logger.LogWarning("마이그레이션 이력 자동 보정 적용: {MigrationId}", migrationId);
        }
    }
    finally
    {
        if (shouldClose)
        {
            connection.Close();
        }
    }
}

static void EnsureChatbotCategoryTable(AiDeskContext db, ILogger logger)
{
    try
    {
        if (db.Database.IsSqlite())
        {
            db.Database.ExecuteSqlRaw(@"
CREATE TABLE IF NOT EXISTS ChatbotCategory (
    CategoryID INTEGER NOT NULL,
    ParentCategoryID INTEGER NULL,
    Type TEXT NOT NULL,
    Title TEXT NOT NULL,
    Content TEXT NULL,
    UseYN TEXT NOT NULL DEFAULT 'Y',
    SortOrder INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    CreatedBy INTEGER NULL,
    CONSTRAINT PK_CHATBOTCATEGORY PRIMARY KEY (CategoryID)
);
CREATE INDEX IF NOT EXISTS IX_ChatbotCategory_Parent_UseYN_Sort
ON ChatbotCategory (ParentCategoryID, UseYN, SortOrder);
");
            return;
        }

        if (db.Database.IsSqlServer())
        {
            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'[dbo].[ChatbotCategory]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ChatbotCategory](
        [CategoryID] INT NOT NULL,
        [ParentCategoryID] INT NULL,
        [Type] VARCHAR(20) NOT NULL,
        [Title] NVARCHAR(500) NOT NULL,
        [Content] NVARCHAR(1000) NULL,
        [UseYN] CHAR(1) NOT NULL CONSTRAINT [DF_ChatbotCategory_UseYN] DEFAULT ('Y'),
        [SortOrder] INT NOT NULL CONSTRAINT [DF_ChatbotCategory_SortOrder] DEFAULT (0),
        [CreatedAt] DATETIME NOT NULL CONSTRAINT [DF_ChatbotCategory_CreatedAt] DEFAULT (GETDATE()),
        [CreatedBy] INT NULL,
        CONSTRAINT [PK_CHATBOTCATEGORY] PRIMARY KEY CLUSTERED ([CategoryID] ASC)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_ChatbotCategory_Parent_UseYN_Sort'
      AND object_id = OBJECT_ID(N'[dbo].[ChatbotCategory]')
)
BEGIN
    CREATE INDEX [IX_ChatbotCategory_Parent_UseYN_Sort]
    ON [dbo].[ChatbotCategory]([ParentCategoryID], [UseYN], [SortOrder]);
END;
");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "ChatbotCategory 테이블 보장 처리 중 오류가 발생했습니다.");
        throw;
    }
}
