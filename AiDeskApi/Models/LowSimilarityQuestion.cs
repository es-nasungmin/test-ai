namespace AiDeskApi.Models
{
    public class LowSimilarityQuestion
    {
        public int Id { get; set; }
        public string Question { get; set; } = string.Empty;
        public string Role { get; set; } = "user";
        public string ActorName { get; set; } = "알 수 없음";
        public string Platform { get; set; } = "web";
        public float TopSimilarity { get; set; }
        public string? TopMatchedQuestion { get; set; }
        public string? TopMatchedKbTitle { get; set; }
        public string? TopMatchedKbContent { get; set; }
        public int? SessionId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsResolved { get; set; } = false;
        public DateTime? ResolvedAt { get; set; }
    }
}
