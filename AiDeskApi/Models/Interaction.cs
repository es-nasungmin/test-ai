using System.Text.Json.Serialization;

namespace AiDeskApi.Models
{
    public class Interaction
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public string? Type { get; set; } // Call, Email, Meeting, Note
        public string? Content { get; set; }
        public string? Outcome { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ScheduledDate { get; set; }
        public bool IsCompleted { get; set; } = false;
        // 레거시 호환용 플래그 (현재 CRM-KB 자동연동에서는 사용하지 않음)
        public bool IsExternalProvided { get; set; } = false;

        // Foreign key
        [JsonIgnore]
        public Customer? Customer { get; set; }
    }
}
