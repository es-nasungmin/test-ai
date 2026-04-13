using Microsoft.EntityFrameworkCore;
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
        public DbSet<Customer> Customers { get; set; } = null!;
        public DbSet<Interaction> Interactions { get; set; } = null!;
        public DbSet<KnowledgeBase> KnowledgeBases { get; set; } = null!;
        public DbSet<KbPlatform> KbPlatforms { get; set; } = null!;
        public DbSet<KnowledgeBaseSimilarQuestion> KnowledgeBaseSimilarQuestions { get; set; } = null!;
        public DbSet<LowSimilarityQuestion> LowSimilarityQuestions { get; set; } = null!;

        // 챗봇 세션/메시지
        public DbSet<ChatSession> ChatSessions { get; set; } = null!;
        public DbSet<ChatMessage> ChatMessages { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User 설정
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Username).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Email).IsRequired().HasMaxLength(100);
                entity.Property(e => e.PasswordHash).IsRequired();
                entity.Property(e => e.Role).IsRequired().HasMaxLength(20);
                entity.HasIndex(e => e.Username).IsUnique();
                entity.HasIndex(e => e.Email).IsUnique();
            });

            modelBuilder.Entity<Customer>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.PhoneNumber).HasMaxLength(20);
                entity.Property(e => e.Email).HasMaxLength(100);
                entity.Property(e => e.Company).HasMaxLength(100);
                entity.Property(e => e.Position).HasMaxLength(50);
                entity.Property(e => e.Notes).HasMaxLength(500);
                entity.Property(e => e.Status).HasMaxLength(20);
                entity.HasMany(e => e.Interactions)
                    .WithOne(i => i.Customer)
                    .HasForeignKey(i => i.CustomerId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Interaction>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Type).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Content).IsRequired().HasMaxLength(1000);
                entity.Property(e => e.Outcome).HasMaxLength(500);
                entity.HasOne(e => e.Customer)
                    .WithMany(c => c.Interactions)
                    .HasForeignKey(e => e.CustomerId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<KnowledgeBase>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).HasMaxLength(200);
                entity.Property(e => e.Problem).IsRequired().HasMaxLength(500);
                entity.Property(e => e.Solution).IsRequired();
                entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(100);
                entity.Property(e => e.UpdatedBy).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Visibility).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Platform).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Tags).HasMaxLength(500);
                entity.HasMany(e => e.SimilarQuestions)
                    .WithOne(x => x.KnowledgeBase)
                    .HasForeignKey(x => x.KnowledgeBaseId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<KbPlatform>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
                entity.HasIndex(e => e.Name).IsUnique();
            });

            modelBuilder.Entity<KnowledgeBaseSimilarQuestion>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Question).IsRequired().HasMaxLength(500);
                entity.HasIndex(e => e.KnowledgeBaseId);
            });

            modelBuilder.Entity<LowSimilarityQuestion>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Question).IsRequired().HasMaxLength(1000);
                entity.Property(e => e.Role).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Platform).IsRequired().HasMaxLength(50);
                entity.Property(e => e.TopMatchedQuestion).HasMaxLength(500);
                entity.HasIndex(e => new { e.IsResolved, e.CreatedAt });
            });

            modelBuilder.Entity<ChatMessage>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Role).IsRequired().HasMaxLength(10);
                entity.Property(e => e.Content).IsRequired();
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
                entity.Property(e => e.Platform).IsRequired().HasMaxLength(50);
            });
        }
    }
}
