namespace AiDeskApi.Models
{
    public class KnowledgeBase
    {
        public int Id { get; set; }
        public string? Problem { get; set; }
        public string? Solution { get; set; }
        public string? ProblemEmbedding { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public int ViewCount { get; set; } = 0;

        // admin = 관리자 전용, user = 일반 사용자 공개
        public string Visibility { get; set; } = "admin";
        // 공통 또는 특정 플랫폼 식별자 (예: 공통, windows, mobile-app)
        public string Platform { get; set; } = "공통";
        public string? Tags { get; set; }

        public ICollection<KnowledgeBaseSimilarQuestion> SimilarQuestions { get; set; } = new List<KnowledgeBaseSimilarQuestion>();
    }
}
