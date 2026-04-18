using System.Net.Http.Json;
using System.Text.Json;
using AiDeskApi.Models;

namespace AiDeskApi.Services
{
    public interface IKnowledgeExtractorService
    {
        Task<ExtractedKnowledge> ExtractFromInteractionAsync(Interaction interaction);
        Task<List<string>> GenerateSimilarQuestionsAsync(string representativeQuestion, string solution, int count = 3);
        Task<List<string>> GenerateKeywordsAsync(string content, List<string>? additionalContent, int count, string systemPrompt, string rulesPrompt, string source = "question");
        Task<string> RefineSolutionAsync(string representativeQuestion, string solution, string systemPrompt, string rulesPrompt);
    }

    public class KnowledgeExtractorService : IKnowledgeExtractorService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<KnowledgeExtractorService> _logger;
        private readonly string _chatCompletionsEndpoint;
        private readonly string _chatModel;

        public KnowledgeExtractorService(HttpClient httpClient, IConfiguration configuration, ILogger<KnowledgeExtractorService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;

            var apiKey = configuration["OpenAI:ApiKey"];
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            _chatCompletionsEndpoint = configuration["OpenAI:Endpoint"] ?? "https://api.openai.com/v1/chat/completions";
            _chatModel = configuration["OpenAI:Model"] ?? "gpt-4o-mini";
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
                    model = _chatModel,
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
                    _chatCompletionsEndpoint,
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

        public async Task<List<string>> GenerateSimilarQuestionsAsync(string representativeQuestion, string solution, int count = 3)
        {
            if (string.IsNullOrWhiteSpace(solution))
                throw new ArgumentException("답변이 필요합니다.");

            try
            {
                var limitedCount = Math.Clamp(count, 1, 5);
                var rep = representativeQuestion?.Trim();
                var prompt = string.IsNullOrWhiteSpace(rep)
                    ? $@"다음 답변 내용을 읽고, 사용자가 실제로 물어볼 법한 유사 질문을 {limitedCount}개 생성하세요.

【답변】
{solution}

【규칙】
- 반드시 JSON 배열만 응답
- 다른 설명 문장 금지
- 한국어 사용
- 각 질문은 자연스러운 사용자 말투
- 답변 내용을 보고 사용자의 상황을 역으로 추론해 질문 만들기
- 서로 의미가 겹치지 않게 작성
- 질문 길이와 표현을 다양하게 구성
- 가능하면 아래 3가지 톤을 섞어서 생성
    1. 짧고 바로 묻는 질문
    2. 상황을 조금 설명하는 질문
    3. 사용자가 실제로 말할 법한 자연스러운 변형 질문

예시 형식: JSON 문자열 배열"
                    : $@"다음 KB를 읽고, 사용자가 실제로 물어볼 법한 유사 질문을 {limitedCount}개 생성하세요.

【대표질문】
{rep}

【답변】
{solution}

【규칙】
- 반드시 JSON 배열만 응답
- 다른 설명 문장 금지
- 한국어 사용
- 각 질문은 자연스러운 사용자 말투
- 의미만 비슷하고 문장은 겹치지 않게 작성
- 대표질문을 그대로 복사하지 말 것
- 질문 길이와 표현을 다양하게 구성
- 가능하면 아래 3가지 톤을 섞어서 생성
    1. 짧고 바로 묻는 질문
    2. 상황을 조금 설명하는 질문
    3. 사용자가 실제로 말할 법한 자연스러운 변형 질문

예시 형식: JSON 문자열 배열";

                var request = new
                {
                    model = _chatModel,
                    messages = new[]
                    {
                        new
                        {
                            role = "system",
                            content = "당신은 고객 문의 패턴을 분석해 유사 질문을 만들어주는 도우미입니다. 항상 JSON 문자열 배열만 응답하고, 질문 표현이 서로 겹치지 않게 다양하게 만드세요."
                        },
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.5,
                    max_tokens = 300
                };

                var response = await _httpClient.PostAsJsonAsync(
                    _chatCompletionsEndpoint,
                    request);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"유사 질문 생성 실패: {error}");
                    throw new Exception("유사 질문 생성 실패");
                }

                var jsonString = await response.Content.ReadAsStringAsync();
                var json = JsonDocument.Parse(jsonString).RootElement;
                var content = json
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                var parsed = ParseQuestionList(content, rep, limitedCount);
                return parsed;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ 유사 질문 생성 오류: {ex.Message}");
                throw;
            }
        }

        public async Task<List<string>> GenerateKeywordsAsync(
            string content,
            List<string>? additionalContent,
            int count,
            string systemPrompt,
            string rulesPrompt,
            string source = "question")
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new ArgumentException("콘텐츠가 필요합니다.");

