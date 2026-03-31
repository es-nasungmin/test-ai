namespace CrmApi.Models
{
    public class ChatSession
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        // "admin" 또는 "user"
        public string UserRole { get; set; } = "user";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public int MessageCount { get; set; } = 0;

        public ICollection<ChatMessage>? Messages { get; set; }
    }
}
