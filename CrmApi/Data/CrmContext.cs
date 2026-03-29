using Microsoft.EntityFrameworkCore;
using CrmApi.Models;

namespace CrmApi.Data
{
    // CRM 도메인에서 사용하는 모든 엔티티와 관계를 관리하는 EF Core 컨텍스트
    public class CrmContext : DbContext
    {
        public CrmContext(DbContextOptions<CrmContext> options) : base(options)
        {
        }

        // 고객/상담/지식베이스 핵심 테이블
        public DbSet<Customer> Customers { get; set; } = null!;
        public DbSet<Interaction> Interactions { get; set; } = null!;
        public DbSet<KnowledgeBase> KnowledgeBases { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

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
        }
    }
}
