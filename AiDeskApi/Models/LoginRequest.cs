namespace AiDeskApi.Models
{
    public class LoginRequest
    {
        public string LoginId { get; set; } = string.Empty;
        public string? Username { get; set; }
        public string Password { get; set; } = string.Empty;
    }
}
