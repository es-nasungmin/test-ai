namespace AiDeskApi.Models
{
    public class KnowledgeBaseSimilarQuestion
    {
        public int Id { get; set; }
        public int KnowledgeBaseId { get; set; }
        public string Question { get; set; } = string.Empty;
        public string? QuestionEmbedding { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public KnowledgeBase? KnowledgeBase { get; set; }
    }
}
