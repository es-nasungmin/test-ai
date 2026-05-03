namespace AiDeskApi.Models
{
    public class RegisterRequest
    {
        public string LoginId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? ConfirmPassword { get; set; }
    }
}
