namespace AiDeskApi.Models
{
    public class KnowledgeBaseWriterPromptTemplate
    {
        public int Id { get; set; }
        public string KeywordSystemPrompt { get; set; } = string.Empty;
        public string KeywordRulesPrompt { get; set; } = string.Empty;
        public string TopicKeywordSystemPrompt { get; set; } = string.Empty;
        public string TopicKeywordRulesPrompt { get; set; } = string.Empty;
        public string AnswerRefineSystemPrompt { get; set; } = string.Empty;
        public string AnswerRefineRulesPrompt { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
