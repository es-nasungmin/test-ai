using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using CrmApi.Data;
using CrmApi.Models;

namespace CrmApi.Services
{
    public interface IRagService
    {
        // role: "admin" = 전체 KB 조회, "user" = visibility='common' + IsApproved=true만 조회
        Task<RagResponse> SearchAndGenerateAsync(string question, string role = "user");
    }

    // 질문 임베딩 -> 유사 KB 검색 -> 답변 생성까지 RAG 전체 흐름을 담당
    public class OpenAiRagService : IRagService
    {
        private readonly CrmContext _context;
        private readonly IEmbeddingService _embeddingService;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<OpenAiRagService> _logger;

        public OpenAiRagService(
            CrmContext context,
            IEmbeddingService embeddingService,
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<OpenAiRagService> logger)
        {
            _context = context;
            _embeddingService = embeddingService;
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;

            var apiKey = configuration["OpenAI:ApiKey"];
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }

        public async Task<RagResponse> SearchAndGenerateAsync(string question, string role = "user")
        {
            try
            {
                _logger.LogInformation($"📝 질문 받음 [{role}]: {question}");

                // 1. 질문을 벡터로 변환
                var questionEmbedding = await _embeddingService.EmbedTextAsync(question);

                // 2. role에 따라 KB 필터 적용
                //    admin: 모든 KB 조회
                //    user:  visibility='common' + IsApproved=true만 조회
                var query = _context.KnowledgeBases.AsQueryable();
                if (role != "admin")
                {
                    query = query.Where(x => x.Visibility == "common" && x.IsApproved);
                }

                var kbs = await query
                    .OrderByDescending(x => x.ViewCount)
                    .Take(100)
                    .ToListAsync();

                if (kbs.Count == 0)
                {
                    _logger.LogWarning("⚠️ KB 데이터 없음");
                    return new RagResponse
                    {
                        Answer = "유사한 상담내역이 없습니다. 담당자에게 문의해주세요.",
                        RelatedKBs = new List<KBSummary>()
                    };
                }

                // 3. 코사인 유사도 + 타입 가중치로 상위 3개 검색
                // 공식 KB(official) 우선 원칙: 동일/유사 점수라면 공식 KB가 앞서도록 보정
                var topResults = kbs
                    .Select(kb =>
                    {
                        if (string.IsNullOrEmpty(kb.ProblemEmbedding)) return default((KnowledgeBase, float)?);

                        try
                        {
                            var embedding = JsonSerializer.Deserialize<float[]>(kb.ProblemEmbedding);
                            if (embedding == null) return default((KnowledgeBase, float)?);

                            var similarity = CosineSimilarity(questionEmbedding, embedding);
                            var weightedSimilarity = ApplySourceTypeWeight(similarity, kb.SourceType);
                            return ((KnowledgeBase, float)?)(kb, weightedSimilarity);
                        }
                        catch
                        {
                            return default((KnowledgeBase, float)?);
                        }
                    })
                    .Where(x => x.HasValue)
                    .Select(x => x.Value)
                    .OrderByDescending(x => x.Item2)
                    .Take(3)
                    .ToList();

                _logger.LogInformation($"✓ 검색 완료 ({topResults.Count}개)");

                // 3-1. 상위 결과 내 해결안 충돌 감지 및 다수결/점수 기반 우선안 선택
                var voteResult = BuildResolutionVote(topResults);
                var selectedResults = voteResult.SelectedCases;

                // 4. View count 증가
                if (topResults.Count > 0)
                {
                    topResults[0].Item1.ViewCount++;
                    await _context.SaveChangesAsync();
                }

                // 5. GPT로 답변 생성 (사용자는 유사도/점수 노출 없이 명확한 답변 우선)
                var contextText = BuildContext(topResults, voteResult, role);
                var topScore = topResults.Count > 0 ? topResults[0].Item2 : 0f;
                var answer = await GenerateAnswerAsync(question, contextText, role, topScore);

                return new RagResponse
                {
                    Answer = answer,
                    ConflictDetected = voteResult.ConflictDetected,
                    DecisionRule = voteResult.DecisionRule,
                    RelatedKBs = topResults.Select(x => new KBSummary
                    {
                        Id = x.Item1.Id,
                        Problem = x.Item1.Problem,
                        Solution = x.Item1.Solution,
                        SourceType = x.Item1.SourceType,
                        Similarity = x.Item2,
                        IsSelected = selectedResults.Any(s => s.kb.Id == x.Item1.Id)
                    }).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ RAG 오류: {ex.Message}");
                throw;
            }
        }

        private async Task<string> GenerateAnswerAsync(string question, string context, string role, float topScore)
        {
            try
            {
                if (role != "admin" && topScore < 0.42f)
                {
                    return "현재 보유한 공개 지식으로는 이 질문에 대해 정확한 안내를 드리기 어렵습니다. 정확한 확인을 위해 관리자에게 문의해 주세요.";
                }

                var userRules = role == "admin"
                    ? "1) 충돌 감지가 있는 경우, 다수결/점수 우선안을 먼저 제시하고 \"다른 사례도 있음\"을 짧게 언급\n2) 가능한 경우 점수(유사도 %)를 함께 설명\n3) 확신이 낮으면 단정하지 말고 확인 질문을 1개 제시\n4) 필요시 단계별 해결방법을 제시"
                    : "1) 유사도/점수/벡터/랭킹 같은 내부 용어는 절대 언급하지 않는다\n2) 확실한 사실만 간결하게 답하고 모르면 모른다고 명시한다\n3) 확신이 낮으면 추측하지 말고 관리자 문의를 안내한다\n4) 고객이 바로 실행할 수 있는 단계 중심으로 설명한다";

                var prompt = $@"{context}

【사용자 질문】
{question}

위의 내부 참조 정보를 바탕으로 친절하고 정확하게 답변해주세요.
답변 규칙:
{userRules}";

                var request = new
                {
                    model = "gpt-3.5-turbo",
                    messages = new[]
                    {
                        new
                        {
                            role = "system",
                            content = "당신은 고객 지원 담당자입니다. 과거 상담 사례를 기반으로 정확하고 친절하게 답변합니다. 한국어만 사용하세요."
                        },
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.7,
                    max_tokens = 1000
                };

                var response = await _httpClient.PostAsJsonAsync(
                    "https://api.openai.com/v1/chat/completions",
                    request);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"답변 생성 실패: {error}");
                    throw new Exception("GPT 답변 생성 실패");
                }

                var jsonString = await response.Content.ReadAsStringAsync();
                var json = JsonDocument.Parse(jsonString).RootElement;
                var answer = json
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                _logger.LogInformation($"✓ 답변 생성 완료");
                return answer ?? "답변을 생성할 수 없습니다.";
            }
            catch (Exception ex)
            {
                _logger.LogError($"답변 생성 오류: {ex.Message}");
                throw;
            }
        }

