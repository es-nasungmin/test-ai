using System.Text.Json.Serialization;

namespace CrmApi.Models
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

        // Foreign key
        [JsonIgnore]
        public Customer? Customer { get; set; }
    }
}
