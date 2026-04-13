using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using AiDeskApi.Data;
using AiDeskApi.Models;

namespace AiDeskApi.Services
{
    public interface IRagService
    {
        // role: "admin" = 전체 KB 조회, "user" = visibility='user'만 조회
        // history: 이전 대화 [(role, content)] 최근 순 (오래된 것부터)
        // platform: 공통이면 전체 플랫폼 조회, 특정값이면 공통 + 해당 플랫폼 조회
        Task<RagResponse> SearchAndGenerateAsync(
            string question,
            string role = "user",
            string platform = "공통",
            IList<(string Role, string Content)>? history = null,
            RagRuntimeOptions? runtimeOptions = null);
    }

    // 질문 임베딩 -> 유사 KB 검색 -> 답변 생성까지 RAG 전체 흐름을 담당
    public class OpenAiRagService : IRagService
    {
        private const float KeywordBoostPerMatch = 0.03f;
        private const float MaxKeywordBoost = 0.12f;
        private const int FinalTopK = 5;

        private readonly AiDeskContext _context;
        private readonly IEmbeddingService _embeddingService;
        private readonly IVectorSearchService _vectorSearchService;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IChatbotPromptTemplateService _promptTemplates;
        private readonly ILogger<OpenAiRagService> _logger;

        public OpenAiRagService(
            AiDeskContext context,
            IEmbeddingService embeddingService,
            IVectorSearchService vectorSearchService,
            HttpClient httpClient,
            IConfiguration configuration,
            IChatbotPromptTemplateService promptTemplates,
            ILogger<OpenAiRagService> logger)
        {
            _context = context;
            _embeddingService = embeddingService;
            _vectorSearchService = vectorSearchService;
            _httpClient = httpClient;
            _configuration = configuration;
            _promptTemplates = promptTemplates;
            _logger = logger;

            var apiKey = configuration["OpenAI:ApiKey"];
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }

