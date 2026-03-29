using System.Text;
using System.Text.Json;

namespace CrmApi.Services
{
    public interface IGeminiService
    {
        Task<string> SummarizeConsultationAsync(string content, string type = "Call");
        Task<string> SummarizeAllConsultationsAsync(List<ConsultationData> consultations, CompanyData company);
    }

    public class GeminiService : IGeminiService
    {
        private static readonly string[] DefaultFallbackModels =
        {
            "gemini-2.0-flash",
            "gemini-1.5-flash"
        };

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GeminiService> _logger;
        private readonly ISummaryPromptTemplateService _promptTemplates;

        public GeminiService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<GeminiService> logger,
            ISummaryPromptTemplateService promptTemplates)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _promptTemplates = promptTemplates;
        }

        public async Task<string> SummarizeConsultationAsync(string content, string type = "Call")
        {
            try
            {
                var apiKey = _configuration["Gemini:ApiKey"];
                if (string.IsNullOrEmpty(apiKey))
                {
                    throw new InvalidOperationException("Gemini API 키가 설정되지 않았습니다.");
                }

                if (string.IsNullOrWhiteSpace(content))
                {
                    throw new ArgumentException("상담 내용을 입력해 주세요.");
                }

                var prompt = _promptTemplates.SingleConsultationTemplate
                    .Replace("{type}", type)
                    .Replace("{content}", content);

                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new { text = prompt }
                            }
                        }
                    }
                };
                return await GenerateWithFallbackModelsAsync(requestBody, apiKey, "정리 내용을 가져올 수 없습니다.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"상담 정리 오류: {ex.Message}");
                throw;
            }
        }

        public async Task<string> SummarizeAllConsultationsAsync(List<ConsultationData> consultations, CompanyData company)
        {
            try
            {
                var apiKey = _configuration["Gemini:ApiKey"];
                if (string.IsNullOrEmpty(apiKey))
                {
                    throw new InvalidOperationException("Gemini API 키가 설정되지 않았습니다.");
                }

                if (consultations == null || consultations.Count == 0)
                {
                    throw new ArgumentException("상담 이력이 없습니다.");
                }

                // 날짜 순서대로 정렬
                var sortedConsultations = consultations
                    .OrderBy(c => c.CreatedAt)
                    .ToList();

                var consultationText = string.Join("\n\n---\n\n",
                    sortedConsultations.Select((c, index) =>
                        $"[{index + 1}] {c.Type} ({Convert.ToDateTime(c.CreatedAt):yyyy-MM-dd})\n{c.Content}"
                    )
                );

                var prompt = _promptTemplates.AllConsultationsTemplate
                    .Replace("{companyName}", company.Name)
                    .Replace("{consultationText}", consultationText);

                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new { text = prompt }
                            }
                        }
                    }
                };
                return await GenerateWithFallbackModelsAsync(requestBody, apiKey, "요약 내용을 가져올 수 없습니다.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"전체 상담 요약 오류: {ex.Message}");
                throw;
            }
        }

        private async Task<string> GenerateWithFallbackModelsAsync(object requestBody, string apiKey, string emptyFallbackMessage)
        {
            var configuredModel = _configuration["Gemini:Model"];
            var modelCandidates = string.IsNullOrWhiteSpace(configuredModel)
                ? DefaultFallbackModels
                : new[] { configuredModel }.Concat(DefaultFallbackModels).Distinct().ToArray();

            foreach (var model in modelCandidates)
            {
                var response = await SendGenerateContentRequestAsync(model, requestBody, apiKey);
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var parsed = TryExtractText(responseContent);
                    return parsed ?? emptyFallbackMessage;
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Gemini API 모델 {Model} 호출 실패: {StatusCode} - {Body}", model, response.StatusCode, errorContent);

                if ((int)response.StatusCode != 404)
                {
                    throw new HttpRequestException($"Gemini API 오류: {response.StatusCode} - {errorContent}");
                }
            }

            throw new HttpRequestException("Gemini API 오류: NotFound - 사용 가능한 모델을 찾지 못했습니다. Gemini:Model 설정을 확인하세요.");
        }

        private async Task<HttpResponseMessage> SendGenerateContentRequestAsync(string model, object requestBody, string apiKey)
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            return await _httpClient.PostAsync(url, jsonContent);
        }

        private static string? TryExtractText(string responseContent)
        {
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                return null;
            }

            var firstCandidate = candidates[0];
            if (!firstCandidate.TryGetProperty("content", out var contentProp) ||
                !contentProp.TryGetProperty("parts", out var parts) ||
                parts.GetArrayLength() == 0)
            {
                return null;
            }

            return parts[0].TryGetProperty("text", out var textProp) ? textProp.GetString() : null;
        }
    }

    // DTO 클래스들
    public class ConsultationData
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string? Outcome { get; set; }
        public DateTime? ScheduledDate { get; set; }
        public bool IsCompleted { get; set; }
    }

    public class CompanyData
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public string? Company { get; set; }
        public string? Position { get; set; }
    }
}
