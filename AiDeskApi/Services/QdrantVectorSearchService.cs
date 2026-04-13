using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AiDeskApi.Data;
using AiDeskApi.Models;

namespace AiDeskApi.Services
{
    public interface IVectorSearchService
    {
        Task EnsureCollectionAsync(int vectorSize, CancellationToken cancellationToken = default);
        Task UpsertKnowledgeBaseAsync(KnowledgeBase kb, CancellationToken cancellationToken = default);
        Task DeleteKnowledgeBaseAsync(int kbId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<VectorSearchHit>> SearchAsync(float[] queryVector, string role, string platform, int topK, CancellationToken cancellationToken = default);
        Task SyncAllKnowledgeBasesAsync(CancellationToken cancellationToken = default);
    }

    public class VectorSearchHit
    {
        public int KbId { get; set; }
        public float Score { get; set; }
        public string MatchedQuestion { get; set; } = string.Empty;
        public bool IsSimilarQuestion { get; set; }
    }

    internal class QdrantPoint
    {
        public string id { get; set; } = string.Empty;
        public float[] vector { get; set; } = Array.Empty<float>();
        public object payload { get; set; } = new { };
    }

    public class QdrantVectorSearchService : IVectorSearchService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly AiDeskContext _context;
        private readonly ILogger<QdrantVectorSearchService> _logger;
        private readonly string _collectionName;
        private readonly bool _enabled;
        private bool _collectionEnsured;

        public QdrantVectorSearchService(
            HttpClient httpClient,
            IConfiguration configuration,
            AiDeskContext context,
            ILogger<QdrantVectorSearchService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _context = context;
            _logger = logger;

            _enabled = _configuration.GetValue<bool?>("Qdrant:Enabled") ?? true;
            _collectionName = _configuration["Qdrant:CollectionName"] ?? "aidesk_kb";

            var baseUrl = _configuration["Qdrant:Url"] ?? "http://localhost:6333";
            _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/'));
        }

        public async Task EnsureCollectionAsync(int vectorSize, CancellationToken cancellationToken = default)
        {
            if (!_enabled || _collectionEnsured || vectorSize <= 0)
            {
                return;
            }

            var body = new
            {
                vectors = new
                {
                    size = vectorSize,
                    distance = "Cosine"
                }
            };

            var response = await _httpClient.PutAsJsonAsync($"/collections/{_collectionName}", body, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var message = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Qdrant 컬렉션 생성 실패: {message}");
            }

            _collectionEnsured = true;
        }

        public async Task UpsertKnowledgeBaseAsync(KnowledgeBase kb, CancellationToken cancellationToken = default)
        {
            if (!_enabled) return;

            var points = BuildPoints(kb).ToList();
            if (points.Count == 0) return;

            await EnsureCollectionAsync(points[0].vector.Length, cancellationToken);

            var body = new { points };
            var response = await _httpClient.PutAsJsonAsync($"/collections/{_collectionName}/points?wait=true", body, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var message = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Qdrant upsert 실패: {message}");
            }
        }

        public async Task DeleteKnowledgeBaseAsync(int kbId, CancellationToken cancellationToken = default)
        {
            if (!_enabled || kbId <= 0) return;

            var body = new
            {
                filter = new
                {
                    must = new object[]
                    {
                        new
                        {
                            key = "kbId",
                            match = new { value = kbId }
                        }
                    }
                }
            };

            var response = await _httpClient.PostAsJsonAsync($"/collections/{_collectionName}/points/delete?wait=true", body, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var message = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Qdrant delete 실패: {message}");
            }
        }

