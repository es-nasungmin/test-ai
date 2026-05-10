namespace AiDeskApi.Models
{
    public class KnowledgeBaseHistory
    {
        public int Id { get; set; }
        public int KnowledgeBaseId { get; set; }
        // "생성", "수정", "삭제" 등의 작업 유형
        public string Action { get; set; } = string.Empty;
        public string Actor { get; set; } = "알 수 없음";
        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

        public string? BeforeTitle { get; set; }
        public string? BeforeContent { get; set; }
        public string? BeforeVisibility { get; set; }
        public string? BeforePlatform { get; set; }
        public string? BeforeKeywords { get; set; }

        public string? AfterTitle { get; set; }
        public string? AfterContent { get; set; }
        public string? AfterVisibility { get; set; }
        public string? AfterPlatform { get; set; }
        public string? AfterKeywords { get; set; }
    }
}
