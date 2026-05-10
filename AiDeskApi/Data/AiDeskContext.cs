using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using AiDeskApi.Models;

namespace AiDeskApi.Data
{
    // AiDesk 도메인에서 사용하는 모든 엔티티와 관계를 관리하는 EF Core 컨텍스트
    public class AiDeskContext : DbContext
    {
        public AiDeskContext(DbContextOptions<AiDeskContext> options) : base(options)
        {
        }

        // 인증
        public DbSet<User> Users { get; set; } = null!;

        // 고객/상담/지식베이스 핵심 테이블
        public DbSet<KnowledgeBase> KnowledgeBases { get; set; } = null!;
        public DbSet<KnowledgeBaseHistory> KnowledgeBaseHistories { get; set; } = null!;
        public DbSet<KnowledgeBaseExpectedQuestionHistory> KnowledgeBaseExpectedQuestionHistories { get; set; } = null!;
        public DbSet<KbPlatform> KbPlatforms { get; set; } = null!;
        public DbSet<KnowledgeBaseExpectedQuestion> KnowledgeBaseExpectedQuestions { get; set; } = null!;
        public DbSet<LowSimilarityQuestion> LowSimilarityQuestions { get; set; } = null!;
        public DbSet<KnowledgeBaseWriterPromptTemplate> KnowledgeBaseWriterPromptTemplates { get; set; } = null!;
        public DbSet<ChatbotPromptTemplate> ChatbotPromptTemplates { get; set; } = null!;

