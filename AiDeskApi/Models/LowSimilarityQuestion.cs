namespace AiDeskApi.Models
{
    public class LowSimilarityQuestion
    {
        public int Id { get; set; }
        public string Question { get; set; } = string.Empty;
        public string Role { get; set; } = "user";
        public string Platform { get; set; } = "web";
        public float TopSimilarity { get; set; }
        public string? TopMatchedQuestion { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsResolved { get; set; } = false;
        public DateTime? ResolvedAt { get; set; }
    }
}
