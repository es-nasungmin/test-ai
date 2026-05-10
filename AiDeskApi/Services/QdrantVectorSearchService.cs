using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using AiDeskApi.Data;
using AiDeskApi.Models;

namespace AiDeskApi.Services
{
    /// <summary>
    /// Qdrant에 KB 벡터를 적재하고 검색하는 벡터 저장소 서비스입니다.
    /// </summary>
    public interface IVectorSearchService
    {
        /// <summary>검색에 사용할 Qdrant 컬렉션이 존재하는지 확인하고 없으면 생성합니다.</summary>
        Task EnsureCollectionAsync(int vectorSize, CancellationToken cancellationToken = default);
        /// <summary>특정 KB의 본문/예상질문 벡터를 Qdrant에 적재하거나 갱신합니다.</summary>
        Task UpsertKnowledgeBaseAsync(KnowledgeBase kb, CancellationToken cancellationToken = default);
        /// <summary>특정 KB에 연결된 모든 벡터 포인트를 삭제합니다.</summary>
        Task DeleteKnowledgeBaseAsync(int kbId, CancellationToken cancellationToken = default);
        /// <summary>특정 KB의 예상질문 벡터 포인트만 삭제합니다.</summary>
        Task DeleteExpectedQuestionPointsAsync(int kbId, IEnumerable<string> questions, CancellationToken cancellationToken = default);
        /// <summary>질문 벡터를 이용해 KB 후보를 검색합니다.</summary>
        Task<IReadOnlyList<VectorSearchHit>> SearchAsync(float[] queryVector, string role, string platform, int topK, CancellationToken cancellationToken = default);
        /// <summary>DB에 있는 모든 KB를 Qdrant에 다시 동기화합니다.</summary>
        Task SyncAllKnowledgeBasesAsync(CancellationToken cancellationToken = default);
        /// <summary>Qdrant 컬렉션을 비우고 전체 KB를 새로 적재합니다.</summary>
        Task RebuildAllKnowledgeBasesAsync(CancellationToken cancellationToken = default);
    }

    // 벡터 검색 결과를 나타내는 모델입니다. KB ID, 유사도 점수, 매칭된 질문, 예상질문 여부를 포함합니다.
    public class VectorSearchHit
    {
        public int KbId { get; set; }
        public float Score { get; set; }
        public string MatchedEvidenceText { get; set; } = string.Empty;
        public bool IsExpectedQuestion { get; set; }
    }

    // KB 문서와 예상질문을 Qdrant 포인트로 변환할 때 사용하는 내부 모델
    internal class QdrantPoint
    {
        public string id { get; set; } = string.Empty;
        public float[] vector { get; set; } = Array.Empty<float>();
        public object payload { get; set; } = new { };
    }

    // KB 본문/예상질문 벡터를 Qdrant 컬렉션에 동기화하고 유사 항목을 조회하는 구현체
    public class QdrantVectorSearchService : IVectorSearchService
    {
        // Qdrant REST API 호출 전용 HttpClient (DI에서 typed/named client로 주입)
        private readonly HttpClient _httpClient;
        // appsettings*.json / 환경변수의 Qdrant:* 키를 읽기 위한 설정 루트
        private readonly IConfiguration _configuration;
        // KB/ExpectedQuestions를 DB에서 읽어 전체 동기화할 때 사용하는 EF Core DbContext
        private readonly AiDeskContext _context;
        // 본문/질문 텍스트를 임베딩 벡터로 변환하는 서비스
        private readonly IEmbeddingService _embeddingService;
        private readonly ILogger<QdrantVectorSearchService> _logger;
        // Qdrant 컬렉션 이름 (Qdrant:CollectionName)
        private readonly string _collectionName;
        // 벡터 기능 on/off 플래그 (Qdrant:Enabled)
        private readonly bool _enabled;
        // 앱 인스턴스 생명주기 내 컬렉션 생성 1회 보장 플래그
        private bool _collectionEnsured;