        // 챗봇 세션/메시지
        public DbSet<ChatSession> ChatSessions { get; set; } = null!;
        public DbSet<ChatMessage> ChatMessages { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            var utcDateTimeConverter = new ValueConverter<DateTime, DateTime>(
                value => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(),
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

            var utcNullableDateTimeConverter = new ValueConverter<DateTime?, DateTime?>(
                value => value.HasValue ? (value.Value.Kind == DateTimeKind.Utc ? value.Value : value.Value.ToUniversalTime()) : value,
                value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value);

            // User 설정
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.LoginId).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Username).IsRequired().HasMaxLength(50);
                entity.Property(e => e.PasswordHash).IsRequired();
                entity.Property(e => e.Role).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
                entity.Property(e => e.CreatedAt).HasConversion(utcDateTimeConverter);
                entity.Property(e => e.ApprovedAt).HasConversion(utcNullableDateTimeConverter);
                entity.Property(e => e.LastLoginAt).HasConversion(utcNullableDateTimeConverter);
                entity.HasIndex(e => e.LoginId).IsUnique();
            });

            modelBuilder.Entity<KnowledgeBase>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Content).IsRequired();
                entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(100);
                entity.Property(e => e.UpdatedBy).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Visibility).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Platform).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Keywords).HasMaxLength(500);
                entity.Property(e => e.CreatedAt).HasConversion(utcDateTimeConverter);
                entity.Property(e => e.UpdatedAt).HasConversion(utcDateTimeConverter);
                entity.HasIndex(e => new { e.Visibility, e.Platform, e.UpdatedAt });
                entity.HasIndex(e => e.UpdatedAt);
                entity.HasMany(e => e.ExpectedQuestions)
                    .WithOne(x => x.KnowledgeBase)
                    .HasForeignKey(x => x.KnowledgeBaseId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<KbPlatform>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
                entity.Property(e => e.CreatedAt).HasConversion(utcDateTimeConverter);
                entity.HasIndex(e => e.Name).IsUnique();
            });

            modelBuilder.Entity<KnowledgeBaseHistory>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Action).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Actor).IsRequired().HasMaxLength(100);
                entity.Property(e => e.BeforeTitle).HasMaxLength(200);
                entity.Property(e => e.AfterTitle).HasMaxLength(200);
                entity.Property(e => e.BeforeVisibility).HasMaxLength(20);
                entity.Property(e => e.AfterVisibility).HasMaxLength(20);
                entity.Property(e => e.BeforePlatform).HasMaxLength(200);
                entity.Property(e => e.AfterPlatform).HasMaxLength(200);
                entity.Property(e => e.BeforeKeywords).HasMaxLength(500);
                entity.Property(e => e.AfterKeywords).HasMaxLength(500);
                entity.Property(e => e.ChangedAt).HasConversion(utcDateTimeConverter);
                entity.HasIndex(e => new { e.KnowledgeBaseId, e.ChangedAt });
            });

            modelBuilder.Entity<KnowledgeBaseExpectedQuestionHistory>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Action).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Actor).IsRequired().HasMaxLength(100);
                entity.Property(e => e.BeforeQuestion).HasMaxLength(500);
                entity.Property(e => e.AfterQuestion).HasMaxLength(500);
                entity.Property(e => e.ChangedAt).HasConversion(utcDateTimeConverter);
                entity.HasIndex(e => new { e.KnowledgeBaseId, e.ChangedAt });
            });

            modelBuilder.Entity<KnowledgeBaseExpectedQuestion>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Question).IsRequired().HasMaxLength(500);
                entity.Property(e => e.CreatedAt).HasConversion(utcDateTimeConverter);
                entity.HasIndex(e => e.KnowledgeBaseId);
            });

            modelBuilder.Entity<LowSimilarityQuestion>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Question).IsRequired().HasMaxLength(1000);
                entity.Property(e => e.Role).IsRequired().HasMaxLength(20);
                entity.Property(e => e.ActorName).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Platform).IsRequired().HasMaxLength(50);
                entity.Property(e => e.TopMatchedEvidenceText).HasColumnName("TopMatchedQuestion").HasMaxLength(500);
                entity.Property(e => e.CreatedAt).HasConversion(utcDateTimeConverter);
                entity.Property(e => e.ResolvedAt).HasConversion(utcNullableDateTimeConverter);
                entity.HasIndex(e => new { e.IsResolved, e.CreatedAt });
            });

            modelBuilder.Entity<KnowledgeBaseWriterPromptTemplate>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.KeywordSystemPrompt).IsRequired();
                entity.Property(e => e.KeywordRulesPrompt).IsRequired();
                entity.Property(e => e.ExpectedQuestionSystemPrompt).IsRequired();
                entity.Property(e => e.ExpectedQuestionRulesPrompt).IsRequired();
                entity.Property(e => e.TopicKeywordSystemPrompt).IsRequired();
                entity.Property(e => e.TopicKeywordRulesPrompt).IsRequired();
                entity.Property(e => e.AnswerRefineSystemPrompt).IsRequired();
                entity.Property(e => e.AnswerRefineRulesPrompt).IsRequired();
                entity.Property(e => e.CreatedAt).HasConversion(utcDateTimeConverter);
                entity.Property(e => e.UpdatedAt).HasConversion(utcDateTimeConverter);
            });

            modelBuilder.Entity<ChatbotPromptTemplate>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.UserSystemPrompt).IsRequired();
                entity.Property(e => e.AdminSystemPrompt).IsRequired();
                entity.Property(e => e.UserRulesPrompt).IsRequired();
                entity.Property(e => e.AdminRulesPrompt).IsRequired();
                entity.Property(e => e.UserLowSimilarityMessage).IsRequired();
                entity.Property(e => e.AdminLowSimilarityMessage).IsRequired();
                entity.Property(e => e.SimilarityThreshold).IsRequired();
                entity.Property(e => e.CreatedAt).HasConversion(utcDateTimeConverter);
                entity.Property(e => e.UpdatedAt).HasConversion(utcDateTimeConverter);
            });

            modelBuilder.Entity<ChatMessage>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Role).IsRequired().HasMaxLength(10);
                entity.Property(e => e.Content).IsRequired();
                entity.Property(e => e.CreatedAt).HasConversion(utcDateTimeConverter);
                entity.Property(e => e.RelatedKbIds);
                entity.Property(e => e.RelatedKbMeta);
                entity.Property(e => e.RetrievalDebugMeta);
                entity.Property(e => e.TopSimilarity);
                entity.Property(e => e.IsLowSimilarity);
                entity.HasOne(e => e.Session)
                    .WithMany(s => s.Messages)
                    .HasForeignKey(e => e.SessionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ChatSession>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.UserRole).IsRequired().HasMaxLength(10);
                entity.Property(e => e.ActorName).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Platform).IsRequired().HasMaxLength(50);
                entity.Property(e => e.CreatedAt).HasConversion(utcDateTimeConverter);
                entity.Property(e => e.UpdatedAt).HasConversion(utcDateTimeConverter);
            });
        }
    }
}
