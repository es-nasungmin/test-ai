namespace AiDeskApi.Models
{
    public class KnowledgeBaseExpectedQuestionHistory
    {
        public int Id { get; set; }
        public int KnowledgeBaseId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string Actor { get; set; } = "알 수 없음";
        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
        public string? BeforeQuestion { get; set; }
        public string? AfterQuestion { get; set; }
    }
}