            var limitedCount = Math.Clamp(count, 3, 20);
            var mainContent = content.Trim();
            var additional = (additionalContent ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToList();

            string prompt = source.ToLower() switch
            {
                "answer" => GenerateTopicKeywordPrompt(mainContent, additional, limitedCount),
                _ => GenerateSearchKeywordPrompt(mainContent, additional, limitedCount)
            };

            var request = new
            {
                model = _chatModel,
                messages = new[]
                {
                    new { role = "system", content = $"{systemPrompt}\n\n{rulesPrompt}" },
                    new { role = "user", content = prompt }
                },
                temperature = 0.3,
                max_tokens = 250
            };

            var response = await _httpClient.PostAsJsonAsync(_chatCompletionsEndpoint, request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError($"키워드 생성 실패: {error}");
                throw new Exception("키워드 생성 실패");
            }

            var jsonString = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(jsonString).RootElement;
            var responseContent = json
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return ParseTextList(responseContent, limitedCount);
        }

        private string GenerateSearchKeywordPrompt(string representativeQuestion, List<string> similarQuestions, int limitedCount)
        {
            var similarText = similarQuestions.Count == 0
                ? "없음"
                : string.Join("\n", similarQuestions.Select((x, i) => $"{i + 1}. {x}"));

            return $@"아래 KB 질문 데이터를 기준으로 검색 최적화 키워드를 {limitedCount}개 생성하세요.

【대표질문】
{representativeQuestion}

【유사질문】
{similarText}

【추가 규칙】
- 반드시 JSON 배열만 응답
- 각 키워드는 짧은 명사형/핵심 구로 작성
- 중복/유사 표현은 하나로 통합
- 대표질문과 직접 무관한 단어는 제외
- count에 맞는 개수로 생성
- 사용자가 실제로 검색할 법한 표현 우선";
        }

        private string GenerateTopicKeywordPrompt(string solution, List<string> _, int limitedCount)
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

        public async Task<string> RefineSolutionAsync(
            string representativeQuestion,
            string solution,
            string systemPrompt,
            string rulesPrompt)
        {
            if (string.IsNullOrWhiteSpace(solution))
                throw new ArgumentException("답변이 필요합니다.");

            var rep = representativeQuestion?.Trim();
            var prompt = $@"아래 KB 답변 초안을 가독성 높은 최종 답변으로 정리하세요.

【대표질문】
{(string.IsNullOrWhiteSpace(rep) ? "(없음)" : rep)}

【답변 초안】
{solution.Trim()}

【출력 형식】
- 한국어 마크다운 텍스트
- 필요 시 번호 목록 사용
- 원문 의미는 유지";

            var request = new
            {
                model = _chatModel,
                messages = new[]
                {
                    new { role = "system", content = $"{systemPrompt}\n\n{rulesPrompt}" },
                    new { role = "user", content = prompt }
                },
                temperature = 0.2,
                max_tokens = 700
            };

            var response = await _httpClient.PostAsJsonAsync(_chatCompletionsEndpoint, request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError($"답변 정리 실패: {error}");
                throw new Exception("답변 정리 실패");
            }

            var jsonString = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(jsonString).RootElement;
            var content = json
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
                throw new Exception("답변 정리 결과가 비어 있습니다.");

            return content.Trim();
        }

        private ExtractedKnowledge ParseJson(string? response)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(response))
                    throw new Exception("응답이 비어 있습니다.");

                var nonNullResponse = response.Trim();
                var jsonStart = nonNullResponse.IndexOf('{');
                var jsonEnd = nonNullResponse.LastIndexOf('}');

                if (jsonStart < 0 || jsonEnd <= jsonStart)
                    throw new Exception("JSON 형식을 찾을 수 없습니다.");

                var jsonString = nonNullResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
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

        private static List<string> ParseQuestionList(string? response, string? representativeQuestion, int count)
        {
            try
            {
                var raw = response?.Trim();
                if (string.IsNullOrWhiteSpace(raw))
                    throw new Exception("응답이 비어 있습니다.");

                var jsonStart = raw.IndexOf('[');
                var jsonEnd = raw.LastIndexOf(']');
                if (jsonStart < 0 || jsonEnd <= jsonStart)
                    throw new Exception("JSON 배열 형식을 찾을 수 없습니다.");

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
                throw new Exception($"유사 질문 JSON 파싱 실패: {ex.Message}\n응답: {response}");
            }
        }

        private static List<string> ParseTextList(string? response, int count)
        {
            try
            {
                var raw = response?.Trim();
                if (string.IsNullOrWhiteSpace(raw))
                    throw new Exception("응답이 비어 있습니다.");

                var jsonStart = raw.IndexOf('[');
                var jsonEnd = raw.LastIndexOf(']');
                if (jsonStart < 0 || jsonEnd <= jsonStart)
                    throw new Exception("JSON 배열 형식을 찾을 수 없습니다.");

                var jsonArray = raw.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var items = JsonSerializer.Deserialize<List<string>>(jsonArray) ?? new List<string>();

                return items
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
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

    public class ExtractedKnowledge
    {
        public string Problem { get; set; } = string.Empty;
        public string Solution { get; set; } = string.Empty;
    }

}
