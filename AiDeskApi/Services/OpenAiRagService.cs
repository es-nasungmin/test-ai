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
        private const float MaxTextMatchBoost = 0.08f;
        private const int FinalTopK = 5;

        private readonly AiDeskContext _context;
        private readonly IEmbeddingService _embeddingService;
        private readonly IVectorSearchService _vectorSearchService;
        private readonly IDocumentKnowledgeService _documentKnowledgeService;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IChatbotPromptTemplateService _promptTemplates;
        private readonly ILogger<OpenAiRagService> _logger;
        private readonly string _chatCompletionsEndpoint;
        private readonly string _chatModel;
        private readonly float _chatTemperature;
        private readonly int _chatMaxTokens;

        public OpenAiRagService(
            AiDeskContext context,
            IEmbeddingService embeddingService,
            IVectorSearchService vectorSearchService,
            IDocumentKnowledgeService documentKnowledgeService,
            HttpClient httpClient,
            IConfiguration configuration,
            IChatbotPromptTemplateService promptTemplates,
            ILogger<OpenAiRagService> logger)
        {
            _context = context;
            _embeddingService = embeddingService;
            _vectorSearchService = vectorSearchService;
            _documentKnowledgeService = documentKnowledgeService;
            _httpClient = httpClient;
            _configuration = configuration;
            _promptTemplates = promptTemplates;
            _logger = logger;

            var apiKey = configuration["OpenAI:ApiKey"];
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            _chatCompletionsEndpoint = configuration["OpenAI:Endpoint"] ?? "https://api.openai.com/v1/chat/completions";
            _chatModel = configuration["OpenAI:Model"] ?? "gpt-4o-mini";
            _chatTemperature = Math.Clamp(configuration.GetValue<float?>("OpenAI:ChatTemperature") ?? 0.7f, 0f, 2f);
            _chatMaxTokens = Math.Clamp(configuration.GetValue<int?>("OpenAI:ChatMaxTokens") ?? 1000, 100, 4000);
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

                List<(KnowledgeBase kb, float baseSimilarity, float keywordBoost, float adjustedSimilarity, string matchedQuestion, bool matchedSimilar, int keywordMatchCount, List<string> matchedKeywords, bool includedBySemantic, bool includedByKeyword)> scoredResults;
                var fallbackOnVectorEmpty = _configuration.GetValue<bool?>("Rag:FallbackOnVectorEmpty") ?? true;

                try
                {
                    var vectorTopK = _configuration.GetValue<int?>("Rag:VectorTopK") ?? 80;
                    var searchHits = await _vectorSearchService.SearchAsync(questionEmbedding, role, normalizedPlatform, Math.Max(10, vectorTopK));

                    if (searchHits.Count == 0)
                    {
                        _logger.LogWarning("⚠️ 벡터 검색 결과 없음. 키워드 리콜 + 로컬 재계산 수행");
                        scoredResults = await BuildHybridScoredResultsAsync(
                            query,
                            questionEmbedding,
                            questionTokens,
                            normalizedPlatform,
                            searchHits);

                        if (scoredResults.Count == 0 && fallbackOnVectorEmpty)
                        {
                            _logger.LogWarning("⚠️ 하이브리드 리콜 결과 없음. 전체 로컬 유사도 fallback 수행");
                            scoredResults = await BuildLocalScoredResultsAsync(query, questionEmbedding, questionTokens, normalizedPlatform);
                        }
                    }
                    else
                    {
                        scoredResults = await BuildHybridScoredResultsAsync(
                            query,
                            questionEmbedding,
                            questionTokens,
                            normalizedPlatform,
                            searchHits);

                        if (scoredResults.Count == 0 && fallbackOnVectorEmpty)
                        {
                            _logger.LogWarning("⚠️ 벡터 검색 결과 매핑 실패. 로컬 유사도 fallback 수행");
                            scoredResults = await BuildLocalScoredResultsAsync(query, questionEmbedding, questionTokens, normalizedPlatform);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ 벡터 검색 실패. 로컬 유사도 fallback 수행");
                    scoredResults = await BuildLocalScoredResultsAsync(query, questionEmbedding, questionTokens, normalizedPlatform);
                }

                var topResults = scoredResults
                    .OrderByDescending(x => x.adjustedSimilarity)
                    .Take(FinalTopK)
                    .Select(x => (x.kb, x.adjustedSimilarity, x.matchedQuestion, x.matchedSimilar))
                    .ToList();

                var docTopK = Math.Clamp(_configuration.GetValue<int?>("Rag:DocumentTopK") ?? 3, 1, 10);
                List<DocumentChunkSearchHit> documentHits;
                try
                {
                    documentHits = await _documentKnowledgeService.SearchChunksAsync(
                        questionEmbedding,
                        questionTokens,
                        role,
                        normalizedPlatform,
                        docTopK);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ 문서형 KB 검색 실패. FAQ 채널만 사용");
                    documentHits = new List<DocumentChunkSearchHit>();
                }

                if (topResults.Count == 0 && documentHits.Count == 0)
                {
                    _logger.LogWarning("⚠️ KB/문서 후보 없음");
                    return new RagResponse
                    {
                        Answer = "유사한 상담내역이 없습니다. 담당자에게 문의해주세요.",
                        RelatedKBs = new List<KBSummary>(),
                        RelatedDocuments = new List<DocumentReferenceSummary>()
                    };
                }

                _logger.LogInformation($"✓ 검색 완료 (kb={topResults.Count}, doc={documentHits.Count})");

                var similarityThreshold = ResolveSimilarityThreshold(runtimeOptions);

                // 답변 생성에 사용하는 FAQ 후보는 임계치 이상 사례로 제한
                var eligibleResults = topResults
                    .Where(x => x.Item2 >= similarityThreshold)
                    .ToList();

                // 문서 후보도 임계치 이상만 근거 컨텍스트에 반영
                var eligibleDocumentHits = documentHits
                    .Where(x => x.Score >= similarityThreshold)
                    .ToList();

                // 임계치 이상 FAQ 후보만 충돌 감지/우선안 선택 수행
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
                            Title = string.IsNullOrWhiteSpace(x.kb.Title) ? x.kb.Problem : x.kb.Title,
                            MatchedQuestion = x.matchedQuestion,
                            BaseSimilarity = x.baseSimilarity,
                            KeywordBoost = x.keywordBoost,
                            AdjustedSimilarity = x.adjustedSimilarity,
                            KeywordMatchCount = x.keywordMatchCount,
                            MatchedKeywords = x.matchedKeywords,
                            IncludedBySemantic = x.includedBySemantic,
                            IncludedByKeyword = x.includedByKeyword,
                            PassedThreshold = eligibleSet.Contains(x.kb.Id),
                            SelectedForAnswer = selectedSet.Contains(x.kb.Id)
                        })
                        .ToList()
                };

                // View count 증가 (실제로 사용된 모든 FAQ)
                _logger.LogInformation($"📊 View count 처리: DisablePersistence={runtimeOptions.DisablePersistence}, selectedResults={selectedResults.Count}");
                if (!runtimeOptions.DisablePersistence && selectedResults.Count > 0)
                {
                    var kbIds = selectedResults.Select(x => x.kb.Id).ToList();
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $@"UPDATE KnowledgeBases 
                           SET ViewCount = ViewCount + 1, UpdatedAt = {DateTime.UtcNow}
                           WHERE Id IN ({string.Join(",", kbIds.Select(id => id.ToString()))})");
                    _logger.LogInformation($"✓ View count 증가: {kbIds.Count}개 FAQ 업데이트 완료");
                }

                // 답변 생성
                var faqContext = BuildContext(eligibleResults, voteResult, role);
                var documentContext = BuildDocumentContext(eligibleDocumentHits);
                var contextText = string.IsNullOrWhiteSpace(documentContext)
                    ? faqContext
                    : $"{faqContext}\n\n{documentContext}";

                var topKbScore = topResults.Count > 0 ? topResults[0].Item2 : 0f;
                var topDocScore = documentHits.Count > 0 ? documentHits[0].Score : 0f;
                var topScore = Math.Max(topKbScore, topDocScore);
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
                            Title = string.IsNullOrWhiteSpace(x.Item1.Title) ? x.Item1.Problem : x.Item1.Title,
                            Solution = x.Item1.Solution,
                            Similarity = x.Item2,
                            MatchedQuestion = x.Item3,
                            IsSelected = selectedResults.Any(s => s.kb.Id == x.Item1.Id)
                        }).ToList(),
                    RelatedDocuments = isLowSimilarity
                        ? new List<DocumentReferenceSummary>()
                        : eligibleDocumentHits
                        .Select(x => new DocumentReferenceSummary
                        {
                            DocumentId = x.DocumentId,
                            ChunkId = x.ChunkId,
                            DocumentName = x.DocumentName,
                            PageNumber = x.PageNumber,
                            Excerpt = TruncateText(x.Content, 220),
                            Similarity = x.Score
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
                    model = _chatModel,
                    messages,
                    temperature = _chatTemperature,
                    max_tokens = _chatMaxTokens
                };

                var response = await _httpClient.PostAsJsonAsync(
                    _chatCompletionsEndpoint,
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
                sb.AppendLine($"   제목: {(string.IsNullOrWhiteSpace(kb.Title) ? kb.Problem : kb.Title)}");
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
                sb.AppendLine($"- KB 제목: {(string.IsNullOrWhiteSpace(kb.Title) ? kb.Problem : kb.Title)}");
                sb.AppendLine($"- 권장 안내: {kb.Solution}");
            }

            sb.AppendLine("\n※ 주의: 답변에는 우선순위, 유사도, 사례/내역 건수 같은 내부 기준을 노출하지 말 것.");
            return sb.ToString();
        }

        private string BuildDocumentContext(List<DocumentChunkSearchHit> documentHits)
        {
            if (documentHits.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.AppendLine("【문서형 매뉴얼 근거】");

            for (var i = 0; i < documentHits.Count; i++)
            {
                var hit = documentHits[i];
                sb.AppendLine($"\n문서 근거 {i + 1}");
                sb.AppendLine($"- 문서: {hit.DocumentName} (p.{hit.PageNumber})");
                sb.AppendLine($"- 내용: {TruncateText(hit.Content, 380)}");
            }

            return sb.ToString();
        }

        private static string TruncateText(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var text = value.Trim();
            if (text.Length <= maxLength) return text;
            return text[..maxLength] + "...";
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

        private float CalculateKeywordBoost(HashSet<string> questionTokens, HashSet<string> keywordTokens)
        {
            if (questionTokens.Count == 0 || keywordTokens.Count == 0)
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

        private float CalculateTextMatchBoost(HashSet<string> questionTokens, string? matchedQuestion)
        {
            if (questionTokens.Count == 0 || string.IsNullOrWhiteSpace(matchedQuestion))
            {
                return 0f;
            }

            var matchedQuestionTokens = ExtractKeywordTokens(matchedQuestion);
            if (matchedQuestionTokens.Count == 0)
            {
                return 0f;
            }

            var intersectionCount = questionTokens.Count(t => matchedQuestionTokens.Contains(t));
            if (intersectionCount == 0)
            {
                return 0f;
            }

            var unionCount = questionTokens.Union(matchedQuestionTokens, StringComparer.OrdinalIgnoreCase).Count();
            if (unionCount <= 0)
            {
                return 0f;
            }

            var jaccard = (float)intersectionCount / unionCount;
            return Math.Min(jaccard * MaxTextMatchBoost, MaxTextMatchBoost);
        }

        private async Task<List<(KnowledgeBase kb, float baseSimilarity, float keywordBoost, float adjustedSimilarity, string matchedQuestion, bool matchedSimilar, int keywordMatchCount, List<string> matchedKeywords, bool includedBySemantic, bool includedByKeyword)>>
            BuildHybridScoredResultsAsync(
                IQueryable<KnowledgeBase> baseQuery,
                float[] questionEmbedding,
                HashSet<string> questionTokens,
                string normalizedPlatform,
                IReadOnlyList<VectorSearchHit> searchHits)
        {
            var keywordRecallTopK = Math.Clamp(_configuration.GetValue<int?>("Rag:KeywordRecallTopK") ?? 60, 10, 500);
            var keywordRecallTokenLimit = Math.Clamp(_configuration.GetValue<int?>("Rag:KeywordRecallTokenLimit") ?? 8, 3, 20);

            var vectorBestByKb = searchHits
                .GroupBy(x => x.KbId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.Score).First());

            var keywordCandidateIds = await BuildKeywordRecallKbIdsAsync(
                baseQuery,
                questionTokens,
                normalizedPlatform,
                keywordRecallTopK,
                keywordRecallTokenLimit);

            var candidateKbIds = vectorBestByKb.Keys
                .Concat(keywordCandidateIds)
                .Distinct()
                .ToList();

            if (candidateKbIds.Count == 0)
            {
                return new List<(KnowledgeBase kb, float baseSimilarity, float keywordBoost, float adjustedSimilarity, string matchedQuestion, bool matchedSimilar, int keywordMatchCount, List<string> matchedKeywords, bool includedBySemantic, bool includedByKeyword)>();
            }

            var kbs = await baseQuery
                .AsNoTracking()
                .Where(x => candidateKbIds.Contains(x.Id))
                .ToListAsync();

            var results = new List<(KnowledgeBase kb, float baseSimilarity, float keywordBoost, float adjustedSimilarity, string matchedQuestion, bool matchedSimilar, int keywordMatchCount, List<string> matchedKeywords, bool includedBySemantic, bool includedByKeyword)>();

            foreach (var kb in kbs)
            {
                var includedBySemantic = vectorBestByKb.TryGetValue(kb.Id, out var semanticHit);
                var includedByKeyword = keywordCandidateIds.Contains(kb.Id);

                float baseSimilarity;
                string matchedQuestion;
                bool matchedSimilar;

                if (includedBySemantic)
                {
                    baseSimilarity = semanticHit!.Score;
                    matchedQuestion = semanticHit.MatchedQuestion;
                    matchedSimilar = semanticHit.IsSimilarQuestion;
                }
                else if (TryFindBestLocalMatch(kb, questionEmbedding, out var localSimilarity, out var localMatchedQuestion, out var localMatchedSimilar))
                {
                    baseSimilarity = localSimilarity;
                    matchedQuestion = localMatchedQuestion;
                    matchedSimilar = localMatchedSimilar;
                }
                else
                {
                    continue;
                }

                var keywordTokens = BuildKbKeywordTokens(kb);
                var matchedKeywords = keywordTokens
                    .Where(t => questionTokens.Contains(t))
                    .OrderBy(x => x)
                    .ToList();
                var keywordMatchCount = matchedKeywords.Count;

                var keywordBoost = CalculateKeywordBoost(questionTokens, keywordTokens);
                var textMatchBoost = CalculateTextMatchBoost(questionTokens, matchedQuestion);
                var adjustedSimilarity = Math.Clamp(baseSimilarity + keywordBoost + textMatchBoost, 0f, 1f);

                results.Add((
                    kb,
                    baseSimilarity,
                    keywordBoost,
                    adjustedSimilarity,
                    matchedQuestion,
                    matchedSimilar,
                    keywordMatchCount,
                    matchedKeywords,
                    includedBySemantic,
                    includedByKeyword));
            }

            return results;
        }

        private async Task<HashSet<int>> BuildKeywordRecallKbIdsAsync(
            IQueryable<KnowledgeBase> baseQuery,
            HashSet<string> questionTokens,
            string normalizedPlatform,
            int topK,
            int tokenLimit)
        {
            if (questionTokens.Count == 0)
            {
                return new HashSet<int>();
            }

            var limitedTokenSet = questionTokens
                .Where(x => x.Length >= 2)
                .Take(tokenLimit)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (limitedTokenSet.Count == 0)
            {
                return new HashSet<int>();
            }

            // Include된 네비게이션 로드를 피하기 위해 키워드 리콜은 경량 컬럼만 사용
            var keywordBaseQuery = baseQuery
                .AsNoTracking()
                .Where(x => !string.IsNullOrWhiteSpace(x.Keywords))
                .Select(x => new KeywordRecallCandidate
                {
                    Id = x.Id,
                    Keywords = x.Keywords,
                    Platform = x.Platform,
                    UpdatedAt = x.UpdatedAt
                });

            IQueryable<KeywordRecallCandidate>? keywordMatchedQuery = null;
            foreach (var token in limitedTokenSet)
            {
                var pattern = $"%{token}%";
                var perTokenQuery = keywordBaseQuery
                    .Where(x => x.Keywords != null && EF.Functions.Like(x.Keywords, pattern));

                keywordMatchedQuery = keywordMatchedQuery == null
                    ? perTokenQuery
                    : keywordMatchedQuery.Union(perTokenQuery);
            }

            if (keywordMatchedQuery == null)
            {
                return new HashSet<int>();
            }

            var candidates = await keywordMatchedQuery
                .OrderByDescending(x => x.UpdatedAt)
                .ThenByDescending(x => x.Id)
                .ToListAsync();

            if (!string.Equals(normalizedPlatform, "전체 플랫폼", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalizedPlatform, "공통", StringComparison.OrdinalIgnoreCase))
            {
                candidates = candidates
                    .Where(kb =>
                    {
                        var targets = ParsePlatforms(kb.Platform);
                        return targets.Contains("공통") || targets.Contains(normalizedPlatform);
                    })
                    .ToList();
            }

            return candidates
                .Select(kb => new
                {
                    kb.Id,
                    MatchCount = ParseKeywordTokens(kb.Keywords ?? string.Empty).Count(t => limitedTokenSet.Contains(t)),
                    kb.UpdatedAt
                })
                .Where(x => x.MatchCount > 0)
                .OrderByDescending(x => x.MatchCount)
                .ThenByDescending(x => x.UpdatedAt)
                .Take(topK)
                .Select(x => x.Id)
                .ToHashSet();
        }

        private sealed class KeywordRecallCandidate
        {
            public int Id { get; init; }
            public string? Keywords { get; init; }
            public string Platform { get; init; } = "공통";
            public DateTime UpdatedAt { get; init; }
        }

        private bool TryFindBestLocalMatch(
            KnowledgeBase kb,
            float[] questionEmbedding,
            out float similarity,
            out string matchedQuestion,
            out bool matchedSimilar)
        {
            similarity = 0f;
            matchedQuestion = string.Empty;
            matchedSimilar = false;

            var hasCandidate = false;

            if (!string.IsNullOrWhiteSpace(kb.ProblemEmbedding) && !string.IsNullOrWhiteSpace(kb.Problem))
            {
                var representativeEmbedding = ParseEmbedding(kb.ProblemEmbedding);
                if (representativeEmbedding != null)
                {
                    similarity = CosineSimilarity(questionEmbedding, representativeEmbedding);
                    matchedQuestion = kb.Problem;
                    matchedSimilar = false;
                    hasCandidate = true;
                }
            }

            foreach (var sq in kb.SimilarQuestions)
            {
                if (string.IsNullOrWhiteSpace(sq.QuestionEmbedding) || string.IsNullOrWhiteSpace(sq.Question))
                {
                    continue;
                }

                var similarEmbedding = ParseEmbedding(sq.QuestionEmbedding);
                if (similarEmbedding == null)
                {
                    continue;
                }

                var sim = CosineSimilarity(questionEmbedding, similarEmbedding);
                if (!hasCandidate || sim > similarity)
                {
                    similarity = sim;
                    matchedQuestion = sq.Question;
                    matchedSimilar = true;
                    hasCandidate = true;
                }
            }

            return hasCandidate;
        }

        private async Task<List<(KnowledgeBase kb, float baseSimilarity, float keywordBoost, float adjustedSimilarity, string matchedQuestion, bool matchedSimilar, int keywordMatchCount, List<string> matchedKeywords, bool includedBySemantic, bool includedByKeyword)>>
            BuildLocalScoredResultsAsync(
                IQueryable<KnowledgeBase> baseQuery,
                float[] questionEmbedding,
                HashSet<string> questionTokens,
                string normalizedPlatform)
        {
            var maxCandidateKb = Math.Clamp(_configuration.GetValue<int?>("Rag:MaxCandidateKb") ?? 500, 100, 5000);
            var candidateQuery = baseQuery.AsNoTracking();
            candidateQuery = candidateQuery
                .OrderByDescending(x => x.UpdatedAt)
                .ThenByDescending(x => x.Id)
                .Take(maxCandidateKb);

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

                    if (candidates.Count == 0) return default((KnowledgeBase kb, float baseSimilarity, float keywordBoost, float adjustedSimilarity, string matchedQuestion, bool matchedSimilar, int keywordMatchCount, List<string> matchedKeywords, bool includedBySemantic, bool includedByKeyword)?);

                    var best = candidates.OrderByDescending(x => x.similarity).First();

                    var keywordTokens = BuildKbKeywordTokens(kb);
                    var matchedKeywords = keywordTokens
                        .Where(t => questionTokens.Contains(t))
                        .OrderBy(x => x)
                        .ToList();
                    var keywordMatchCount = matchedKeywords.Count;

                    var keywordBoost = CalculateKeywordBoost(questionTokens, keywordTokens);
                    var textMatchBoost = CalculateTextMatchBoost(questionTokens, best.question);
                    var adjustedSimilarity = Math.Clamp(best.similarity + keywordBoost + textMatchBoost, 0f, 1f);

                    return ((KnowledgeBase kb, float baseSimilarity, float keywordBoost, float adjustedSimilarity, string matchedQuestion, bool matchedSimilar, int keywordMatchCount, List<string> matchedKeywords, bool includedBySemantic, bool includedByKeyword)?)
                        (kb, best.similarity, keywordBoost, adjustedSimilarity, best.question, best.isSimilar, keywordMatchCount, matchedKeywords, false, false);
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

        private static HashSet<string> BuildKbKeywordTokens(KnowledgeBase kb)
        {
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(kb.Keywords))
            {
                tokens.UnionWith(ParseKeywordTokens(kb.Keywords));
            }

            return tokens;
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
        public List<DocumentReferenceSummary> RelatedDocuments { get; set; } = new();
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
        public string? Title { get; set; }
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
        public string? Title { get; set; }
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

    public class DocumentReferenceSummary
    {
        public int DocumentId { get; set; }
        public int ChunkId { get; set; }
        public string DocumentName { get; set; } = string.Empty;
        public int PageNumber { get; set; }
        public string Excerpt { get; set; } = string.Empty;
        public float Similarity { get; set; }
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
