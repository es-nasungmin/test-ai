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

        // 가시성 & 승인 관련
        // "internal" = 상담원 전용, "common" = 일반 공개
        public string Visibility { get; set; } = "internal";
        // "case" = 상담에서 자동 추출, "official" = 관리자가 직접 작성
        public string SourceType { get; set; } = "case";
        public bool IsApproved { get; set; } = false;
        public string? Tags { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }
    }
}
