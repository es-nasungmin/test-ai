namespace CrmApi.Models
{
    public class ChatMessage
    {
        public int Id { get; set; }
        public int SessionId { get; set; }
        // "user" 또는 "bot"
        public string Role { get; set; } = "user";
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        // 답변 생성에 참조된 KB ID 목록 (JSON 배열)
        public string? RelatedKbIds { get; set; }

        public ChatSession? Session { get; set; }
    }
}
