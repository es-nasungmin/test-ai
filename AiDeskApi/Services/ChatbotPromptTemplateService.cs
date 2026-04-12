namespace AiDeskApi.Services
{
    public interface IChatbotPromptTemplateService
    {
        string UserSystemPrompt { get; }
        string AdminSystemPrompt { get; }
        string UserRulesPrompt { get; }
        string AdminRulesPrompt { get; }
        string UserLowSimilarityMessage { get; }
        string AdminLowSimilarityMessage { get; }
        float SimilarityThreshold { get; }

        void Update(
            string userSystemPrompt,
            string adminSystemPrompt,
            string userRulesPrompt,
            string adminRulesPrompt,
            string userLowSimilarityMessage,
            string adminLowSimilarityMessage,
            float similarityThreshold);
    }

    public class ChatbotPromptTemplateService : IChatbotPromptTemplateService
    {
        private readonly object _sync = new();

        private string _userSystemPrompt = "당신은 고객 지원 챗봇입니다. 친절하고 정확하게 한국어로 답하세요.";
        private string _adminSystemPrompt = "당신은 관리자 지원 어시스턴트입니다. 내부 운영 관점에서 근거 중심으로 한국어로 답하세요.";

        private string _userRulesPrompt = "1) 유사도/점수/벡터/랭킹 같은 내부 용어는 절대 언급하지 않는다\n2) 확실한 사실만 간결하게 답한다\n3) 근거가 약하면 추측하지 말고 관리자 문의를 안내한다\n4) 사용자가 바로 실행할 수 있는 단계 중심으로 설명한다";
        private string _adminRulesPrompt = "1) 충돌 사례가 있으면 우선안을 먼저 제시하고 대안을 짧게 덧붙인다\n2) 단정이 어려우면 확인 질문 1개를 제시한다\n3) 운영자가 실행할 액션 아이템 위주로 정리한다";

        private string _userLowSimilarityMessage = "현재 보유한 지식으로는 정확한 안내가 어렵습니다. 관리자에게 문의해 주세요.";
        private string _adminLowSimilarityMessage = "현재 KB 유사도가 낮아 신뢰 가능한 답변 생성이 어렵습니다. 해당 질문을 신규 KB 후보로 등록해 주세요.";

        private float _similarityThreshold = 0.42f;

        public string UserSystemPrompt { get { lock (_sync) { return _userSystemPrompt; } } }
        public string AdminSystemPrompt { get { lock (_sync) { return _adminSystemPrompt; } } }
        public string UserRulesPrompt { get { lock (_sync) { return _userRulesPrompt; } } }
        public string AdminRulesPrompt { get { lock (_sync) { return _adminRulesPrompt; } } }
        public string UserLowSimilarityMessage { get { lock (_sync) { return _userLowSimilarityMessage; } } }
        public string AdminLowSimilarityMessage { get { lock (_sync) { return _adminLowSimilarityMessage; } } }
        public float SimilarityThreshold { get { lock (_sync) { return _similarityThreshold; } } }

        public void Update(
            string userSystemPrompt,
            string adminSystemPrompt,
            string userRulesPrompt,
            string adminRulesPrompt,
            string userLowSimilarityMessage,
            string adminLowSimilarityMessage,
            float similarityThreshold)
        {
            if (string.IsNullOrWhiteSpace(userSystemPrompt)) throw new ArgumentException("사용자 시스템 프롬프트는 비울 수 없습니다.");
            if (string.IsNullOrWhiteSpace(adminSystemPrompt)) throw new ArgumentException("관리자 시스템 프롬프트는 비울 수 없습니다.");
            if (string.IsNullOrWhiteSpace(userRulesPrompt)) throw new ArgumentException("사용자 규칙 프롬프트는 비울 수 없습니다.");
            if (string.IsNullOrWhiteSpace(adminRulesPrompt)) throw new ArgumentException("관리자 규칙 프롬프트는 비울 수 없습니다.");
            if (string.IsNullOrWhiteSpace(userLowSimilarityMessage)) throw new ArgumentException("사용자 저유사도 안내문은 비울 수 없습니다.");
            if (string.IsNullOrWhiteSpace(adminLowSimilarityMessage)) throw new ArgumentException("관리자 저유사도 안내문은 비울 수 없습니다.");
            if (similarityThreshold <= 0f || similarityThreshold >= 1f) throw new ArgumentException("유사도 임계치는 0~1 사이여야 합니다.");

            lock (_sync)
            {
                _userSystemPrompt = userSystemPrompt.Trim();
                _adminSystemPrompt = adminSystemPrompt.Trim();
                _userRulesPrompt = userRulesPrompt.Trim();
                _adminRulesPrompt = adminRulesPrompt.Trim();
                _userLowSimilarityMessage = userLowSimilarityMessage.Trim();
                _adminLowSimilarityMessage = adminLowSimilarityMessage.Trim();
                _similarityThreshold = similarityThreshold;
            }
        }
    }
}
