using System.Net.Http.Json;
using System.Text.Json;

namespace AiDeskApi.Services
{
    /// <summary>
    /// 질문이나 문장을 OpenAI 임베딩 벡터로 변환하는 서비스입니다.
    /// </summary>
    public interface IEmbeddingService
    {
        /// <summary>입력 텍스트를 검색용 임베딩 벡터로 변환합니다.</summary>
        Task<float[]> EmbedTextAsync(string text);
    }

    // OpenAI embeddings API를 호출해 검색용 벡터를 만드는 구현체
    public class OpenAiEmbeddingService : IEmbeddingService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<OpenAiEmbeddingService> _logger;

        public OpenAiEmbeddingService(HttpClient httpClient, IConfiguration configuration, ILogger<OpenAiEmbeddingService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;

            var apiKey = configuration["OpenAI:ApiKey"];
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }

        public async Task<float[]> EmbedTextAsync(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                    throw new ArgumentException("텍스트가 비어있습니다.");

                var request = new
                {
                    model = "text-embedding-3-small",
                    input = text.Substring(0, Math.Min(8191, text.Length))  // 토큰 제한
                };

                var response = await _httpClient.PostAsJsonAsync(
                    "https://api.openai.com/v1/embeddings",
                    request);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("OpenAI API 오류: {Error}", error);
                    throw new Exception($"임베딩 실패: {response.StatusCode}");
                }

                var jsonString = await response.Content.ReadAsStringAsync();
                var json = JsonDocument.Parse(jsonString).RootElement;
                var embedding = json
                    .GetProperty("data")[0]
                    .GetProperty("embedding")
                    .EnumerateArray()
                    .Select(x => (float)x.GetDouble())
                    .ToArray();

                _logger.LogInformation("✓ 임베딩 생성 완료 ({Length} chars)", text.Length);
                return embedding;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "❌ 네트워크 오류: {Message}", ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 임베딩 오류: {Message}", ex.Message);
                throw;
            }
        }
    }
}
