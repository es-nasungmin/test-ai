namespace AiDeskApi.Models
{
    public class KnowledgeBaseWriterPromptTemplate
    {
        public int Id { get; set; }
        // 키워드 생성 프롬프트
        public string KeywordSystemPrompt { get; set; } = string.Empty;
        public string KeywordRulesPrompt { get; set; } = string.Empty;
        // 예상질문 생성 프롬프트
        public string ExpectedQuestionSystemPrompt { get; set; } = string.Empty;
        public string ExpectedQuestionRulesPrompt { get; set; } = string.Empty;
        // 주제 키워드 생성 프롬프트
        public string TopicKeywordSystemPrompt { get; set; } = string.Empty;
        public string TopicKeywordRulesPrompt { get; set; } = string.Empty;
        // 답변 정제 프롬프트
        // - 관리자
        public string AnswerRefineSystemPrompt { get; set; } = string.Empty;
        // - 유저
        public string AnswerRefineRulesPrompt { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}