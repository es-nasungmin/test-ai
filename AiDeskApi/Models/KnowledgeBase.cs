namespace AiDeskApi.Models
{
    public class KnowledgeBase
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; } = "시스템";
        public string UpdatedBy { get; set; } = "시스템";
        public int ViewCount { get; set; } = 0;

        // admin = 관리자 전용, user = 일반 사용자 공개
        public string Visibility { get; set; } = "admin";
        // 공통 또는 특정 플랫폼 식별자 (예: 공통, windows, mobile-app)
        public string Platform { get; set; } = "공통";
        public string? Keywords { get; set; }

        // 분류(대/중/소). 선택 입력이며, 챗봇 분류 선택 필터링에 사용
        public string? CategoryLarge { get; set; }
        public string? CategoryMedium { get; set; }
        public string? CategorySmall { get; set; }

        // 벡터DB 동기화 상태 (pending: 대기, synced: 완료, failed: 실패)
        public string VectorSyncStatus { get; set; } = "pending";
        // 마지막 벡터DB 동기화 시간
        public DateTime? VectorSyncedAt { get; set; }

        public ICollection<KnowledgeBaseExpectedQuestion> ExpectedQuestions { get; set; } = new List<KnowledgeBaseExpectedQuestion>();
    }
}
