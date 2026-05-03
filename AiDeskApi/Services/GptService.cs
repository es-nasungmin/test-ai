using System.Text;
using System.Text.Json;

namespace AiDeskApi.Services
{
    public interface IGptService
    {
        Task<string> SummarizeConsultationAsync(string content, string type = "Call");
        Task<string> SummarizeAllConsultationsAsync(List<ConsultationData> consultations, CompanyData company);
    }

    public class GptService : IGptService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GptService> _logger;
        private readonly ISummaryPromptTemplateService _promptTemplates;

        public GptService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<GptService> logger,
            ISummaryPromptTemplateService promptTemplates)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _promptTemplates = promptTemplates;
        }

        public async Task<string> SummarizeConsultationAsync(string content, string type = "Call")
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new ArgumentException("상담 내용을 입력해 주세요.");
            }

            var prompt = _promptTemplates.SingleConsultationTemplate
                .Replace("{type}", type)
                .Replace("{content}", content);

            return await GenerateSummaryAsync(prompt, "정리 내용을 가져올 수 없습니다.");
        }

        public async Task<string> SummarizeAllConsultationsAsync(List<ConsultationData> consultations, CompanyData company)
        {
            if (consultations == null || consultations.Count == 0)
            {
                throw new ArgumentException("상담 이력이 없습니다.");
            }

            var sortedConsultations = consultations
                .OrderBy(c => c.CreatedAt)
                .ToList();

            var consultationText = string.Join("\n\n---\n\n",
                sortedConsultations.Select((c, index) =>
                    $"[{index + 1}] {c.Type} ({c.CreatedAt:yyyy-MM-dd})\n{c.Content}"
                )
            );

            var prompt = _promptTemplates.AllConsultationsTemplate
                .Replace("{companyName}", company.Name)
                .Replace("{consultationText}", consultationText);

            return await GenerateSummaryAsync(prompt, "요약 내용을 가져올 수 없습니다.");
        }

        private async Task<string> GenerateSummaryAsync(string prompt, string emptyFallbackMessage)
        {
            var apiKey = _configuration["OpenAI:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("OpenAI API 키가 설정되지 않았습니다.");
            }

            var model = _configuration["OpenAI:Model"];
            if (string.IsNullOrWhiteSpace(model))
            {
                model = "gpt-4o-mini";
            }

            var endpoint = _configuration["OpenAI:Endpoint"];
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                endpoint = "https://api.openai.com/v1/chat/completions";
            }

            var requestBody = new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = "You are a helpful CRM assistant that writes concise Korean summaries." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.2
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("OpenAI API 오류: {StatusCode} - {Body}", response.StatusCode, body);
                throw new HttpRequestException($"OpenAI API 오류: {response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                return emptyFallbackMessage;
            }

            var message = choices[0].GetProperty("message");
            if (!message.TryGetProperty("content", out var contentProp))
            {
                return emptyFallbackMessage;
            }

            return contentProp.GetString() ?? emptyFallbackMessage;
        }
    }

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