        private string BuildContext(List<(KnowledgeBase kb, float similarity)> results, ResolutionVoteResult voteResult, string role)
        {
            if (role != "admin")
            {
                return BuildUserContext(results);
            }

            var sb = new StringBuilder();
            sb.AppendLine("【관련된 과거 상담 사례】");
            for (int i = 0; i < results.Count; i++)
            {
                var (kb, similarity) = results[i];
                sb.AppendLine($"\n{i + 1}. 유사도: {similarity:P0}");
                sb.AppendLine($"   문제: {kb.Problem}");
                sb.AppendLine($"   해결: {kb.Solution}");
            }

            sb.AppendLine("\n【충돌 분석 결과】");
            sb.AppendLine($"- 충돌 감지: {(voteResult.ConflictDetected ? "예" : "아니오")}");
            sb.AppendLine($"- 선택 규칙: {voteResult.DecisionRule}");

            if (voteResult.SelectedCases.Count > 0)
            {
                var top = voteResult.SelectedCases[0];
                sb.AppendLine($"- 우선 선택 사례: KB#{top.kb.Id} (유사도 {top.similarity:P0})");
                sb.AppendLine($"- 우선 해결안: {top.kb.Solution}");
            }

            return sb.ToString();
        }

        private string BuildUserContext(List<(KnowledgeBase kb, float similarity)> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("【답변 후보 정보(우선순위 순)】");

            for (int i = 0; i < results.Count; i++)
            {
                var (kb, _) = results[i];
                sb.AppendLine($"\n우선순위 {i + 1}");
                sb.AppendLine($"- 핵심 질문: {kb.Problem}");
                sb.AppendLine($"- 권장 안내: {kb.Solution}");
            }

            sb.AppendLine("\n※ 주의: 답변에는 우선순위, 유사도, 사례/내역 건수 같은 내부 기준을 노출하지 말 것.");
            return sb.ToString();
        }

