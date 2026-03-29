namespace CrmApi.Models
{
    public class KnowledgeBase
    {
        public int Id { get; set; }
        public int? SourceInteractionId { get; set; }  // 어느 상담에서 나왔는지
        public string? Problem { get; set; }           // "계산서 조회 안됨"
        public string? Solution { get; set; }          // "국세청 홈택스에서 확인..."
        public string? ProblemEmbedding { get; set; }  // JSON 벡터
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int ViewCount { get; set; } = 0;        // 자주 참조되는 KB
    }
}
