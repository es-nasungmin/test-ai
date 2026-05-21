using System.ComponentModel.DataAnnotations;

namespace AiDeskApi.Models
{
    public class ChatbotCategory
    {
        public int CategoryId { get; set; }

        public int? ParentCategoryId { get; set; }

        [Required]
        [MaxLength(20)]
        public string Type { get; set; } = "MENU";

        [Required]
        [MaxLength(500)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Content { get; set; }

        [Required]
        [MaxLength(1)]
        public string UseYN { get; set; } = "Y";

        public int SortOrder { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int? CreatedBy { get; set; }
    }
}