        private ResolutionVoteResult BuildResolutionVote(List<(KnowledgeBase kb, float similarity)> results)
        {
            if (results.Count == 0)
            {
                return new ResolutionVoteResult
                {
                    ConflictDetected = false,
                    DecisionRule = "후보 없음",
                    SelectedCases = new List<(KnowledgeBase kb, float similarity)>()
                };
            }

            var grouped = results
                .GroupBy(x => NormalizeSolutionForVote(x.kb.Solution))
                .Select(g => new
                {
                    Key = g.Key,
                    Count = g.Count(),
                    SimilaritySum = g.Sum(x => x.similarity),
                    Cases = g.ToList()
                })
                .OrderByDescending(x => x.Count)
                .ThenByDescending(x => x.SimilaritySum)
                .ToList();

            var winner = grouped.First();
            var conflictDetected = grouped.Count > 1;
            var rule = conflictDetected
                ? "상위 3개 기준 다수결(동률 시 유사도 합계 우선)"
                : "단일 해법";

            return new ResolutionVoteResult
            {
                ConflictDetected = conflictDetected,
                DecisionRule = rule,
                SelectedCases = winner.Cases
            };
        }

        private string NormalizeSolutionForVote(string? solution)
        {
            if (string.IsNullOrWhiteSpace(solution))
                return "";

            // 공백/기호를 제거해 유사한 표현을 같은 해법 후보로 본다.
            return Regex.Replace(solution, "[\\s\\p{P}\\p{S}]", "").Trim().ToLowerInvariant();
        }

        private float CosineSimilarity(float[] vec1, float[] vec2)
        {
            float dotProduct = 0, normA = 0, normB = 0;
            for (int i = 0; i < vec1.Length; i++)
            {
                dotProduct += vec1[i] * vec2[i];
                normA += vec1[i] * vec1[i];
                normB += vec2[i] * vec2[i];
            }
            return normA == 0 || normB == 0 ? 0 : (float)(dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB)));
        }

        private float ApplySourceTypeWeight(float similarity, string? sourceType)
        {
            // official KB에 15% 가중치 부여 (최대 1.0)
            if (string.Equals(sourceType, "official", StringComparison.OrdinalIgnoreCase))
            {
                return MathF.Min(1f, similarity * 1.15f);
            }
            return similarity;
        }
    }

    public class RagResponse
    {
        public string Answer { get; set; } = string.Empty;
        public bool ConflictDetected { get; set; }
        public string? DecisionRule { get; set; }
        public List<KBSummary> RelatedKBs { get; set; } = new();
    }

    public class KBSummary
    {
        public int Id { get; set; }
        public string? Problem { get; set; }
        public string? Solution { get; set; }
        public string? SourceType { get; set; }
        public float Similarity { get; set; }
        public bool IsSelected { get; set; }
    }

    public class ResolutionVoteResult
    {
        public bool ConflictDetected { get; set; }
        public string DecisionRule { get; set; } = string.Empty;
        public List<(KnowledgeBase kb, float similarity)> SelectedCases { get; set; } = new();
    }
}