        public async Task<RagResponse> SearchAndGenerateAsync(
            string question,
            string role = "user",
            string platform = "공통",
            IList<(string Role, string Content)>? history = null,
            RagRuntimeOptions? runtimeOptions = null)
        {
            try
            {
                _logger.LogInformation($"📝 질문 받음 [{role}/{platform}]: {question}");
                runtimeOptions ??= new RagRuntimeOptions();

                if (runtimeOptions.PromptOnly)
                {
                    var promptOnlyAnswer = await GenerateAnswerAsync(
                        question,
                        string.Empty,
                        role,
                        1.0f,
                        history,
                        runtimeOptions);

                    return new RagResponse
                    {
                        Answer = promptOnlyAnswer,
                        TopSimilarity = 1.0f,
                        IsLowSimilarity = false,
                        ConflictDetected = false,
                        DecisionRule = "prompt-only",
                        RelatedKBs = new List<KBSummary>()
                    };
                }

                // 1. 질문을 벡터로 변환
                var questionEmbedding = await _embeddingService.EmbedTextAsync(question);

                // 2. role에 따라 KB 필터 적용
                //    admin: 모든 KB 조회
                //    user:  visibility='user'만 조회
                var query = _context.KnowledgeBases
                    .Include(x => x.SimilarQuestions)
                    .AsQueryable();
                if (role != "admin")
                {
                    query = query.Where(x => x.Visibility == "user");
                }

                var normalizedPlatform = NormalizePlatform(platform);
                var questionTokens = ExtractKeywordTokens(question);

                List<(KnowledgeBase kb, float baseSimilarity, float keywordBoost, float adjustedSimilarity, string matchedQuestion, bool matchedSimilar, int keywordMatchCount, List<string> matchedKeywords)> scoredResults;
                var fallbackOnVectorEmpty = _configuration.GetValue<bool?>("Rag:FallbackOnVectorEmpty") ?? true;

                try
                {
                    var vectorTopK = _configuration.GetValue<int?>("Rag:VectorTopK") ?? 80;
                    var searchHits = await _vectorSearchService.SearchAsync(questionEmbedding, role, normalizedPlatform, Math.Max(10, vectorTopK));

                    if (searchHits.Count == 0 && fallbackOnVectorEmpty)
                    {
                        _logger.LogWarning("⚠️ 벡터 검색 결과 없음. 로컬 유사도 fallback 수행");
                        scoredResults = await BuildLocalScoredResultsAsync(query, question, questionEmbedding, questionTokens, normalizedPlatform);
                    }
                    else
                    {
                        var candidateKbIds = searchHits
                            .Select(x => x.KbId)
                            .Distinct()
                            .ToList();

                        var kbs = await query
                            .AsNoTracking()
                            .Where(x => candidateKbIds.Contains(x.Id))
                            .ToListAsync();
                        var kbById = kbs.ToDictionary(x => x.Id, x => x);

                        scoredResults = searchHits
                            .GroupBy(x => x.KbId)
                            .Select(group =>
                            {
                                if (!kbById.TryGetValue(group.Key, out var kb))
                                {
                                    return default((KnowledgeBase kb, float baseSimilarity, float keywordBoost, float adjustedSimilarity, string matchedQuestion, bool matchedSimilar, int keywordMatchCount, List<string> matchedKeywords)?);
                                }

                                var best = group.OrderByDescending(x => x.Score).First();

                                var keywordTokens = string.IsNullOrWhiteSpace(kb.Tags)
                                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                                    : ParseKeywordTokens(kb.Tags);
                                var matchedKeywords = keywordTokens
                                    .Where(t => questionTokens.Contains(t))
                                    .OrderBy(x => x)
                                    .ToList();
                                var keywordMatchCount = matchedKeywords.Count;

                                var keywordBoost = CalculateKeywordBoost(question, kb.Tags);
                                var adjustedSimilarity = Math.Clamp(best.Score + keywordBoost, 0f, 1f);

                                return ((KnowledgeBase kb, float baseSimilarity, float keywordBoost, float adjustedSimilarity, string matchedQuestion, bool matchedSimilar, int keywordMatchCount, List<string> matchedKeywords)?)
                                    (kb, best.Score, keywordBoost, adjustedSimilarity, best.MatchedQuestion, best.IsSimilarQuestion, keywordMatchCount, matchedKeywords);
                            })
                            .Where(x => x.HasValue)
                            .Select(x => x!.Value)
                            .ToList();

                        if (scoredResults.Count == 0 && fallbackOnVectorEmpty)
                        {
                            _logger.LogWarning("⚠️ 벡터 검색 결과 매핑 실패. 로컬 유사도 fallback 수행");
                            scoredResults = await BuildLocalScoredResultsAsync(query, question, questionEmbedding, questionTokens, normalizedPlatform);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ 벡터 검색 실패. 로컬 유사도 fallback 수행");
                    scoredResults = await BuildLocalScoredResultsAsync(query, question, questionEmbedding, questionTokens, normalizedPlatform);
                }

                if (scoredResults.Count == 0)
                {
                    _logger.LogWarning("⚠️ KB 데이터 없음");
                    return new RagResponse
                    {
                        Answer = "유사한 상담내역이 없습니다. 담당자에게 문의해주세요.",
                        RelatedKBs = new List<KBSummary>()
                    };
                }

                var topResults = scoredResults
                    .OrderByDescending(x => x.adjustedSimilarity)
                    .Take(FinalTopK)
                    .Select(x => (x.kb, x.adjustedSimilarity, x.matchedQuestion, x.matchedSimilar))
                    .ToList();

                _logger.LogInformation($"✓ 검색 완료 ({topResults.Count}개)");

                var similarityThreshold = ResolveSimilarityThreshold(runtimeOptions);

                // 답변 생성에 사용하는 후보는 무조건 임계치 이상 사례로만 제한한다.
                var eligibleResults = topResults
                    .Where(x => x.Item2 >= similarityThreshold)
                    .ToList();

                // 임계치 이상 후보만으로 충돌 감지/우선안 선택을 수행한다.
                var voteResult = BuildResolutionVote(eligibleResults);
                var selectedResults = voteResult.SelectedCases;
                var topResultSet = topResults.Select(x => x.kb.Id).ToHashSet();
                var eligibleSet = eligibleResults.Select(x => x.Item1.Id).ToHashSet();
                var selectedSet = selectedResults.Select(x => x.kb.Id).ToHashSet();

                var retrievalDiagnostics = new RetrievalDiagnostics
                {
                    SimilarityThreshold = similarityThreshold,
                    QuestionTokens = questionTokens.OrderBy(x => x).ToList(),
                    Candidates = scoredResults
                        .Where(x => topResultSet.Contains(x.kb.Id))
                        .OrderByDescending(x => x.adjustedSimilarity)
                        .Select(x => new RetrievalCandidateDiagnostic
                        {
                            Id = x.kb.Id,
                            Problem = x.kb.Problem,
                            MatchedQuestion = x.matchedQuestion,
                            BaseSimilarity = x.baseSimilarity,
                            KeywordBoost = x.keywordBoost,
                            AdjustedSimilarity = x.adjustedSimilarity,
                            KeywordMatchCount = x.keywordMatchCount,
                            MatchedKeywords = x.matchedKeywords,
                            IncludedBySemantic = true,
                            IncludedByKeyword = false,
                            PassedThreshold = eligibleSet.Contains(x.kb.Id),
                            SelectedForAnswer = selectedSet.Contains(x.kb.Id)
                        })
                        .ToList()
                };

                // 4. View count 증가
                if (!runtimeOptions.DisablePersistence && topResults.Count > 0)
                {
                    topResults[0].Item1.ViewCount++;
                    await _context.SaveChangesAsync();
                }

                // 5. GPT로 답변 생성 (사용자는 유사도/점수 노출 없이 명확한 답변 우선)
                var contextText = BuildContext(eligibleResults, voteResult, role);
                var topScore = topResults.Count > 0 ? topResults[0].Item2 : 0f;
                var topMatchedQuestion = topResults.Count > 0 ? topResults[0].Item3 : null;
                var answer = await GenerateAnswerAsync(question, contextText, role, topScore, history, runtimeOptions);
                var isLowSimilarity = topScore < similarityThreshold;

                return new RagResponse
                {
                    Answer = answer,
                    TopSimilarity = topScore,
                    IsLowSimilarity = isLowSimilarity,
                    TopMatchedQuestion = topMatchedQuestion,
                    ConflictDetected = voteResult.ConflictDetected,
                    DecisionRule = voteResult.DecisionRule,
                    RetrievalDiagnostics = retrievalDiagnostics,
                    RelatedKBs = isLowSimilarity
                        ? new List<KBSummary>()
                        : eligibleResults
                        .Select(x => new KBSummary
                        {
                            Id = x.Item1.Id,
                            Problem = x.Item1.Problem,
                            Solution = x.Item1.Solution,
                            Similarity = x.Item2,
                            MatchedQuestion = x.Item3,
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

        private static string NormalizePlatform(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "공통";
            var trimmed = value.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) return "공통";
            if (string.Equals(trimmed, "전체", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "전체 플랫폼", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "all", StringComparison.OrdinalIgnoreCase))
                return "전체 플랫폼";
            if (string.Equals(trimmed, "common", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "공통", StringComparison.OrdinalIgnoreCase))
                return "공통";
            return trimmed.ToLowerInvariant();
        }

        private static List<string> ParsePlatforms(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new List<string> { "공통" };

            var parsed = raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizePlatform)
                .Where(x => !string.Equals(x, "전체 플랫폼", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();

            return parsed.Count == 0 ? new List<string> { "공통" } : parsed;
        }

        private async Task<string> GenerateAnswerAsync(
            string question,
            string context,
            string role,
            float topScore,
            IList<(string Role, string Content)>? history = null,
            RagRuntimeOptions? runtimeOptions = null)
        {
            try
            {
                runtimeOptions ??= new RagRuntimeOptions();
                var similarityThreshold = ResolveSimilarityThreshold(runtimeOptions);
                var isLowSimilarity = topScore < similarityThreshold;

                // KB 근거가 부족하면 대화 맥락으로 추정하지 않고 즉시 안내문 반환
                if (isLowSimilarity)
                {
                    return ResolveLowSimilarityMessage(role, runtimeOptions);
                }

                var systemPrompt = ResolveSystemPrompt(role, runtimeOptions);

                var messages = new List<object>
                {
                    new { role = "system", content = systemPrompt }
                };

                // 이전 대화 이력 삽입 (user/bot → user/assistant)
                if (history != null && history.Count > 0)
                {
                    foreach (var (histRole, histContent) in history!)
                    {
                        var gptRole = histRole == "bot" ? "assistant" : histRole;
                        messages.Add(new { role = gptRole, content = histContent });
                    }
                }

                string prompt;
                if (isLowSimilarity)
                {
                    prompt = question;
                }
                else
                {
                    var userRules = ResolveRulesPrompt(role, runtimeOptions);
                    if (runtimeOptions.PromptOnly)
                    {
                        prompt = $@"【사용자 질문】
{question}

답변 규칙:
{userRules}";
                    }
                    else
                    {
                        prompt = $@"{context}

【사용자 질문】
{question}

위의 내부 참조 정보를 바탕으로 친절하고 정확하게 답변해주세요.
아래 KB 근거 규칙을 반드시 지키세요.

[KB 근거 규칙]
1) 내부 참조 정보에 없는 사실/수치/경로/정책/원인/절차는 절대 추가하지 않는다.
2) 추측, 일반 상식 보완, 다른 사례 끼워넣기를 금지한다.
3) 답변의 각 핵심 문장은 내부 참조 정보에서 직접 확인 가능한 내용으로만 작성한다.
4) 질문의 일부가 내부 참조 정보에 없으면, 없는 항목임을 명시하고 확인 필요로 안내한다.
5) 특히 파일 경로/설정값/기한/금액/조건은 참조 근거가 있을 때만 말한다.

답변 규칙:
{userRules}";
                    }
                }

                messages.Add(new { role = "user", content = prompt });

                var request = new
                {
                    model = "gpt-3.5-turbo",
                    messages,
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

        private float ResolveSimilarityThreshold(RagRuntimeOptions options)
        {
            if (options.SimilarityThresholdOverride.HasValue)
            {
                var v = options.SimilarityThresholdOverride.Value;
                return Math.Clamp(v, 0.1f, 0.95f);
            }

            return _promptTemplates.SimilarityThreshold;
        }

        private string ResolveSystemPrompt(string role, RagRuntimeOptions options)
        {
            return string.IsNullOrWhiteSpace(options.SystemPromptOverride)
                ? (role == "admin" ? _promptTemplates.AdminSystemPrompt : _promptTemplates.UserSystemPrompt)
                : options.SystemPromptOverride;
        }

        private string ResolveRulesPrompt(string role, RagRuntimeOptions options)
        {
            return string.IsNullOrWhiteSpace(options.RulesPromptOverride)
                ? (role == "admin" ? _promptTemplates.AdminRulesPrompt : _promptTemplates.UserRulesPrompt)
                : options.RulesPromptOverride;
        }

        private string ResolveLowSimilarityMessage(string role, RagRuntimeOptions options)
        {
            return string.IsNullOrWhiteSpace(options.LowSimilarityMessageOverride)
                ? (role == "admin" ? _promptTemplates.AdminLowSimilarityMessage : _promptTemplates.UserLowSimilarityMessage)
                : options.LowSimilarityMessageOverride;
        }

        private string BuildContext(List<(KnowledgeBase kb, float similarity, string matchedQuestion, bool matchedSimilar)> results, ResolutionVoteResult voteResult, string role)
        {
            if (role != "admin")
            {
                return BuildUserContext(results);
            }

            var sb = new StringBuilder();
            sb.AppendLine("【관련된 과거 상담 사례】");
            for (int i = 0; i < results.Count; i++)
            {
                var (kb, similarity, matchedQuestion, matchedSimilar) = results[i];
                sb.AppendLine($"\n{i + 1}. 유사도: {similarity:P0}");
                sb.AppendLine($"   매칭 질문: {matchedQuestion} {(matchedSimilar ? "(유사질문)" : "(대표질문)")}");
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

        private string BuildUserContext(List<(KnowledgeBase kb, float similarity, string matchedQuestion, bool matchedSimilar)> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("【답변 후보 정보(우선순위 순)】");

            for (int i = 0; i < results.Count; i++)
            {
                var (kb, _, matchedQuestion, matchedSimilar) = results[i];
                sb.AppendLine($"\n우선순위 {i + 1}");
                sb.AppendLine($"- 매칭된 질문: {matchedQuestion} {(matchedSimilar ? "(유사질문)" : "(대표질문)")}");
                sb.AppendLine($"- 핵심 질문: {kb.Problem}");
                sb.AppendLine($"- 권장 안내: {kb.Solution}");
            }

            sb.AppendLine("\n※ 주의: 답변에는 우선순위, 유사도, 사례/내역 건수 같은 내부 기준을 노출하지 말 것.");
            return sb.ToString();
        }

        private ResolutionVoteResult BuildResolutionVote(List<(KnowledgeBase kb, float similarity, string matchedQuestion, bool matchedSimilar)> results)
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
                SelectedCases = winner.Cases.Select(c => (c.kb, c.similarity)).ToList()
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
            var length = Math.Min(vec1.Length, vec2.Length);
            if (length == 0)
            {
                return 0;
            }

            float dotProduct = 0, normA = 0, normB = 0;
            for (int i = 0; i < length; i++)
            {
                dotProduct += vec1[i] * vec2[i];
                normA += vec1[i] * vec1[i];
                normB += vec2[i] * vec2[i];
            }
            return normA == 0 || normB == 0 ? 0 : (float)(dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB)));
        }

        private float CalculateKeywordBoost(string question, string? rawKeywords)
        {
            if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(rawKeywords))
            {
                return 0f;
            }

            var questionTokens = ExtractKeywordTokens(question);
            if (questionTokens.Count == 0)
            {
                return 0f;
            }

            var keywordTokens = ParseKeywordTokens(rawKeywords);
            if (keywordTokens.Count == 0)
            {
                return 0f;
            }

            var matchedCount = keywordTokens.Count(t => questionTokens.Contains(t));
            if (matchedCount <= 0)
            {
                return 0f;
            }

            return Math.Min(matchedCount * KeywordBoostPerMatch, MaxKeywordBoost);
        }

        private async Task<List<(KnowledgeBase kb, float baseSimilarity, float keywordBoost, float adjustedSimilarity, string matchedQuestion, bool matchedSimilar, int keywordMatchCount, List<string> matchedKeywords)>>
            BuildLocalScoredResultsAsync(
                IQueryable<KnowledgeBase> baseQuery,
                string question,
                float[] questionEmbedding,
                HashSet<string> questionTokens,
                string normalizedPlatform)
        {
            var maxCandidateKb = _configuration.GetValue<int?>("Rag:MaxCandidateKb") ?? 0;
            var candidateQuery = baseQuery.AsNoTracking();
            if (maxCandidateKb > 0)
            {
                candidateQuery = candidateQuery
                    .OrderByDescending(x => x.UpdatedAt)
                    .ThenByDescending(x => x.Id)
                    .Take(maxCandidateKb);
            }

            var kbs = await candidateQuery.ToListAsync();

            if (!string.Equals(normalizedPlatform, "전체 플랫폼", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalizedPlatform, "공통", StringComparison.OrdinalIgnoreCase))
            {
                kbs = kbs
                    .Where(kb =>
                    {
                        var targets = ParsePlatforms(kb.Platform);
                        return targets.Contains("공통") || targets.Contains(normalizedPlatform);
                    })
                    .ToList();
            }

            return kbs
                .Select(kb =>
                {
                    var candidates = new List<(string question, float similarity, bool isSimilar)>();

                    if (!string.IsNullOrWhiteSpace(kb.ProblemEmbedding) && !string.IsNullOrWhiteSpace(kb.Problem))
                    {
                        var representativeEmbedding = ParseEmbedding(kb.ProblemEmbedding);
                        if (representativeEmbedding != null)
                        {
                            candidates.Add((
                                question: kb.Problem,
                                similarity: CosineSimilarity(questionEmbedding, representativeEmbedding),
                                isSimilar: false
                            ));
                        }
                    }

                    foreach (var sq in kb.SimilarQuestions)
                    {
                        if (string.IsNullOrWhiteSpace(sq.QuestionEmbedding) || string.IsNullOrWhiteSpace(sq.Question))
                            continue;

                        var similarEmbedding = ParseEmbedding(sq.QuestionEmbedding);
                        if (similarEmbedding == null) continue;

                        candidates.Add((
                            question: sq.Question,
                            similarity: CosineSimilarity(questionEmbedding, similarEmbedding),
                            isSimilar: true
                        ));
                    }

                    if (candidates.Count == 0) return default((KnowledgeBase kb, float baseSimilarity, float keywordBoost, float adjustedSimilarity, string matchedQuestion, bool matchedSimilar, int keywordMatchCount, List<string> matchedKeywords)?);

                    var best = candidates.OrderByDescending(x => x.similarity).First();

                    var keywordTokens = string.IsNullOrWhiteSpace(kb.Tags)
                        ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        : ParseKeywordTokens(kb.Tags);
                    var matchedKeywords = keywordTokens
                        .Where(t => questionTokens.Contains(t))
                        .OrderBy(x => x)
                        .ToList();
                    var keywordMatchCount = matchedKeywords.Count;

                    var keywordBoost = CalculateKeywordBoost(question, kb.Tags);
                    var adjustedSimilarity = Math.Clamp(best.similarity + keywordBoost, 0f, 1f);

                    return ((KnowledgeBase kb, float baseSimilarity, float keywordBoost, float adjustedSimilarity, string matchedQuestion, bool matchedSimilar, int keywordMatchCount, List<string> matchedKeywords)?)
                        (kb, best.similarity, keywordBoost, adjustedSimilarity, best.question, best.isSimilar, keywordMatchCount, matchedKeywords);
                })
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToList();
        }

        private static HashSet<string> ParseKeywordTokens(string rawKeywords)
        {
            return rawKeywords
                .Split(new[] { ',', ';', '|', '/', '#' }, StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(ExtractKeywordTokens)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static HashSet<string> ExtractKeywordTokens(string text)
        {
            var stopwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "문의", "관련", "처리", "요청", "문제", "해결", "오류", "에러", "이슈", "안내"
            };

            return Regex.Matches(text, "[\\p{L}\\p{Nd}]{2,}")
                .Select(m => m.Value.Trim().ToLowerInvariant())
                .Where(token => !stopwords.Contains(token))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private float[]? ParseEmbedding(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    return ParseNumberArray(root);
                }

                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                if (TryGetArrayProperty(root, "embedding", out var embeddingArray))
                {
                    return ParseNumberArray(embeddingArray);
                }

                if (TryGetArrayProperty(root, "values", out var valuesArray))
                {
                    return ParseNumberArray(valuesArray);
                }

                if (TryGetArrayProperty(root, "vector", out var vectorArray))
                {
                    return ParseNumberArray(vectorArray);
                }

                if (root.TryGetProperty("data", out var data)
                    && data.ValueKind == JsonValueKind.Array
                    && data.GetArrayLength() > 0)
                {
                    var first = data[0];
                    if (TryGetArrayProperty(first, "embedding", out var dataEmbeddingArray))
                    {
                        return ParseNumberArray(dataEmbeddingArray);
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGetArrayProperty(JsonElement obj, string propertyName, out JsonElement arrayElement)
        {
            if (obj.ValueKind == JsonValueKind.Object
                && obj.TryGetProperty(propertyName, out var element)
                && element.ValueKind == JsonValueKind.Array)
            {
                arrayElement = element;
                return true;
            }

            arrayElement = default;
            return false;
        }

        private static float[]? ParseNumberArray(JsonElement arrayElement)
        {
            if (arrayElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var result = new List<float>();
            foreach (var item in arrayElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                if (item.TryGetSingle(out var singleValue))
                {
                    result.Add(singleValue);
                }
                else
                {
                    result.Add((float)item.GetDouble());
                }
            }

            return result.Count == 0 ? null : result.ToArray();
        }
    }

    public class RagResponse
    {
        public string Answer { get; set; } = string.Empty;
        public float TopSimilarity { get; set; }
        public bool IsLowSimilarity { get; set; }
        public string? TopMatchedQuestion { get; set; }
        public bool ConflictDetected { get; set; }
        public string? DecisionRule { get; set; }
        public RetrievalDiagnostics? RetrievalDiagnostics { get; set; }
        public List<KBSummary> RelatedKBs { get; set; } = new();
    }

    public class RetrievalDiagnostics
    {
        public float SimilarityThreshold { get; set; }
        public List<string> QuestionTokens { get; set; } = new();
        public List<RetrievalCandidateDiagnostic> Candidates { get; set; } = new();
    }

    public class RetrievalCandidateDiagnostic
    {
        public int Id { get; set; }
        public string? Problem { get; set; }
        public string? MatchedQuestion { get; set; }
        public float BaseSimilarity { get; set; }
        public float KeywordBoost { get; set; }
        public float AdjustedSimilarity { get; set; }
        public int KeywordMatchCount { get; set; }
        public List<string> MatchedKeywords { get; set; } = new();
        public bool IncludedBySemantic { get; set; }
        public bool IncludedByKeyword { get; set; }
        public bool PassedThreshold { get; set; }
        public bool SelectedForAnswer { get; set; }
    }

    public class KBSummary
    {
        public int Id { get; set; }
        public string? Problem { get; set; }
        public string? Solution { get; set; }
        public float Similarity { get; set; }
        public string? MatchedQuestion { get; set; }
        public bool IsSelected { get; set; }
    }

    public class ResolutionVoteResult
    {
        public bool ConflictDetected { get; set; }
        public string DecisionRule { get; set; } = string.Empty;
        public List<(KnowledgeBase kb, float similarity)> SelectedCases { get; set; } = new();
    }

    public class RagRuntimeOptions
    {
        public bool DisablePersistence { get; set; }
        public bool PromptOnly { get; set; }
        public string? SystemPromptOverride { get; set; }
        public string? RulesPromptOverride { get; set; }
        public string? LowSimilarityMessageOverride { get; set; }
        public float? SimilarityThresholdOverride { get; set; }
    }
}
