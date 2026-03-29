using System.Net.Http.Json;
using System.Text.Json;
using CrmApi.Models;

namespace CrmApi.Services
{
    public interface IKnowledgeExtractorService
    {
        Task<ExtractedKnowledge> ExtractFromInteractionAsync(Interaction interaction);
    }

    public class KnowledgeExtractorService : IKnowledgeExtractorService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<KnowledgeExtractorService> _logger;

        public KnowledgeExtractorService(HttpClient httpClient, IConfiguration configuration, ILogger<KnowledgeExtractorService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;

            var apiKey = configuration["OpenAI:ApiKey"];
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }

        public async Task<ExtractedKnowledge> ExtractFromInteractionAsync(Interaction interaction)
        {
            try
            {
                _logger.LogInformation($"🔍 분석 시작: [{interaction.Type}]");

                var prompt = $@"다음 {interaction.Type} 상담을 분석하세요.

【상담 내용】
{interaction.Content}

【요구사항】
- 반드시 JSON 형식만 응답
- 다른 텍스트 금지
- 한국어 사용

【JSON 형식】
{{
  ""problem"": ""고객의 구체적 문제 (최대 200자)"",
  ""solution"": ""적용한 해결방법 또는 조치내용 (최대 500자)""
}}";

                var request = new
                {
                    model = "gpt-3.5-turbo",
                    messages = new[]
                    {
                        new
                        {
                            role = "system",
                            content = "당신은 상담 내용을 분석하여 문제와 해결방법을 추출하는 전문가입니다. 항상 JSON 형식만 응답하세요."
                        },
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.3,
                    max_tokens = 500
                };

                var response = await _httpClient.PostAsJsonAsync(
                    "https://api.openai.com/v1/chat/completions",
                    request);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"분석 실패: {error}");
                    throw new Exception("GPT 분석 실패");
                }

                var jsonString = await response.Content.ReadAsStringAsync();
                var json = JsonDocument.Parse(jsonString).RootElement;
                var content = json
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                var extracted = ParseJson(content);
                _logger.LogInformation($"✓ 분석 완료");

                return extracted;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ 분석 오류: {ex.Message}");
                throw;
            }
        }

        private ExtractedKnowledge ParseJson(string response)
        {
            try
            {
                var jsonStart = response?.IndexOf('{') ?? -1;
                var jsonEnd = response?.LastIndexOf('}') ?? -1;

                if (jsonStart < 0 || jsonEnd <= jsonStart)
                    throw new Exception("JSON 형식을 찾을 수 없습니다.");

                var jsonString = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var doc = JsonDocument.Parse(jsonString);

                return new ExtractedKnowledge
                {
                    Problem = doc.RootElement.GetProperty("problem").GetString() ?? "알 수 없음",
                    Solution = doc.RootElement.GetProperty("solution").GetString() ?? "알 수 없음"
                };
            }
            catch (Exception ex)
            {
                throw new Exception($"JSON 파싱 실패: {ex.Message}\n응답: {response}");
            }
        }
    }

    public class ExtractedKnowledge
    {
        public string Problem { get; set; } = string.Empty;
        public string Solution { get; set; } = string.Empty;
    }
}
