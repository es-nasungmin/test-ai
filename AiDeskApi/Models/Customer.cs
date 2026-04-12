namespace AiDeskApi.Models
{
    public class Customer
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public string? Company { get; set; }
        public string? Position { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastContactDate { get; set; }
        public string? Status { get; set; } = "Active"; // Active, Inactive, Lead

        // Navigation property
        public ICollection<Interaction>? Interactions { get; set; }
    }
}
