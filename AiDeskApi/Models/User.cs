namespace AiDeskApi.Models
{
    public class User
    {
        public int Id { get; set; }
        public string LoginId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Role { get; set; } = "user"; // admin, user
        public string Status { get; set; } = "pending"; // pending(대기), approved(승인), deleted(삭제)
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ApprovedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
    }
}
