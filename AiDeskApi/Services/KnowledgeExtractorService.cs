using System.Net.Http.Json;
using System.Text.Json;

namespace AiDeskApi.Services
{
    public interface IKnowledgeExtractorService
    {
        Task<List<string>> GenerateSimilarQuestionsAsync(string title, string content, int count, string systemPrompt, string rulesPrompt);
        Task<List<string>> GenerateKeywordsAsync(string content, List<string>? additionalContent, int count, string systemPrompt, string rulesPrompt, string source = "question");
        Task<string> RefineSolutionAsync(string title, string content, string systemPrompt, string rulesPrompt);
    }

    public class KnowledgeExtractorService : IKnowledgeExtractorService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<KnowledgeExtractorService> _logger;
        private readonly string _chatCompletionsEndpoint;
        private readonly string _chatModel;

        public KnowledgeExtractorService(HttpClient httpClient, IConfiguration configuration, ILogger<KnowledgeExtractorService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;

            var apiKey = configuration["OpenAI:ApiKey"];
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            _chatCompletionsEndpoint = configuration["OpenAI:Endpoint"] ?? "https://api.openai.com/v1/chat/completions";
            _chatModel = configuration["OpenAI:Model"] ?? "gpt-4o-mini";
        }

        public async Task<List<string>> GenerateSimilarQuestionsAsync(string title, string content, int count, string systemPrompt, string rulesPrompt)
        {
            if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("내용이 필요합니다.");
            if (string.IsNullOrWhiteSpace(systemPrompt)) throw new ArgumentException("예상질문 시스템 프롬프트가 필요합니다.");
            if (string.IsNullOrWhiteSpace(rulesPrompt)) throw new ArgumentException("예상질문 규칙 프롬프트가 필요합니다.");

            var limitedCount = Math.Clamp(count, 1, 5);
            var titleText = string.IsNullOrWhiteSpace(title) ? "(제목 없음)" : title.Trim();
            var prompt = $@"다음 문서형 KB를 읽고, 사용자가 실제로 물어볼 법한 예상 질문을 {limitedCount}개 생성하세요.

【제목】
{titleText}

【내용】
{content.Trim()}

【규칙】
- 반드시 JSON 배열만 응답
- 다른 설명 문장 금지
- 한국어 사용
- 각 질문은 자연스러운 사용자 말투
- 문서에 근거한 질문만 생성
- 서로 의미가 겹치지 않게 작성
- 제목을 그대로 복사하지 말 것
- 질문 길이와 표현을 다양하게 구성

예시 형식: JSON 문자열 배열";

            var responseContent = await RequestCompletionAsync(systemPrompt, rulesPrompt, prompt, 0.5, 300, "유사 질문 생성 실패");
            return ParseTextList(responseContent, limitedCount, titleText);
        }

        public async Task<List<string>> GenerateKeywordsAsync(string content, List<string>? additionalContent, int count, string systemPrompt, string rulesPrompt, string source = "question")
        {
            if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("콘텐츠가 필요합니다.");

            var limitedCount = Math.Clamp(count, 3, 20);
            var mainContent = content.Trim();
            var additional = (additionalContent ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToList();

            var prompt = string.Equals(source, "answer", StringComparison.OrdinalIgnoreCase)
                ? GenerateTopicKeywordPrompt(mainContent, limitedCount)
                : GenerateSearchKeywordPrompt(mainContent, additional, limitedCount);

            var responseContent = await RequestCompletionAsync(systemPrompt, rulesPrompt, prompt, 0.3, 250, "키워드 생성 실패");
            return ParseTextList(responseContent, limitedCount);
        }

        public async Task<string> RefineSolutionAsync(string title, string content, string systemPrompt, string rulesPrompt)
        {
            if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("내용이 필요합니다.");

            var titleText = string.IsNullOrWhiteSpace(title) ? "(없음)" : title.Trim();
            var prompt = $@"아래 KB 답변 초안을 가독성 높은 최종 답변으로 정리하세요.
제목은 참고용으로만 사용하고 출력에 포함하지 마세요. 본문 내용만 출력합니다.

【제목 (참고용, 출력 제외)】
{titleText}

【내용 초안】
{content.Trim()}";

            var responseContent = await RequestCompletionAsync(systemPrompt, rulesPrompt, prompt, 0.2, 700, "답변 정리 실패");
            if (string.IsNullOrWhiteSpace(responseContent)) throw new Exception("답변 정리 결과가 비어 있습니다.");
            return responseContent.Trim();
        }

        private async Task<string> RequestCompletionAsync(string systemPrompt, string rulesPrompt, string userPrompt, double temperature, int maxTokens, string errorMessage)
        {
            var request = new
            {
                model = _chatModel,
                messages = new[]
                {
                    new { role = "system", content = $"{systemPrompt}\n\n{rulesPrompt}" },
                    new { role = "user", content = userPrompt }
                },
                temperature,
                max_tokens = maxTokens
            };

            var response = await _httpClient.PostAsJsonAsync(_chatCompletionsEndpoint, request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("{ErrorMessage}: {Error}", errorMessage, error);
                throw new Exception(errorMessage);
            }

            var jsonString = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(jsonString).RootElement;
            return json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        }

        private static string GenerateSearchKeywordPrompt(string mainContent, List<string> additionalContent, int limitedCount)
        {
            var additionalText = additionalContent.Count == 0 ? "없음" : string.Join("\n", additionalContent.Select((x, i) => $"{i + 1}. {x}"));
            return $@"아래 KB 문서 데이터를 기준으로 검색 최적화 키워드를 {limitedCount}개 생성하세요.

【주요 텍스트】
{mainContent}

【보조 텍스트】
{additionalText}

【추가 규칙】
- 반드시 JSON 배열만 응답
- 각 키워드는 짧은 명사형/핵심 구로 작성
- 중복/유사 표현은 하나로 통합
- 문서 내용과 직접 무관한 단어는 제외
- count에 맞는 개수로 생성
- 사용자가 실제로 검색할 법한 표현 우선";
        }

        private static string GenerateTopicKeywordPrompt(string solution, int limitedCount)
        {
            return $@"아래 KB 답변 내용을 기준으로 주제/도메인 키워드를 {limitedCount}개 생성하세요.

【답변】
{solution}

【추가 규칙】
- 반드시 JSON 배열만 응답
- 각 키워드는 도메인 용어 또는 주제 중심
- 중복/유사 표현은 하나로 통합
- 답변의 핵심 개념과 카테고리 반영
- count에 맞는 개수로 생성
- 다른 KB와의 관련도 연결에 도움이 되는 키워드 우선";
        }

        private static List<string> ParseTextList(string? response, int count, string? representativeQuestion = null)
        {
            try
            {
                var raw = response?.Trim();
                if (string.IsNullOrWhiteSpace(raw)) throw new Exception("응답이 비어 있습니다.");

                var jsonStart = raw.IndexOf('[');
                var jsonEnd = raw.LastIndexOf(']');
                if (jsonStart < 0 || jsonEnd <= jsonStart) throw new Exception("JSON 배열 형식을 찾을 수 없습니다.");

                var jsonArray = raw.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var items = JsonSerializer.Deserialize<List<string>>(jsonArray) ?? new List<string>();
                var rep = representativeQuestion?.Trim();

                return items
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Where(x => string.IsNullOrWhiteSpace(rep) || !string.Equals(x, rep, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(count)
                    .ToList();
            }
            catch (Exception ex)
            {
                throw new Exception($"JSON 배열 파싱 실패: {ex.Message}\n응답: {response}");
            }
        }
    }
}