        public QdrantVectorSearchService(
            HttpClient httpClient,
            IConfiguration configuration,
            AiDeskContext context,
            IEmbeddingService embeddingService,
            ILogger<QdrantVectorSearchService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _context = context;
            _embeddingService = embeddingService;
            _logger = logger;

            // 설정 키 참조: appsettings.json, appsettings.{Environment}.json, 환경변수 순으로 오버라이드 가능
            _enabled = _configuration.GetValue<bool?>("Qdrant:Enabled") ?? true;
            _collectionName = _configuration["Qdrant:CollectionName"] ?? "aidesk_kb";

            // Qdrant 엔드포인트 (Qdrant:Url). 미설정 시 로컬 기본값 사용
            var baseUrl = _configuration["Qdrant:Url"] ?? "http://localhost:6333";
            _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/'));
        }

        /// <summary>
        /// Qdrant 컬렉션이 존재하지 않을 때 최초 1회 생성합니다.
        /// <para>
        /// - 이미 존재하는 경우(HTTP 409) 에러 없이 통과하며, 이후 요청은 인스턴스 플래그로 스킵합니다.
        /// - 거리 함수는 코사인 유사도(Cosine)를 사용합니다. 임베딩 벡터가 정규화되어 있으므로
        ///   내적(Dot)과 동일하지만 명시적으로 Cosine을 지정해 의도를 드러냅니다.
        /// - <paramref name="vectorSize"/>는 실제 임베딩 결과 길이에서 자동으로 추론됩니다.
        ///   (OpenAI text-embedding-3-small → 1536, text-embedding-3-large → 3072)
        /// </para>
        /// </summary>
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
                // 이미 존재하는 컬렉션이면 409 Conflict 반환 → 정상으로 처리
                if ((int)response.StatusCode == 409)
                {
                    _collectionEnsured = true;
                    return;
                }

                var message = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Qdrant 컬렉션 생성 실패: {message}");
            }

