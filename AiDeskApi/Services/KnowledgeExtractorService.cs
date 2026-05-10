using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiDeskApi.Services
{
    public interface IKnowledgeExtractorService
    {
        Task<List<string>> GenerateExpectedQuestionsAsync(string title, string content, int count, string systemPrompt, string rulesPrompt);
        Task<List<string>> GenerateKeywordsAsync(string content, List<string>? additionalContent, int count, string systemPrompt, string rulesPrompt, string source = "question");
        Task<List<string>> GenerateRecommendedTitlesAsync(string content, int count, string systemPrompt, string rulesPrompt);
        Task<string> GenerateRecommendedTitleAsync(string content, string systemPrompt, string rulesPrompt);
        Task<string> RefineSolutionAsync(string content, string systemPrompt, string rulesPrompt);
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

        public async Task<List<string>> GenerateRecommendedTitlesAsync(string content, int count, string systemPrompt, string rulesPrompt)
        {
            if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("내용이 필요합니다.");

            var limitedCount = Math.Clamp(count, 1, 5);

            var prompt = $@"【작업 입력】
작업 종류: KB 제목 추천
요청 개수: {limitedCount}

【내용】
{content.Trim()}

【출력 형식 제약】
- 제목만 출력한다.
- 40자 이내로 작성한다.
- 접두어/라벨(예: 제목:, 추천:)을 붙이지 않는다.
- 마크다운 기호를 사용하지 않는다.
- JSON 배열 또는 줄바꿈 목록 형식으로 출력한다.";

            var responseContent = await RequestCompletionAsync(systemPrompt, rulesPrompt, prompt, 0.2, 220, "제목 추천 실패");
            var titles = ParseTextList(responseContent, limitedCount)
                .Select(NormalizeRecommendedTitle)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(limitedCount)
                .ToList();

            if (titles.Count == 0)
            {
                var fallback = NormalizeRecommendedTitle(responseContent);
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    titles.Add(fallback);
                }
            }

            if (titles.Count == 0) throw new Exception("제목 추천 결과가 비어 있습니다.");

            return titles;
        }

        public async Task<string> GenerateRecommendedTitleAsync(string content, string systemPrompt, string rulesPrompt)
        {
            var titles = await GenerateRecommendedTitlesAsync(content, 1, systemPrompt, rulesPrompt);

            return titles[0];
        }

        public async Task<string> RefineSolutionAsync(string content, string systemPrompt, string rulesPrompt)
        {
            if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("내용이 필요합니다.");

            var prompt = $@"【작업 입력】
작업 종류: KB 답변 정리

【내용】
{content.Trim()}

【출력 형식 제약】
- 출력 첫 줄에 '주제:', '제목:' 같은 라벨 줄을 추가하지 않는다.
- 제목성 한 줄 요약을 맨 위에 따로 두지 말고, 본문 안내 내용부터 바로 시작한다.";

            var responseContent = await RequestCompletionAsync(systemPrompt, rulesPrompt, prompt, 0.2, 700, "답변 정리 실패");
            if (string.IsNullOrWhiteSpace(responseContent)) throw new Exception("답변 정리 결과가 비어 있습니다.");
            
            // 마크다운 형식 제거
            var cleaned = Regex.Replace(responseContent, @"\*\*", "");
            cleaned = RemoveLeadingTopicLine(cleaned);
            return cleaned.Trim();
        }

        private static string RemoveLeadingTopicLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var lines = value.Replace("\r\n", "\n").Split('\n').ToList();
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
            {
                lines.RemoveAt(0);
            }

            if (lines.Count == 0)
            {
                return string.Empty;
            }

            var firstLine = lines[0].Trim();
            if (Regex.IsMatch(firstLine, "^(주제|제목)\\s*[:：-].+", RegexOptions.IgnoreCase))
            {
                lines.RemoveAt(0);
                while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
                {
                    lines.RemoveAt(0);
                }
            }

            return string.Join("\n", lines);
        }

        private static string NormalizeRecommendedTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Replace("\r\n", "\n").Trim();
            var firstLine = normalized
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .FirstOrDefault() ?? string.Empty;

            firstLine = Regex.Replace(firstLine, "^(제목|추천|title)\\s*[:：-]\\s*", string.Empty, RegexOptions.IgnoreCase);
            firstLine = firstLine.Replace("**", string.Empty).Replace("`", string.Empty).Trim();

            if (firstLine.Length > 40)
            {
                firstLine = firstLine[..40].TrimEnd();
            }

            return firstLine;
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