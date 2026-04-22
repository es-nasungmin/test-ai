namespace AiDeskApi.Models
{
    public class DocumentKnowledge
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Visibility { get; set; } = "admin";
        public string Platform { get; set; } = "공통";
        public string Status { get; set; } = "ready";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; } = "시스템";
        public string UpdatedBy { get; set; } = "시스템";

        public ICollection<DocumentKnowledgeChunk> Chunks { get; set; } = new List<DocumentKnowledgeChunk>();
    }
}
