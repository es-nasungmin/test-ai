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
        private const int RawVectorTopK = 80;
        private const int DocumentVectorTopK = 20;
        private const int ExpectedVectorTopK = 30;
        private const int MergeTopK = 12;
        private const int ReRankTopK = 8;
        private const int FinalTopK = 5;
        private const float KeywordBoostPerMatch = 0.01f;
        private const float MaxKeywordBoost = 0.03f;
        private const float KeywordBoostHardFloorGap = 0.05f;

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

                // 1) 사용자 질문 임베딩
                var normalizedQuestion = NormalizeQueryForEmbedding(question);
                var questionEmbedding = await _embeddingService.EmbedTextAsync(normalizedQuestion);
                var normalizedPlatform = NormalizePlatform(platform);
                var similarityThreshold = ResolveSimilarityThreshold(runtimeOptions);

                var kbQuery = _context.KnowledgeBases.AsNoTracking().AsQueryable();
                if (role != "admin")
                {
                    kbQuery = kbQuery.Where(x => x.Visibility == "user");
                }

                if (!string.Equals(normalizedPlatform, "전체 플랫폼", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(normalizedPlatform, "공통", StringComparison.OrdinalIgnoreCase))
                {
                    kbQuery = kbQuery.Where(kb => kb.Platform.Contains("공통") || kb.Platform.Contains(normalizedPlatform));
                }

                // 1-1) 벡터 검색: 본문(document) + 예상질문(expected) 포인트를 함께 조회
                IReadOnlyList<VectorSearchHit> semanticHits;
                try
                {
                    semanticHits = await _vectorSearchService.SearchAsync(questionEmbedding, role, normalizedPlatform, RawVectorTopK);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ 벡터 검색 실패. 빈 결과로 진행");
                    semanticHits = Array.Empty<VectorSearchHit>();
                }

                var documentVectorHits = semanticHits
                    .Where(x => !x.IsSimilarQuestion)
                    .OrderByDescending(x => x.Score)
                    .Take(DocumentVectorTopK)
                    .ToList();

                var expectedVectorHits = semanticHits
                    .Where(x => x.IsSimilarQuestion)
                    .OrderByDescending(x => x.Score)
                    .Take(ExpectedVectorTopK)
                    .ToList();

                var semanticTop = documentVectorHits.Concat(expectedVectorHits).ToList();

                // 2) 질문 키워드 추출 (키워드는 임베딩하지 않고 약한 가산/진단 용도로만 사용)
                var questionTokens = ExtractKeywordTokens(normalizedQuestion);

                var candidateKbIds = semanticTop
                    .Select(x => x.KbId)
                    .Distinct()
                    .ToList();

                var kbById = await kbQuery
                    .Where(x => candidateKbIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id);

                var documentBestByKb = documentVectorHits
                    .GroupBy(x => x.KbId)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Score).First());

                var expectedBestByKb = expectedVectorHits
                    .GroupBy(x => x.KbId)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Score).First());

                var mergedCandidates = candidateKbIds
                    .Where(kbById.ContainsKey)
                    .Select(id =>
                    {
                        documentBestByKb.TryGetValue(id, out var bestDoc);
                        expectedBestByKb.TryGetValue(id, out var bestExpected);

                        var bestSemantic = bestExpected != null && (bestDoc == null || bestExpected.Score >= bestDoc.Score)
                            ? bestExpected
                            : bestDoc;

                        var semanticScore = bestSemantic?.Score ?? 0f;
                        var matchedQuestion = bestSemantic?.MatchedQuestion
                            ?? kbById[id].Title
                            ?? kbById[id].Problem
                            ?? string.Empty;
                        var isExpectedMatch = bestSemantic?.IsSimilarQuestion == true;

                        var keywordInfo = BuildKeywordBoostInfo(kbById[id], questionTokens);
                        var keywordBoost = semanticScore >= similarityThreshold - KeywordBoostHardFloorGap
                            ? keywordInfo.Boost
                            : 0f;

                        return new MergedRetrievalCandidate
                        {
                            Kb = kbById[id],
                            MatchedQuestion = matchedQuestion,
                            IsExpectedMatch = isExpectedMatch,
                            SemanticScore = semanticScore,
                            KeywordScore = keywordBoost,
                            FinalScore = semanticScore + keywordBoost,
                            IncludedBySemantic = bestSemantic != null,
                            IncludedByKeyword = keywordInfo.MatchCount > 0,
                            MatchedKeywords = keywordInfo.MatchedKeywords,
                            KeywordMatchCount = keywordInfo.MatchCount
                        };
                    })
                    .OrderByDescending(x => x.FinalScore)
                    .ThenByDescending(x => x.SemanticScore)
                    .Take(MergeTopK)
                    .ToList();

                // 3) 상위 후보를 AI로 재정렬 후 답변 컨텍스트 후보를 확정
                var reranked = await ReRankCandidatesAsync(question, mergedCandidates, ReRankTopK);
                var topResults = reranked
                    .Take(FinalTopK)
                    .Select(x => (x.Kb, x.FinalScore, x.MatchedQuestion, x.IsExpectedMatch))
                    .ToList();

                var docTopK = Math.Clamp(_configuration.GetValue<int?>("Rag:DocumentTopK") ?? 15, 1, 20);
                List<DocumentChunkSearchHit> documentHits;
                try
                {
                    // 본문/예상질문 벡터 모두 임계치 미달일 때만 문서형 근거를 보조로 탐색
                    if (!topResults.Any(x => x.Item2 >= similarityThreshold))
                    {
                        documentHits = await _documentKnowledgeService.SearchChunksAsync(
                            questionEmbedding,
                            role,
                            normalizedPlatform,
                            docTopK);
                    }
                    else
                    {
                        documentHits = new List<DocumentChunkSearchHit>();
                    }
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
                        TopSimilarity = 0f,
                        IsLowSimilarity = true,
                        DecisionRule = "후보 없음",
                        RelatedKBs = new List<KBSummary>(),
                        RelatedDocuments = new List<DocumentReferenceSummary>()
                    };
                }

                _logger.LogInformation($"✓ 검색 완료 (kb={topResults.Count}, doc={documentHits.Count})");

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
                var topResultSet = topResults.Select(x => x.Item1.Id).ToHashSet();
                var eligibleSet = eligibleResults.Select(x => x.Item1.Id).ToHashSet();
                var selectedSet = selectedResults.Select(x => x.kb.Id).ToHashSet();

                var retrievalDiagnostics = new RetrievalDiagnostics
                {
                    SimilarityThreshold = similarityThreshold,
                    QuestionTokens = questionTokens.OrderBy(x => x).ToList(),
                    Candidates = mergedCandidates
                        .Where(x => topResultSet.Contains(x.Kb.Id))
                        .OrderByDescending(x => x.FinalScore)
                        .Select(x => new RetrievalCandidateDiagnostic
                        {
                            Id = x.Kb.Id,
                            Title = string.IsNullOrWhiteSpace(x.Kb.Title) ? x.Kb.Problem : x.Kb.Title,
                            MatchedQuestion = x.MatchedQuestion,
                            BaseSimilarity = x.SemanticScore,
                            KeywordBoost = x.KeywordScore,
                            AdjustedSimilarity = x.FinalScore,
                            KeywordMatchCount = x.KeywordMatchCount,
                            MatchedKeywords = x.MatchedKeywords,
                            IncludedBySemantic = x.IncludedBySemantic,
                            IncludedByKeyword = x.IncludedByKeyword,
                            PassedThreshold = eligibleSet.Contains(x.Kb.Id),
                            SelectedForAnswer = selectedSet.Contains(x.Kb.Id)
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
                var topKb = topResults.Count > 0 ? topResults[0].Item1 : null;
                var topMatchedKbTitle = topKb != null
                    ? (string.IsNullOrWhiteSpace(topKb.Title) ? topKb.Problem : topKb.Title)
                    : null;
                var topMatchedKbContent = topKb?.Solution;

                var answer = await GenerateAnswerAsync(question, contextText, role, topScore, history, runtimeOptions);
                var isLowSimilarity = topScore < similarityThreshold;

                return new RagResponse
                {
                    Answer = answer,
                    TopSimilarity = topScore,
                    IsLowSimilarity = isLowSimilarity,
                    TopMatchedQuestion = topMatchedQuestion,
                    TopMatchedKbTitle = topMatchedKbTitle,
                    TopMatchedKbContent = topMatchedKbContent,
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
                sb.AppendLine($"   매칭 근거: {matchedQuestion} {(matchedSimilar ? "(예상질문)" : "(제목/내용)")}");
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
                sb.AppendLine($"- 매칭된 근거: {matchedQuestion} {(matchedSimilar ? "(예상질문)" : "(제목/내용)")}");
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

        private static string NormalizeQueryForEmbedding(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var normalized = raw.Trim().ToLowerInvariant();
            normalized = Regex.Replace(normalized, "[\\s]+", " ");

            // 경계 케이스에서 의미가 같은 표현을 통일해 임베딩 분산을 줄인다.
            normalized = Regex.Replace(normalized, "안\\s*됨|안\\s*돼요|안\\s*되요|안\\s*됩니다|안\\s*되는", "안돼");
            normalized = Regex.Replace(normalized, "불가|조회\\s*불가|확인\\s*불가", "안돼");
            normalized = Regex.Replace(normalized, "안\\s*보임|안\\s*보여요|안\\s*보입니다", "안보여");

            return normalized;
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

        private static HashSet<string> ParseKeywordTokens(string rawKeywords)
        {
            return rawKeywords
                .Split(new[] { ',', ';', '|', '/', '#' }, StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(ExtractKeywordTokens)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private KeywordBoostInfo BuildKeywordBoostInfo(KnowledgeBase kb, HashSet<string> questionTokens)
        {
            if (questionTokens.Count == 0)
            {
                return new KeywordBoostInfo();
            }

            var titleTokens = string.IsNullOrWhiteSpace(kb.Title)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : ExtractKeywordTokens(kb.Title);
            var keywordTokens = string.IsNullOrWhiteSpace(kb.Keywords)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : ParseKeywordTokens(kb.Keywords);

            var matched = questionTokens
                .Where(token => titleTokens.Contains(token) || keywordTokens.Contains(token))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            if (matched.Count == 0)
            {
                return new KeywordBoostInfo();
            }

            var boost = Math.Min(matched.Count * KeywordBoostPerMatch, MaxKeywordBoost);

            return new KeywordBoostInfo
            {
                Boost = boost,
                MatchCount = matched.Count,
                MatchedKeywords = matched
            };
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

        private async Task<List<MergedRetrievalCandidate>> ReRankCandidatesAsync(
            string question,
            List<MergedRetrievalCandidate> candidates,
            int topK)
        {
            if (candidates.Count <= 1)
            {
                return candidates.OrderByDescending(x => x.FinalScore).Take(topK).ToList();
            }

            var candidateLines = candidates.Select((x, idx) =>
                $"{idx + 1}. id={x.Kb.Id}, title={x.Kb.Title}, semantic={x.SemanticScore:F3}, final={x.FinalScore:F3}, matched={x.MatchedQuestion}, source={(x.IsExpectedMatch ? "expected" : "document")}, content={TruncateText(x.Kb.Solution, 180)}");

            var prompt = $@"사용자 질문과 관련성 순으로 후보를 재정렬하세요.

질문:
{question}

후보:
{string.Join("\n", candidateLines)}

규칙:
- 반드시 JSON 배열만 응답
- 배열 요소는 후보 id(int)
- 관련성 높은 순서로 최대 {topK}개 반환
- 근거가 약한 후보는 제외 가능";

            var request = new
            {
                model = _chatModel,
                messages = new[]
                {
                    new { role = "system", content = "당신은 RAG 검색 재정렬기입니다. 반드시 JSON 정수 배열만 응답하세요." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.1,
                max_tokens = 180
            };

            try
            {
                var response = await _httpClient.PostAsJsonAsync(_chatCompletionsEndpoint, request);
                if (!response.IsSuccessStatusCode)
                {
                    return candidates.OrderByDescending(x => x.FinalScore).Take(topK).ToList();
                }

                var raw = await response.Content.ReadAsStringAsync();
                var root = JsonDocument.Parse(raw).RootElement;
                var text = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                var rankedIds = ParseIdArray(text, topK);

                if (rankedIds.Count == 0)
                {
                    return candidates.OrderByDescending(x => x.FinalScore).Take(topK).ToList();
                }

                var candidateById = candidates.ToDictionary(x => x.Kb.Id);
                var reranked = rankedIds
                    .Where(candidateById.ContainsKey)
                    .Select(id => candidateById[id])
                    .ToList();

                if (reranked.Count < topK)
                {
                    var fill = candidates
                        .Where(x => !rankedIds.Contains(x.Kb.Id))
                        .OrderByDescending(x => x.FinalScore)
                        .Take(topK - reranked.Count);
                    reranked.AddRange(fill);
                }

                return reranked.Take(topK).ToList();
            }
            catch
            {
                return candidates.OrderByDescending(x => x.FinalScore).Take(topK).ToList();
            }
        }

        private static List<int> ParseIdArray(string? raw, int maxCount)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new List<int>();

            var trimmed = raw.Trim();
            var jsonStart = trimmed.IndexOf('[');
            var jsonEnd = trimmed.LastIndexOf(']');
            if (jsonStart < 0 || jsonEnd <= jsonStart)
            {
                return new List<int>();
            }

            try
            {
                var json = trimmed.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var ids = JsonSerializer.Deserialize<List<int>>(json) ?? new List<int>();
                return ids.Distinct().Take(maxCount).ToList();
            }
            catch
            {
                return new List<int>();
            }
        }

        private sealed class KeywordBoostInfo
        {
            public float Boost { get; init; }
            public int MatchCount { get; init; }
            public List<string> MatchedKeywords { get; init; } = new();
        }

        private sealed class MergedRetrievalCandidate
        {
            public KnowledgeBase Kb { get; init; } = default!;
            public string MatchedQuestion { get; init; } = string.Empty;
            public bool IsExpectedMatch { get; init; }
            public float SemanticScore { get; init; }
            public float KeywordScore { get; init; }
            public float FinalScore { get; init; }
            public bool IncludedBySemantic { get; init; }
            public bool IncludedByKeyword { get; init; }
            public int KeywordMatchCount { get; init; }
            public List<string> MatchedKeywords { get; init; } = new();
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
        public string? TopMatchedKbTitle { get; set; }
        public string? TopMatchedKbContent { get; set; }
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
