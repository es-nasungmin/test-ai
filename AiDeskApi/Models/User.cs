namespace AiDeskApi.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Role { get; set; } = "user"; // admin, user, guest
        public bool IsActive { get; set; } = true;
        public bool IsApproved { get; set; } = false; // 관리자 승인 필요
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ApprovedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
    }
}
