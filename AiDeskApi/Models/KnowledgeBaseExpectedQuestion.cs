namespace AiDeskApi.Models
{
    public class KnowledgeBaseExpectedQuestion
    {
        public int Id { get; set; }
        public int KnowledgeBaseId { get; set; }
        public string Question { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public KnowledgeBase? KnowledgeBase { get; set; }
    }
}
