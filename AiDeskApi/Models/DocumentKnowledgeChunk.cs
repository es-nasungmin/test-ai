namespace AiDeskApi.Models
{
    public class DocumentKnowledgeChunk
    {
        public int Id { get; set; }
        public int DocumentKnowledgeId { get; set; }
        public int PageNumber { get; set; }
        public int ChunkOrder { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? ContentEmbedding { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DocumentKnowledge DocumentKnowledge { get; set; } = null!;
    }
}
