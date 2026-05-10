namespace AiDeskApi.Models
{
    public class KnowledgeBaseExpectedQuestionHistory
    {
        public int Id { get; set; }
        public int KnowledgeBaseId { get; set; }
        // "생성", "수정", "삭제" 등의 작업 유형
        public string Action { get; set; } = string.Empty;
        public string Actor { get; set; } = "알 수 없음";
        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
        public string? BeforeQuestion { get; set; }
        public string? AfterQuestion { get; set; }
    }
}
