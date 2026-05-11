using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiDeskApi.Migrations
{
    /// <inheritdoc />
    public partial class AddVectorSyncStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatbotPromptTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserSystemPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    AdminSystemPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    UserRulesPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    AdminRulesPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    UserLowSimilarityMessage = table.Column<string>(type: "TEXT", nullable: false),
                    AdminLowSimilarityMessage = table.Column<string>(type: "TEXT", nullable: false),
                    SimilarityThreshold = table.Column<float>(type: "REAL", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatbotPromptTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    UserRole = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ActorName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MessageCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KbPlatforms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KbPlatforms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeBaseExpectedQuestionHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    KnowledgeBaseId = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BeforeQuestion = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AfterQuestion = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeBaseExpectedQuestionHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeBaseHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    KnowledgeBaseId = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BeforeTitle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    BeforeContent = table.Column<string>(type: "TEXT", nullable: true),
                    BeforeVisibility = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    BeforePlatform = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    BeforeKeywords = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AfterTitle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AfterContent = table.Column<string>(type: "TEXT", nullable: true),
                    AfterVisibility = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    AfterPlatform = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AfterKeywords = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeBaseHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeBases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ViewCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Visibility = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Keywords = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    VectorSyncStatus = table.Column<string>(type: "TEXT", nullable: false),
                    VectorSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeBases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeBaseWriterPromptTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    KeywordSystemPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    KeywordRulesPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    ExpectedQuestionSystemPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    ExpectedQuestionRulesPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    TopicKeywordSystemPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    TopicKeywordRulesPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    AnswerRefineSystemPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    AnswerRefineRulesPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeBaseWriterPromptTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LowSimilarityQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Question = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ActorName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TopSimilarity = table.Column<float>(type: "REAL", nullable: false),
                    TopMatchedQuestion = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    TopMatchedKbTitle = table.Column<string>(type: "TEXT", nullable: true),
                    TopMatchedKbContent = table.Column<string>(type: "TEXT", nullable: true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsResolved = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LowSimilarityQuestions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LoginId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RelatedKbIds = table.Column<string>(type: "TEXT", nullable: true),
                    RelatedKbMeta = table.Column<string>(type: "TEXT", nullable: true),
                    RetrievalDebugMeta = table.Column<string>(type: "TEXT", nullable: true),
                    TopSimilarity = table.Column<float>(type: "REAL", nullable: true),
                    IsLowSimilarity = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_ChatSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ChatSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeBaseExpectedQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    KnowledgeBaseId = table.Column<int>(type: "INTEGER", nullable: false),
                    Question = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeBaseExpectedQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeBaseExpectedQuestions_KnowledgeBases_KnowledgeBaseId",
                        column: x => x.KnowledgeBaseId,
                        principalTable: "KnowledgeBases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_SessionId",
                table: "ChatMessages",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_KbPlatforms_Name",
                table: "KbPlatforms",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseExpectedQuestionHistories_KnowledgeBaseId_ChangedAt",
                table: "KnowledgeBaseExpectedQuestionHistories",
                columns: new[] { "KnowledgeBaseId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseExpectedQuestions_KnowledgeBaseId",
                table: "KnowledgeBaseExpectedQuestions",
                column: "KnowledgeBaseId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseHistories_KnowledgeBaseId_ChangedAt",
                table: "KnowledgeBaseHistories",
                columns: new[] { "KnowledgeBaseId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBases_UpdatedAt",
                table: "KnowledgeBases",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBases_Visibility_Platform_UpdatedAt",
                table: "KnowledgeBases",
                columns: new[] { "Visibility", "Platform", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LowSimilarityQuestions_IsResolved_CreatedAt",
                table: "LowSimilarityQuestions",
                columns: new[] { "IsResolved", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_LoginId",
                table: "Users",
                column: "LoginId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatbotPromptTemplates");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "KbPlatforms");

            migrationBuilder.DropTable(
                name: "KnowledgeBaseExpectedQuestionHistories");

            migrationBuilder.DropTable(
                name: "KnowledgeBaseExpectedQuestions");

            migrationBuilder.DropTable(
                name: "KnowledgeBaseHistories");

            migrationBuilder.DropTable(
                name: "KnowledgeBaseWriterPromptTemplates");

            migrationBuilder.DropTable(
                name: "LowSimilarityQuestions");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "ChatSessions");

            migrationBuilder.DropTable(
                name: "KnowledgeBases");
        }
    }
}