        public async Task<IReadOnlyList<VectorSearchHit>> SearchAsync(float[] queryVector, string role, string platform, int topK, CancellationToken cancellationToken = default)
        {
            if (!_enabled || queryVector.Length == 0 || topK <= 0)
            {
                return Array.Empty<VectorSearchHit>();
            }

            await EnsureCollectionAsync(queryVector.Length, cancellationToken);

            var must = new List<object>();
            if (!string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
            {
                must.Add(new
                {
                    key = "visibility",
                    match = new { value = "user" }
                });
            }

            var normalizedPlatform = NormalizePlatform(platform);
            if (!string.Equals(normalizedPlatform, "전체 플랫폼", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalizedPlatform, "공통", StringComparison.OrdinalIgnoreCase))
            {
                must.Add(new
                {
                    key = "platforms",
                    match = new { any = new[] { "공통", normalizedPlatform } }
                });
            }

            var body = new
            {
                vector = queryVector,
                limit = topK,
                with_payload = true,
                filter = must.Count == 0 ? null : new { must = must.ToArray() }
            };

            var response = await _httpClient.PostAsJsonAsync($"/collections/{_collectionName}/points/search", body, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var message = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Qdrant search 실패: {message}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var result = new List<VectorSearchHit>();
            if (!json.RootElement.TryGetProperty("result", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("score", out var scoreElem) || scoreElem.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                if (!item.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!payload.TryGetProperty("kbId", out var kbIdElem) || kbIdElem.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                var matchedQuestion = payload.TryGetProperty("question", out var qElem) && qElem.ValueKind == JsonValueKind.String
                    ? qElem.GetString() ?? string.Empty
                    : string.Empty;

                var pointType = payload.TryGetProperty("type", out var tElem) && tElem.ValueKind == JsonValueKind.String
                    ? tElem.GetString() ?? "representative"
                    : "representative";

                result.Add(new VectorSearchHit
                {
                    KbId = kbIdElem.GetInt32(),
                    Score = (float)scoreElem.GetDouble(),
                    MatchedQuestion = matchedQuestion,
                    IsSimilarQuestion = string.Equals(pointType, "similar", StringComparison.OrdinalIgnoreCase)
                });
            }

            return result;
        }

        public async Task SyncAllKnowledgeBasesAsync(CancellationToken cancellationToken = default)
        {
            if (!_enabled) return;

            var kbs = await _context.KnowledgeBases
                .AsNoTracking()
                .Include(x => x.SimilarQuestions)
                .ToListAsync(cancellationToken);

            var firstVectorLength = kbs
                .Select(kb => ParseEmbedding(kb.ProblemEmbedding)?.Length ?? 0)
                .FirstOrDefault(x => x > 0);

            if (firstVectorLength > 0)
            {
                await EnsureCollectionAsync(firstVectorLength, cancellationToken);
            }

            foreach (var kb in kbs)
            {
                await UpsertKnowledgeBaseAsync(kb, cancellationToken);
            }

            _logger.LogInformation("Qdrant 동기화 완료: {Count} KB", kbs.Count);
        }

        private IEnumerable<QdrantPoint> BuildPoints(KnowledgeBase kb)
        {
            var platforms = ParsePlatforms(kb.Platform);
            var tags = string.IsNullOrWhiteSpace(kb.Tags)
                ? Array.Empty<string>()
                : kb.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

            var repEmbedding = ParseEmbedding(kb.ProblemEmbedding);
            if (repEmbedding != null && !string.IsNullOrWhiteSpace(kb.Problem))
            {
                yield return new QdrantPoint
                {
                    id = $"kb-{kb.Id}-rep",
                    vector = repEmbedding,
                    payload = new
                    {
                        kbId = kb.Id,
                        type = "representative",
                        question = kb.Problem,
                        visibility = kb.Visibility,
                        platforms,
                        tags,
                        updatedAt = kb.UpdatedAt
                    }
                };
            }

            foreach (var sq in kb.SimilarQuestions)
            {
                var sqEmbedding = ParseEmbedding(sq.QuestionEmbedding);
                if (sqEmbedding == null || string.IsNullOrWhiteSpace(sq.Question)) continue;

                yield return new QdrantPoint
                {
                    id = $"kb-{kb.Id}-sq-{sq.Id}",
                    vector = sqEmbedding,
                    payload = new
                    {
                        kbId = kb.Id,
                        similarQuestionId = sq.Id,
                        type = "similar",
                        question = sq.Question,
                        visibility = kb.Visibility,
                        platforms,
                        tags,
                        updatedAt = kb.UpdatedAt
                    }
                };
            }
        }

        private static string NormalizePlatform(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "공통";
            var trimmed = value.Trim();
            if (string.Equals(trimmed, "all", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "전체", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "전체 플랫폼", StringComparison.OrdinalIgnoreCase)) return "전체 플랫폼";
            if (string.Equals(trimmed, "common", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "공통", StringComparison.OrdinalIgnoreCase)) return "공통";
            return trimmed.ToLowerInvariant();
        }

        private static string[] ParsePlatforms(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new[] { "공통" };

            var parsed = raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizePlatform)
                .Where(x => !string.Equals(x, "전체 플랫폼", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToArray();

            return parsed.Length == 0 ? new[] { "공통" } : parsed;
        }

        private static float[]? ParseEmbedding(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    return root.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => (float)x.GetDouble()).ToArray();
                }

                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("embedding", out var embedding)
                    && embedding.ValueKind == JsonValueKind.Array)
                {
                    return embedding.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => (float)x.GetDouble()).ToArray();
                }
            }
            catch
            {
                // ignore parse error
            }

            return null;
        }
    }
}