            _collectionEnsured = true;
        }

        /// <summary>
        /// KB 1건을 Qdrant에 upsert(삽입 또는 갱신)합니다.
        /// <para>
        /// - <see cref="BuildPointsAsync"/>로 document/expected 포인트를 생성한 뒤 한 번에 전송합니다.
        /// - <c>?wait=true</c> 쿼리스트링을 통해 Qdrant가 인덱싱을 완료한 후 응답하도록 강제합니다.
        ///   이를 통해 upsert 직후 검색 결과에 즉시 반영되는 것을 보장합니다.
        /// - 포인트 ID는 결정적(deterministic) UUID(MD5 기반)이므로, 동일 KB를 재전송하면
        ///   자동으로 기존 포인트를 덮어씁니다.
        /// </para>
        /// </summary>
        public async Task UpsertKnowledgeBaseAsync(KnowledgeBase kb, CancellationToken cancellationToken = default)
        {
            if (!_enabled) return;

            // document + expected 포인트 목록 생성 (임베딩 즉시 생성 포함)
            var points = await BuildPointsAsync(kb, cancellationToken);
            if (points.Count == 0) return;

            // 첫 번째 포인트의 벡터 크기로 컬렉션 존재 여부를 보장
            await EnsureCollectionAsync(points[0].vector.Length, cancellationToken);

            var body = new { points };
            var response = await _httpClient.PutAsJsonAsync($"/collections/{_collectionName}/points?wait=true", body, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var message = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Qdrant upsert 실패: {message}");
            }
        }

        /// <summary>
        /// 특정 KB에 속한 모든 Qdrant 포인트를 삭제합니다.
        /// <para>
        /// - 하나의 KB는 document 포인트 1개 + expected 포인트 N개를 가질 수 있습니다.
        ///   포인트 ID를 일일이 열거하지 않고, payload의 <c>kbId</c> 필드를 기준으로
        ///   필터 삭제(filter delete)를 사용해 한 번에 제거합니다.
        /// - <c>?wait=true</c>로 인덱스 반영 완료까지 대기합니다.
        /// </para>
        /// </summary>
        public async Task DeleteKnowledgeBaseAsync(int kbId, CancellationToken cancellationToken = default)
        {
            if (!_enabled || kbId <= 0) return;

            // kbId 필드 기준 필터 삭제: 해당 KB의 document/expected 포인트 전부 제거
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

        /// <summary>
        /// 특정 KB의 예상질문 포인트 일부만 삭제합니다.
        /// <para>
        /// KB 수정 시 현재 상태와 달라진 오래된 예상질문만 정리할 때 사용합니다.
        /// </para>
        /// </summary>
        public async Task DeleteExpectedQuestionPointsAsync(int kbId, IEnumerable<string> questions, CancellationToken cancellationToken = default)
        {
            if (!_enabled || kbId <= 0 || questions == null) return;

            var pointIds = questions
                .Where(question => !string.IsNullOrWhiteSpace(question))
                .Select(question => CreatePointId($"kb-{kbId}-expected-{question.Trim()}"))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (pointIds.Length == 0)
            {
                return;
            }

            var body = new
            {
                points = pointIds
            };

            var response = await _httpClient.PostAsJsonAsync($"/collections/{_collectionName}/points/delete?wait=true", body, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var message = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Qdrant delete 실패: {message}");
            }
        }

        /// <summary>
        /// 쿼리 벡터와 코사인 유사도가 높은 Qdrant 포인트를 검색합니다.
        /// <para>
        /// <b>필터 정책</b>
        /// <list type="bullet">
        ///   <item><description>
        ///     <b>visibility 필터</b>: admin이 아닌 일반 사용자는 <c>visibility == "user"</c>
        ///     포인트만 조회합니다. 관리자는 모든 KB를 검색합니다.
        ///   </description></item>
        ///   <item><description>
        ///     <b>platform 필터</b>: "전체 플랫폼" 요청은 필터 없이 전체 조회합니다.
        ///     "공통" 요청은 공통 포인트만 조회하고, 특정 플랫폼 요청은 "공통" + 해당 플랫폼 포인트를
        ///     함께 허용합니다(<c>any</c> 조건).
        ///   </description></item>
        /// </list>
        /// </para>
        /// <para>
        /// 반환된 <see cref="VectorSearchHit"/>의 <c>IsExpectedQuestion</c> 플래그로
        /// document 포인트(본문 매칭)와 expected 포인트(예상질문 매칭)를 구분합니다.
        /// 이 구분은 RAG 파이프라인에서 rerank 전략을 결정하는 데 사용됩니다.
        /// </para>
        /// </summary>
        public async Task<IReadOnlyList<VectorSearchHit>> SearchAsync(float[] queryVector, string role, string platform, int topK, CancellationToken cancellationToken = default)
        {
            if (!_enabled || queryVector.Length == 0 || topK <= 0)
            {
                return Array.Empty<VectorSearchHit>();
            }

            await EnsureCollectionAsync(queryVector.Length, cancellationToken);

            var must = new List<object>();

            // admin이 아닌 사용자는 공개(user) 포인트만 검색 허용
            if (!string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
            {
                must.Add(new
                {
                    key = "visibility",
                    match = new { value = "user" }
                });
            }

            var normalizedPlatform = NormalizePlatform(platform);
            // "전체 플랫폼" 요청: 필터 없이 전체 대상 검색
            // "공통" 요청: 공통 포인트만 검색
            // 특정 플랫폼 요청: 해당 플랫폼 + 공통 포인트 검색
            if (string.Equals(normalizedPlatform, "공통", StringComparison.OrdinalIgnoreCase))
            {
                must.Add(new
                {
                    key = "platforms",
                    match = new { any = new[] { "공통" } }
                });
            }
            else if (!string.Equals(normalizedPlatform, "전체 플랫폼", StringComparison.OrdinalIgnoreCase))
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

                var matchedEvidenceText = payload.TryGetProperty("question", out var qElem) && qElem.ValueKind == JsonValueKind.String
                    ? qElem.GetString() ?? string.Empty
                    : string.Empty;

                // document 포인트는 title 키를 사용하므로, question이 비어 있으면 title로 보완
                if (string.IsNullOrWhiteSpace(matchedEvidenceText)
                    && payload.TryGetProperty("title", out var titleElem)
                    && titleElem.ValueKind == JsonValueKind.String)
                {
                    matchedEvidenceText = titleElem.GetString() ?? string.Empty;
                }

                // type == "expected"이면 예상질문 포인트, 그 외("document")는 본문 포인트
                var pointType = payload.TryGetProperty("type", out var tElem) && tElem.ValueKind == JsonValueKind.String
                    ? tElem.GetString() ?? "document"
                    : "document";

                result.Add(new VectorSearchHit
                {
                    KbId = kbIdElem.GetInt32(),
                    Score = (float)scoreElem.GetDouble(),
                    MatchedEvidenceText = matchedEvidenceText,
                    // true = 예상질문 포인트에서 매칭됨 → RAG에서 높은 신뢰도로 처리
                    IsExpectedQuestion = string.Equals(pointType, "expected", StringComparison.OrdinalIgnoreCase)
                });
            }

            return result;
        }

        /// <summary>
        /// DB의 모든 KB를 Qdrant에 전량 동기화합니다.
        /// <para>
        /// - 서버 시작 시 또는 수동 동기화 엔드포인트를 통해 호출됩니다.
        /// - 포인트 ID가 결정적(deterministic)이므로 이미 존재하는 포인트는 자동 덮어씌웁니다.
        ///   삭제된 KB는 이 메서드로는 제거되지 않으며, <see cref="DeleteKnowledgeBaseAsync"/>를
        ///   명시적으로 호출해야 합니다.
        /// - 대량 KB가 있을 경우 순차 upsert로 인해 시간이 걸릴 수 있습니다.
        ///   운영 환경에서는 배치 upsert로 개선을 고려하세요.
        /// </para>
        /// </summary>
        public async Task SyncAllKnowledgeBasesAsync(CancellationToken cancellationToken = default)
        {
            if (!_enabled) return;

            var kbs = await _context.KnowledgeBases
                .AsNoTracking()
                .Include(x => x.ExpectedQuestions)
                .ToListAsync(cancellationToken);

            foreach (var kb in kbs)
            {
                await UpsertKnowledgeBaseAsync(kb, cancellationToken);
            }

            _logger.LogInformation("Qdrant 동기화 완료: {Count} KB", kbs.Count);
        }

        // 컬렉션을 완전히 초기화한 뒤 전체 KB를 새로 적재합니다. 대량 KB가 있을 때 사용하세요.
        public async Task RebuildAllKnowledgeBasesAsync(CancellationToken cancellationToken = default)
        {
            if (!_enabled) return;

            var response = await _httpClient.DeleteAsync($"/collections/{_collectionName}", cancellationToken);
            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                var message = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Qdrant 컬렉션 초기화 실패: {message}");
            }

            _collectionEnsured = false;
            await SyncAllKnowledgeBasesAsync(cancellationToken);
        }

        /// <summary>
        /// KB 1건으로부터 Qdrant에 저장할 포인트 목록을 생성합니다.
        /// <para>
        /// <b>포인트 종류</b>
        /// <list type="bullet">
        ///   <item><description>
        ///     <b>document 포인트 (type="document")</b>: KB의 제목과 본문을 합쳐
        ///     임베딩한 포인트입니다. "제목: …\n내용: …" 형식으로 결합해
        ///     의미적 맥락을 풍부하게 합니다.
        ///     payload에는 document 전용으로 <c>title</c>을 저장합니다.
        ///   </description></item>
        ///   <item><description>
        ///     <b>expected 포인트 (type="expected")</b>: 예상질문 각각을 독립적으로
        ///     임베딩한 포인트입니다. 사용자 질의와 예상질문이 의미적으로 유사할 때
        ///     높은 점수를 받도록 분리 저장합니다.
        ///     payload에는 expected 전용으로 <c>question</c>을 저장합니다.
        ///   </description></item>
        /// </list>
        /// </para>
        /// <para>
        /// 임베딩은 RDB에 저장하지 않고 이 메서드에서 즉시 생성합니다.
        /// Qdrant가 벡터 저장소 역할을 전담하므로 RDB 중복 저장 없이도
        /// 언제든 재동기화로 복원할 수 있습니다.
        /// </para>
        /// </summary>
        private async Task<List<QdrantPoint>> BuildPointsAsync(KnowledgeBase kb, CancellationToken cancellationToken)
        {
            var points = new List<QdrantPoint>();
            var platforms = ParsePlatforms(kb.Platform);
            var keywords = string.IsNullOrWhiteSpace(kb.Keywords)
                ? Array.Empty<string>()
                : kb.Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

            // 제목 + 본문 임베딩 → document 포인트 1개 생성
            var bodySource = BuildKbBodyEmbeddingSource(kb.Title, kb.Content);
            if (!string.IsNullOrWhiteSpace(bodySource))
            {
                var normalizedBodySource = EmbeddingTextNormalizer.NormalizeForEmbedding(bodySource);
                var bodyEmbedding = await _embeddingService.EmbedTextAsync(normalizedBodySource);
                if (bodyEmbedding.Length > 0)
                {
                    points.Add(new QdrantPoint
                    {
                        id = CreatePointId($"kb-{kb.Id}-doc"),
                        vector = bodyEmbedding,
                        payload = new
                        {
                            kbId = kb.Id,
                            type = "document", // 포인트 분류키: 검색 결과에서 본문 포인트로 판별
                            title = kb.Title,
                            content = kb.Content,
                            visibility = kb.Visibility, // 일반 사용자 검색에서 visibility="user"만 통과시키기 위한 필드
                            platforms,
                            keywords,
                            updatedAt = kb.UpdatedAt
                        }
                    });
                }
            }

            var expectedQuestions = (kb.ExpectedQuestions ?? new List<KnowledgeBaseExpectedQuestion>())
                .Where(x => !string.IsNullOrWhiteSpace(x.Question))
                .Select(x => new
                {
                    Question = x.Question.Trim()
                })
                .DistinctBy(x => x.Question, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 예상질문 각각 임베딩 → expected 포인트 N개 생성
            foreach (var item in expectedQuestions)
            {
                var normalizedQuestion = EmbeddingTextNormalizer.NormalizeForEmbedding(item.Question);
                var questionEmbedding = await _embeddingService.EmbedTextAsync(normalizedQuestion);
                if (questionEmbedding.Length == 0)
                {
                    continue;
                }

                points.Add(new QdrantPoint
                {
                    id = CreatePointId($"kb-{kb.Id}-expected-{item.Question}"),
                    vector = questionEmbedding,
                    payload = new
                    {
                        kbId = kb.Id,
                        type = "expected", // 포인트 분류키: 검색 결과에서 예상질문 포인트로 판별
                        question = item.Question,
                        visibility = kb.Visibility, // 일반 사용자 검색에서 visibility="user"만 통과시키기 위한 필드
                        platforms,
                        keywords,
                        updatedAt = kb.UpdatedAt
                    }
                });
            }

            return points;
        }

        private static string BuildKbBodyEmbeddingSource(string? title, string? content)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(title))
            {
                parts.Add($"제목: {title.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(content))
            {
                parts.Add($"내용: {content.Trim()}");
            }

            return string.Join("\n", parts);
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

        // 포인트 ID 생성: "kb-{kbId}-doc" 또는 "kb-{kbId}-expected-{question}" 형태의 문자열을 MD5 해시로 변환해 UUID 형식으로 반환
        private static string CreatePointId(string rawKey)
        {
            var hash = MD5.HashData(Encoding.UTF8.GetBytes(rawKey));
            return new Guid(hash).ToString();
        }
    }
}