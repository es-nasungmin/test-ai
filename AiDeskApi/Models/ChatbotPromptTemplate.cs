namespace AiDeskApi.Models
{
    public class ChatbotPromptTemplate
    {
        public int Id { get; set; }
        public string UserSystemPrompt { get; set; } = string.Empty;
        public string AdminSystemPrompt { get; set; } = string.Empty;
        public string UserRulesPrompt { get; set; } = string.Empty;
        public string AdminRulesPrompt { get; set; } = string.Empty;
        public string UserLowSimilarityMessage { get; set; } = string.Empty;
        public string AdminLowSimilarityMessage { get; set; } = string.Empty;
        public float SimilarityThreshold { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}