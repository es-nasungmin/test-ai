namespace AiDeskApi.Models
{
    public class KbPlatform
    {
        public int Id { get; set; }
        public string Name { get; set; } = "공통";
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}