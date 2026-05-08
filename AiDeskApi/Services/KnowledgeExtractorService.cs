using System.Net.Http.Json;
using System.Text.Json;

namespace AiDeskApi.Services
{
    public interface IKnowledgeExtractorService
    {
        Task<List<string>> GenerateExpectedQuestionsAsync(string title, string content, int count, string systemPrompt, string rulesPrompt);
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

        public async Task<List<string>> GenerateExpectedQuestionsAsync(string title, string content, int count, string systemPrompt, string rulesPrompt)
        {
            if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("내용이 필요합니다.");
            if (string.IsNullOrWhiteSpace(systemPrompt)) throw new ArgumentException("예상질문 시스템 프롬프트가 필요합니다.");
            if (string.IsNullOrWhiteSpace(rulesPrompt)) throw new ArgumentException("예상질문 규칙 프롬프트가 필요합니다.");

            var limitedCount = Math.Clamp(count, 1, 5);
            var titleText = string.IsNullOrWhiteSpace(title) ? "(제목 없음)" : title.Trim();
            var prompt = $@"【작업 입력】
작업 종류: 예상 질문 생성
요청 개수: {limitedCount}

【제목】
{titleText}

【내용】
{content.Trim()}";

            var responseContent = await RequestCompletionAsync(systemPrompt, rulesPrompt, prompt, 0.5, 300, "예상 질문 생성 실패");
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
            var prompt = $@"【작업 입력】
작업 종류: KB 답변 정리

【제목】
{titleText}

【내용】
{content.Trim()}";

            var responseContent = await RequestCompletionAsync(systemPrompt, rulesPrompt, prompt, 0.2, 700, "답변 정리 실패");
            if (string.IsNullOrWhiteSpace(responseContent)) throw new Exception("답변 정리 결과가 비어 있습니다.");
            
            // 마크다운 형식 제거
            var cleaned = System.Text.RegularExpressions.Regex.Replace(responseContent, @"\*\*", "");
            return cleaned.Trim();
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
            return $@"【작업 입력】
작업 종류: 검색 키워드 생성
요청 개수: {limitedCount}

【주요 텍스트】
{mainContent}

【보조 텍스트】
{additionalText}";
        }

        private static string GenerateTopicKeywordPrompt(string solution, int limitedCount)
        {
            return $@"【작업 입력】
작업 종류: 주제 키워드 생성
요청 개수: {limitedCount}

【답변】
{solution}";
        }

        private static List<string> ParseTextList(string? response, int count, string? representativeQuestion = null)
        {
            try
            {
                var raw = response?.Trim();
                if (string.IsNullOrWhiteSpace(raw)) throw new Exception("응답이 비어 있습니다.");

                var rep = representativeQuestion?.Trim();
                var items = TryParseJsonArray(raw);
                if (items.Count == 0)
                {
                    items = ParseLineSeparatedList(raw);
                }

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
                throw new Exception($"목록 응답 파싱 실패: {ex.Message}\n응답: {response}");
            }
        }

        private static List<string> TryParseJsonArray(string raw)
        {
            var jsonStart = raw.IndexOf('[');
            var jsonEnd = raw.LastIndexOf(']');
            if (jsonStart < 0 || jsonEnd <= jsonStart)
            {
                return new List<string>();
            }

            var jsonArray = raw.Substring(jsonStart, jsonEnd - jsonStart + 1);
            return JsonSerializer.Deserialize<List<string>>(jsonArray) ?? new List<string>();
        }

        private static List<string> ParseLineSeparatedList(string raw)
        {
            return raw
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line =>
                {
                    var cleaned = line.TrimStart('-', '*', '•', ' ', '\t');
                    var dotIndex = cleaned.IndexOf('.');
                    if (dotIndex > 0 && int.TryParse(cleaned[..dotIndex], out _))
                    {
                        cleaned = cleaned[(dotIndex + 1)..].Trim();
                    }

                    return cleaned.Trim().Trim('"');
                })
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
        }
    }
}