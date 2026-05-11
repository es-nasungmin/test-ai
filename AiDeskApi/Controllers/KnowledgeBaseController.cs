using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Claims;
using AiDeskApi.Data;
using AiDeskApi.Models;
using AiDeskApi.Services;

namespace AiDeskApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // 기본적으로 모든 엔드포인트는 인증 필요; 공개 엔드포인트는 [AllowAnonymous] 명시
    public class KnowledgeBaseController : ControllerBase
    {
        private const string KbHistoryActionCreate = "create";
        private const string KbHistoryActionUpdate = "update";
        private const string KbHistoryActionDelete = "delete";
        private const string ExpectedQuestionHistoryActionAdd = "add";
        private const string ExpectedQuestionHistoryActionUpdate = "update";
        private const string ExpectedQuestionHistoryActionRemove = "remove";
        private const int MaxKeywords = 20;

        private readonly AiDeskContext _context;
        private readonly IEmbeddingService _embeddingService;
        private readonly IRagService _ragService;
        private readonly IVectorSearchService _vectorSearchService;
        private readonly IChatbotPromptTemplateService _chatbotPromptTemplates;
        private readonly IKnowledgeBaseWriterPromptTemplateService _kbWriterPromptTemplates;
        private readonly IKnowledgeExtractorService _knowledgeExtractorService;
        private readonly ILogger<KnowledgeBaseController> _logger;

        public KnowledgeBaseController(
            AiDeskContext context,
            IEmbeddingService embeddingService,
            IRagService ragService,
            IVectorSearchService vectorSearchService,
            IKnowledgeExtractorService knowledgeExtractorService,
            IChatbotPromptTemplateService chatbotPromptTemplates,
            IKnowledgeBaseWriterPromptTemplateService kbWriterPromptTemplates,
            ILogger<KnowledgeBaseController> logger)
        {
            _context = context;
            _embeddingService = embeddingService;
            _ragService = ragService;
            _vectorSearchService = vectorSearchService;
            _knowledgeExtractorService = knowledgeExtractorService;
            _chatbotPromptTemplates = chatbotPromptTemplates;
            _kbWriterPromptTemplates = kbWriterPromptTemplates;
            _logger = logger;
        }

        [AllowAnonymous] // 챗봇은 비로그인 사용자도 이용 가능
        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] AskRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Question))
                    return BadRequest("질문을 입력해주세요.");

                if (request.Question.Length > 300)
                    return BadRequest("질문은 300자 이내로 입력해주세요.");

                var role = string.IsNullOrWhiteSpace(request.Role) ? "user" : request.Role;
                var platform = NormalizePlatform(request.Platform);
                _logger.LogInformation($"❓ [{role}] 질문: {request.Question}");

                var noSave = request.NoSave == true;
                var actorName = noSave ? "알 수 없음" : await ResolveActorNameAsync(request.Username, request.UserLoginId);
                // RAG 서비스 기준: 최근 3턴(질문+답변) = 최대 6메시지 사용
                const int maxHistoryTurns = 3;
                const int messagesPerTurn = 2;
                const int maxHistoryMessages = maxHistoryTurns * messagesPerTurn;

                // 신규 필드(메시지 수)를 우선 사용하고, 레거시 턴 필드는 메시지 수로 변환해 호환한다.
                var historyMessageCount = request.HistoryMessageCount.HasValue
                    ? Math.Clamp(request.HistoryMessageCount.Value, 0, maxHistoryMessages)
                    : request.HistoryTurnCount.HasValue
                        ? Math.Clamp(request.HistoryTurnCount.Value, 0, maxHistoryTurns) * messagesPerTurn
                        : maxHistoryMessages;
                var runtimeOptions = new RagRuntimeOptions
                {
                    DisablePersistence = noSave,
                    SystemPromptOverride = request.PromptOverride?.SystemPrompt,
                    RulesPromptOverride = request.PromptOverride?.RulesPrompt,
                    LowSimilarityMessageOverride = request.PromptOverride?.LowSimilarityMessage,
                    SimilarityThresholdOverride = request.PromptOverride?.SimilarityThreshold
                };

                ChatSession? session = null;
                List<(string Role, string Content)> history = new();

                // 인라인 이력이 직접 전달된 경우 (SessionId 없을 때)
                if (request.History != null && request.History.Count > 0 && !request.SessionId.HasValue)
                {
                    history = request.History
                        .Where(h => !string.IsNullOrWhiteSpace(h.Role) && !string.IsNullOrWhiteSpace(h.Content))
                        .Select(h => (h.Role!.Trim(), h.Content!.Trim()))
                        .TakeLast(historyMessageCount)
                        .ToList();
                }

                if (request.SessionId.HasValue)
                {
                    session = await _context.ChatSessions.FindAsync(request.SessionId.Value);

                    if (session == null)
                        return NotFound("세션을 찾을 수 없습니다.");

                        if (historyMessageCount > 0)
                    {
                        history = await _context.ChatMessages
                            .AsNoTracking()
                            .Where(x => x.SessionId == request.SessionId.Value)
                            .OrderByDescending(x => x.CreatedAt)
                            .Take(historyMessageCount)
                            .OrderBy(x => x.CreatedAt)
                            .Select(x => new ValueTuple<string, string>(x.Role, x.Content))
                            .ToListAsync();
                    }
                }
                else if (!noSave && request.CreateSession == true)
                {
                    session = new ChatSession
                    {
                        Title = request.Question.Length > 50
                            ? request.Question[..50] + "..."
                            : request.Question,
                        UserRole = role,
                        ActorName = actorName,
                        Platform = platform,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.ChatSessions.Add(session);
                    await _context.SaveChangesAsync();
                }

                var result = await _ragService.SearchAndGenerateAsync(
                    request.Question,
                    role,
                    platform,
                    history,
                    runtimeOptions);

                if (!noSave && result.IsLowSimilarity)
                {
                    _context.LowSimilarityQuestions.Add(new LowSimilarityQuestion
                    {
                        Question = request.Question,
                        Role = role,
                        ActorName = actorName,
                        Platform = platform,
                        TopSimilarity = result.TopSimilarity,
                        TopMatchedEvidenceText = result.TopMatchedEvidenceText ?? result.RelatedKBs.FirstOrDefault()?.MatchedEvidenceText,
                        TopMatchedKbTitle = result.TopMatchedKbTitle,
                        TopMatchedKbContent = result.TopMatchedKbContent,
                        SessionId = session?.Id,
                        CreatedAt = DateTime.UtcNow,
                        IsResolved = false
                    });
                }

                if (!noSave && session != null)
                {
                    session.Platform = platform;
                    if (string.IsNullOrWhiteSpace(session.ActorName))
                    {
                        session.ActorName = actorName;
                    }
                    var diagnosticsById = (result.RetrievalDiagnostics?.Candidates ?? new List<RetrievalCandidateDiagnostic>())
                        .GroupBy(c => c.Id)
                        .ToDictionary(g => g.Key, g => g.First());

                    var relatedKbMeta = result.RelatedKBs
                        .GroupBy(k => k.Id)
                        .Select(g =>
                        {
                            var similarity = g.Max(x => x.Similarity);
                            var isSelected = g.Any(x => x.IsSelected);
                            diagnosticsById.TryGetValue(g.Key, out var cand);

                            return new
                            {
                                id = g.Key,
                                similarity,
                                isSelected,
                                matchedKeywords = cand?.MatchedKeywords ?? new List<string>(),
                                keywordMatchCount = cand?.KeywordMatchCount ?? 0,
                                baseSimilarity = cand?.BaseSimilarity ?? similarity,
                                keywordBoost = cand?.KeywordBoost ?? 0f
                            };
                        })
                        .ToList();
                    var relatedIds = relatedKbMeta.Where(x => x.isSelected).Select(x => x.id).ToList();
                    _context.ChatMessages.AddRange(
                        new ChatMessage
                        {
                            SessionId = session.Id,
                            Role = "user",
                            Content = request.Question,
                            CreatedAt = DateTime.UtcNow
                        },
                        new ChatMessage
                        {
                            SessionId = session.Id,
                            Role = "bot",
                            Content = result.Answer,
                            CreatedAt = DateTime.UtcNow,
                            RelatedKbIds = JsonSerializer.Serialize(relatedIds),
                            RelatedKbMeta = JsonSerializer.Serialize(relatedKbMeta),
                            RetrievalDebugMeta = JsonSerializer.Serialize(result.RetrievalDiagnostics),
                            TopSimilarity = result.TopSimilarity,
                            IsLowSimilarity = result.IsLowSimilarity
                        }
                    );
                    session.MessageCount += 2;
                    session.UpdatedAt = DateTime.UtcNow;
                }

                if (!noSave)
                {
                    await _context.SaveChangesAsync();
                }

                return Ok(new
                {
                    result.Answer,
                    result.TopSimilarity,
                    result.IsLowSimilarity,
                    result.TopMatchedEvidenceText,
                    result.TopMatchedKbTitle,
                    result.TopMatchedKbContent,
                    result.RelatedKBs,
                    result.ConflictDetected,
                    result.DecisionRule,
                    result.RetrievalDiagnostics,
                    sessionId = session?.Id
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ 질문 오류: {ex.Message}");

                var errorText = ex.ToString();
                if (errorText.Contains("invalid_api_key", StringComparison.OrdinalIgnoreCase)
                    || errorText.Contains("Incorrect API key", StringComparison.OrdinalIgnoreCase)
                    || errorText.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
                    || errorText.Contains("401", StringComparison.OrdinalIgnoreCase))
                {
                    return StatusCode(503, new { error = "OpenAI API 키가 유효하지 않거나 설정되지 않았습니다. 서버 환경변수(OpenAI__ApiKey)를 확인해주세요." });
                }

                return StatusCode(500, new { error = ex.Message });
            }
        }

        [Authorize(Roles = "admin")]
        [HttpPost]
        public async Task<IActionResult> CreateKnowledgeBase([FromBody] UpsertKbRequest request)
        {
            try
            {
                var validationError = ValidateRequest(request);
                if (!string.IsNullOrWhiteSpace(validationError))
                    return BadRequest(validationError);

                var content = ResolveContent(request);
                var expectedQuestions = ResolveExpectedQuestions(request);
                var now = DateTime.UtcNow;
                var actor = await ResolveActorNameAsync();
                var platforms = NormalizePlatforms(request.Platforms, request.Platform);

                var kb = new KnowledgeBase
                {
                    Title = request.Title!.Trim(),
                    Content = content,
                    Visibility = NormalizeVisibility(request.Visibility),
                    Platform = SerializePlatforms(platforms),
                    Keywords = request.Keywords?.Trim(),
                    CreatedAt = now,
                    UpdatedAt = now,
                    CreatedBy = actor,
                    UpdatedBy = actor
                };

                foreach (var q in expectedQuestions)
                {
                    kb.ExpectedQuestions.Add(await BuildExpectedQuestionAsync(q));
                }

                _context.KnowledgeBases.Add(kb);
                await _context.SaveChangesAsync();

                _context.KnowledgeBaseHistories.Add(BuildKbHistory(kb, actor, KbHistoryActionCreate));
                _context.KnowledgeBaseExpectedQuestionHistories.AddRange(
                    kb.ExpectedQuestions.Select(x => BuildExpectedQuestionHistory(kb.Id, actor, ExpectedQuestionHistoryActionAdd, null, x.Question)));
                await _context.SaveChangesAsync();

                try
                {
                    await _vectorSearchService.UpsertKnowledgeBaseAsync(kb);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ 벡터 인덱스 동기화 실패(등록). KB 저장은 완료됨. kbId={KbId}", kb.Id);
                }

                return Ok(new { id = kb.Id, message = "KB가 등록되었습니다." });
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ KB 등록 오류: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [Authorize(Roles = "admin")]
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateKnowledgeBase(int id, [FromBody] UpsertKbRequest request)
        {
            try
            {
                var validationError = ValidateRequest(request);
                if (!string.IsNullOrWhiteSpace(validationError))
                    return BadRequest(validationError);

                var kb = await _context.KnowledgeBases
                    .Include(x => x.ExpectedQuestions)
                    .FirstOrDefaultAsync(x => x.Id == id);

                if (kb == null)
                    return NotFound("KB를 찾을 수 없습니다.");

                var beforeTitle = kb.Title;
                var beforeContent = kb.Content;
                var beforeVisibility = kb.Visibility;
                var beforePlatform = kb.Platform;
                var beforeKeywords = kb.Keywords;
                var beforeExpectedQuestions = kb.ExpectedQuestions
                    .Where(x => !string.IsNullOrWhiteSpace(x.Question))
                    .Select(x => x.Question)
                    .ToList();

                var content = ResolveContent(request);
                var expectedQuestions = ResolveExpectedQuestions(request);

                var platforms = NormalizePlatforms(request.Platforms, request.Platform);
                var actor = await ResolveActorNameAsync();
                kb.Title = request.Title!.Trim();
                kb.Content = content;
                kb.Visibility = NormalizeVisibility(request.Visibility);
                kb.Platform = SerializePlatforms(platforms);
                kb.Keywords = request.Keywords?.Trim();
                kb.UpdatedAt = DateTime.UtcNow;
                kb.UpdatedBy = actor;

                var incoming = expectedQuestions;

                var existingByKey = kb.ExpectedQuestions
                    .GroupBy(x => NormalizeQuestionKey(x.Question))
                    .ToDictionary(g => g.Key, g => g.First());

                var incomingKeys = new HashSet<string>();

                foreach (var q in incoming)
                {
                    var key = NormalizeQuestionKey(q);
                    if (!incomingKeys.Add(key))
                    {
                        continue;
                    }

                    if (existingByKey.TryGetValue(key, out var existing))
                    {
                        // 표현(대소문자/공백)만 바뀐 경우 표시 문자열만 최신값으로 동기화
                        var beforeQuestion = existing.Question;
                        existing.Question = q;
                        if (!string.Equals(beforeQuestion, existing.Question, StringComparison.Ordinal))
                        {
                            _context.KnowledgeBaseExpectedQuestionHistories.Add(
                                BuildExpectedQuestionHistory(kb.Id, actor, ExpectedQuestionHistoryActionUpdate, beforeQuestion, existing.Question));
                        }
                        continue;
                    }

                    var newItem = await BuildExpectedQuestionAsync(q);
                    kb.ExpectedQuestions.Add(newItem);
                    _context.KnowledgeBaseExpectedQuestionHistories.Add(
                        BuildExpectedQuestionHistory(kb.Id, actor, ExpectedQuestionHistoryActionAdd, null, newItem.Question));
                }

                var toRemove = kb.ExpectedQuestions
                    .Where(x => !incomingKeys.Contains(NormalizeQuestionKey(x.Question)))
                    .ToList();

                if (toRemove.Count > 0)
                {
                    _context.KnowledgeBaseExpectedQuestionHistories.AddRange(
                        toRemove.Select(x => BuildExpectedQuestionHistory(kb.Id, actor, ExpectedQuestionHistoryActionRemove, x.Question, null)));
                    _context.KnowledgeBaseExpectedQuestions.RemoveRange(toRemove);
                }

                if (HasKbMetaChanges(
                        beforeTitle,
                        beforeContent,
                        beforeVisibility,
                        beforePlatform,
                        beforeKeywords,
                        kb.Title,
                        kb.Content,
                        kb.Visibility,
                        kb.Platform,
                        kb.Keywords))
                {
                    _context.KnowledgeBaseHistories.Add(BuildKbHistory(
                        kb,
                        actor,
                        KbHistoryActionUpdate,
                        beforeTitle,
                        beforeContent,
                        beforeVisibility,
                        beforePlatform,
                        beforeKeywords));
                }

                await _context.SaveChangesAsync();

                var incomingByKey = incoming
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .GroupBy(x => NormalizeQuestionKey(x))
                    .ToDictionary(g => g.Key, g => g.First());

                var staleExpectedQuestions = beforeExpectedQuestions
                    .Where(oldQuestion =>
                    {
                        var key = NormalizeQuestionKey(oldQuestion);
                        if (!incomingByKey.TryGetValue(key, out var currentQuestion))
                        {
                            return true;
                        }

                        return !string.Equals(oldQuestion, currentQuestion, StringComparison.Ordinal);
                    })
                    .ToList();

                try
                {
                    // 현재 상태를 먼저 업서트하고, 그 다음 구버전 예상질문 포인트만 정리한다.
                    await _vectorSearchService.UpsertKnowledgeBaseAsync(kb);

                    if (staleExpectedQuestions.Count > 0)
                    {
                        await _vectorSearchService.DeleteExpectedQuestionPointsAsync(kb.Id, staleExpectedQuestions);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ 벡터 인덱스 동기화 실패(수정). KB 저장은 완료됨. kbId={KbId}", kb.Id);
                }
                return Ok(new { id = kb.Id, message = "KB가 수정되었습니다." });
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ KB 수정 오류: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetAllKnowledgeBases(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? keyword = null,
            [FromQuery] string? visibility = null,
            [FromQuery] string? platform = null)
        {
            try
            {
                page = Math.Max(1, page);
                pageSize = Math.Clamp(pageSize, 1, 100);

                var query = _context.KnowledgeBases
                    .Include(x => x.ExpectedQuestions)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(visibility))
                {
                    var normalizedVisibility = visibility.Trim().ToLowerInvariant();
                    if (normalizedVisibility == "user" || normalizedVisibility == "admin")
                    {
                        query = query.Where(x => x.Visibility == normalizedVisibility);
                    }
                }

                if (!string.IsNullOrWhiteSpace(platform) && !string.Equals(platform.Trim(), "all", StringComparison.OrdinalIgnoreCase))
                {
                    var normalizedPlatform = NormalizePlatform(platform);
                    query = query.Where(x => x.Platform.Contains(normalizedPlatform));
                }

                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    var q = keyword.Trim();
                    // #101 또는 순수 숫자 입력 시 ID 검색
                    var idStr = q.StartsWith('#') ? q[1..] : q;
                    if (int.TryParse(idStr, out var kbIdFilter))
                    {
                        query = query.Where(x => x.Id == kbIdFilter);
                    }
                    else
                    {
                        query = query.Where(x =>
                            (x.Title != null && EF.Functions.Like(x.Title, $"%{q}%")) ||
                            EF.Functions.Like(x.Content, $"%{q}%") ||
                            (x.Keywords != null && EF.Functions.Like(x.Keywords, $"%{q}%")) ||
                            x.ExpectedQuestions.Any(s => EF.Functions.Like(s.Question, $"%{q}%")));
                    }
                }

                var total = await query.CountAsync();
                var kbs = await query
                    .OrderByDescending(x => x.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(x => new
                    {
                        x.Id,
                        x.Title,
                        content = x.Content,
                        x.CreatedAt,
                        x.UpdatedAt,
                        x.CreatedBy,
                        x.UpdatedBy,
                        x.ViewCount,
                        x.Visibility,
                        x.Platform,
                        platforms = SplitPlatforms(x.Platform),
                        keywords = x.Keywords,
                        expectedQuestions = x.ExpectedQuestions
                            .OrderBy(s => s.Id)
                            .Select(s => new { s.Id, s.Question })
                            .ToList()
                    })
                    .ToListAsync();

                return Ok(new { total, page, pageSize, data = kbs });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("generate-expected-questions")]
        public async Task<IActionResult> GenerateExpectedQuestions([FromBody] GenerateExpectedQuestionsRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Content))
                    return BadRequest(new { error = "내용을 먼저 작성해주세요." });

                var prompts = await _kbWriterPromptTemplates.GetAsync();
                var generated = await _knowledgeExtractorService.GenerateExpectedQuestionsAsync(
                    request.Title?.Trim() ?? string.Empty,
                    request.Content.Trim(),
                    Math.Clamp(request.Count ?? 5, 1, 5),
                    prompts.ExpectedQuestionSystemPrompt,
                    prompts.ExpectedQuestionRulesPrompt);

                return Ok(new { items = generated });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "예상 질문 생성 오류");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("generate-keywords")]
        public async Task<IActionResult> GenerateKeywords([FromBody] GenerateKeywordsRequest request)
        {
            try
            {
                var prompts = await _kbWriterPromptTemplates.GetAsync();
                var source = BuildKeywordGenerationSource(request.Title, request.Content, request.ExpectedQuestions);
                if (string.IsNullOrWhiteSpace(source))
                    return BadRequest(new { error = "제목 또는 내용을 먼저 입력해주세요" });

                var count = Math.Clamp(request.Count ?? 5, 1, 5);
                var generated = await _knowledgeExtractorService.GenerateKeywordsAsync(
                    source,
                    request.ExpectedQuestions,
                    count,
                    prompts.KeywordSystemPrompt,
                    prompts.KeywordRulesPrompt,
                    source: "document");

                return Ok(new { items = generated, combined = generated });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "키워드 생성 오류");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("refine-solution")]
        public async Task<IActionResult> RefineSolution([FromBody] RefineSolutionRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Content))
                    return BadRequest(new { error = "내용을 먼저 작성해주세요." });

                var prompts = await _kbWriterPromptTemplates.GetAsync();
                var refined = await _knowledgeExtractorService.RefineSolutionAsync(
                    request.Content.Trim(),
                    prompts.AnswerRefineSystemPrompt,
                    prompts.AnswerRefineRulesPrompt);

                return Ok(new { solution = refined });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "답변 정리 오류");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("recommend-title")]
        public async Task<IActionResult> RecommendTitle([FromBody] RecommendTitleRequest request)
        {
            return await RecommendTitleCoreAsync(request.Content, request.Count);
        }

        [HttpGet("recommend-title")]
        public async Task<IActionResult> RecommendTitleByQuery([FromQuery] string? content, [FromQuery] int? count)
        {
            return await RecommendTitleCoreAsync(content, count);
        }

        private async Task<IActionResult> RecommendTitleCoreAsync(string? content, int? count)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(content))
                    return BadRequest(new { error = "내용을 먼저 작성해주세요." });

                var prompts = await _kbWriterPromptTemplates.GetAsync();
                var items = await _knowledgeExtractorService.GenerateRecommendedTitlesAsync(
                    content.Trim(),
                    Math.Clamp(count ?? 3, 1, 5),
                    prompts.AnswerRefineSystemPrompt,
                    prompts.AnswerRefineRulesPrompt);

                return Ok(new
                {
                    title = items.FirstOrDefault() ?? string.Empty,
                    items
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "제목 추천 오류");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetKnowledgeBase(int id)
        {
            try
            {
                var kb = await _context.KnowledgeBases
                    .Include(x => x.ExpectedQuestions)
                    .FirstOrDefaultAsync(x => x.Id == id);

                if (kb == null)
                    return NotFound("KB를 찾을 수 없습니다.");

                return Ok(new
                {
                    kb.Id,
                    kb.Title,
                    content = kb.Content,
                    kb.CreatedAt,
                    kb.UpdatedAt,
                    kb.CreatedBy,
                    kb.UpdatedBy,
                    kb.ViewCount,
                    kb.Visibility,
                    kb.Platform,
                    platforms = SplitPlatforms(kb.Platform),
                    keywords = kb.Keywords,
                    expectedQuestions = kb.ExpectedQuestions
                        .OrderBy(s => s.Id)
                        .Select(s => new { s.Id, s.Question })
                        .ToList()
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [Authorize(Roles = "admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteKnowledgeBase(int id)
        {
            try
            {
                var kb = await _context.KnowledgeBases.FindAsync(id);
                if (kb == null)
                    return NotFound("KB를 찾을 수 없습니다.");

                var actor = await ResolveActorNameAsync();

                _context.KnowledgeBaseHistories.Add(BuildKbHistory(kb, actor, KbHistoryActionDelete));
                var questions = await _context.KnowledgeBaseExpectedQuestions
                    .Where(x => x.KnowledgeBaseId == id)
                    .Select(x => x.Question)
                    .ToListAsync();
                _context.KnowledgeBaseExpectedQuestionHistories.AddRange(
                    questions.Select(x => BuildExpectedQuestionHistory(id, actor, ExpectedQuestionHistoryActionRemove, x, null)));

                _context.KnowledgeBases.Remove(kb);
                await _context.SaveChangesAsync();
                try
                {
                    await _vectorSearchService.DeleteKnowledgeBaseAsync(id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ 벡터 인덱스 삭제 실패. KB 삭제는 완료됨. kbId={KbId}", id);
                }

                return Ok(new { message = "KB가 삭제되었습니다." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("{id}/history")]
        public async Task<IActionResult> GetKnowledgeBaseHistory(int id)
        {
            var exists = await _context.KnowledgeBases.AnyAsync(x => x.Id == id);
            if (!exists)
            {
                var hasHistory = await _context.KnowledgeBaseHistories.AnyAsync(x => x.KnowledgeBaseId == id)
                    || await _context.KnowledgeBaseExpectedQuestionHistories.AnyAsync(x => x.KnowledgeBaseId == id);

                if (!hasHistory)
                    return NotFound("KB를 찾을 수 없습니다.");
            }

            var kbHistory = await _context.KnowledgeBaseHistories
                .AsNoTracking()
                .Where(x => x.KnowledgeBaseId == id)
                .OrderByDescending(x => x.ChangedAt)
                .Select(x => new
                {
                    x.Id,
                    x.Action,
                    x.Actor,
                    x.ChangedAt,
                    before = new
                    {
                        title = x.BeforeTitle,
                        content = x.BeforeContent,
                        visibility = x.BeforeVisibility,
                        platform = x.BeforePlatform,
                        keywords = x.BeforeKeywords
                    },
                    after = new
                    {
                        title = x.AfterTitle,
                        content = x.AfterContent,
                        visibility = x.AfterVisibility,
                        platform = x.AfterPlatform,
                        keywords = x.AfterKeywords
                    }
                })
                .ToListAsync();

            var expectedQuestionHistory = await _context.KnowledgeBaseExpectedQuestionHistories
                .AsNoTracking()
                .Where(x => x.KnowledgeBaseId == id)
                .OrderByDescending(x => x.ChangedAt)
                .Select(x => new
                {
                    x.Id,
                    x.Action,
                    x.Actor,
                    x.ChangedAt,
                    beforeQuestion = x.BeforeQuestion,
                    afterQuestion = x.AfterQuestion
                })
                .ToListAsync();

            return Ok(new
            {
                kbId = id,
                kbHistory,
                expectedQuestionHistory
            });
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            try
            {
                var totalKBs = await _context.KnowledgeBases.CountAsync();
                var totalViews = await _context.KnowledgeBases.SumAsync(x => x.ViewCount);

                var byVisibility = await _context.KnowledgeBases
                    .GroupBy(x => x.Visibility)
                    .Select(g => new { visibility = g.Key, count = g.Count() })
                    .ToListAsync();

                var byPlatform = await _context.KnowledgeBases
                    .GroupBy(x => x.Platform)
                    .Select(g => new { platform = g.Key, count = g.Count() })
                    .ToListAsync();

                return Ok(new { totalKBs, totalViews, byVisibility, byPlatform });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [Authorize(Roles = "admin")]
        [HttpPost("rebuild-vector-index")]
        public async Task<IActionResult> RebuildVectorIndex(CancellationToken cancellationToken)
        {
            try
            {
                var totalKbCount = await _context.KnowledgeBases.CountAsync(cancellationToken);
                await _vectorSearchService.RebuildAllKnowledgeBasesAsync(cancellationToken);

                return Ok(new
                {
                    message = "벡터 인덱스를 초기화하고 전체 KB를 다시 임베딩했습니다.",
                    totalKbCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 벡터 인덱스 재구축 실패");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("chatbot-prompt-template")]
        public IActionResult GetChatbotPromptTemplate()
        {
            return Ok(new ChatbotPromptTemplateResponse
            {
                UserSystemPrompt = _chatbotPromptTemplates.UserSystemPrompt,
                AdminSystemPrompt = _chatbotPromptTemplates.AdminSystemPrompt,
                UserRulesPrompt = _chatbotPromptTemplates.UserRulesPrompt,
                AdminRulesPrompt = _chatbotPromptTemplates.AdminRulesPrompt,
                UserLowSimilarityMessage = _chatbotPromptTemplates.UserLowSimilarityMessage,
                AdminLowSimilarityMessage = _chatbotPromptTemplates.AdminLowSimilarityMessage,
                SimilarityThreshold = _chatbotPromptTemplates.SimilarityThreshold
            });
        }

        [HttpGet("writer-prompt-template")]
        public async Task<IActionResult> GetWriterPromptTemplate()
        {
            var template = await _kbWriterPromptTemplates.GetAsync();
            return Ok(new KnowledgeBaseWriterPromptTemplateResponse
            {
                KeywordSystemPrompt = template.KeywordSystemPrompt,
                KeywordRulesPrompt = template.KeywordRulesPrompt,
                ExpectedQuestionSystemPrompt = template.ExpectedQuestionSystemPrompt,
                ExpectedQuestionRulesPrompt = template.ExpectedQuestionRulesPrompt,
                TopicKeywordSystemPrompt = template.TopicKeywordSystemPrompt,
                TopicKeywordRulesPrompt = template.TopicKeywordRulesPrompt,
                AnswerRefineSystemPrompt = template.AnswerRefineSystemPrompt,
                AnswerRefineRulesPrompt = template.AnswerRefineRulesPrompt
            });
        }

        [Authorize(Roles = "admin")]
        [HttpPut("writer-prompt-template")]
        public async Task<IActionResult> UpdateWriterPromptTemplate([FromBody] UpdateKnowledgeBaseWriterPromptTemplateRequest request)
        {
            try
            {
                var topicSystemPrompt = string.IsNullOrWhiteSpace(request.TopicKeywordSystemPrompt)
                    ? request.KeywordSystemPrompt
                    : request.TopicKeywordSystemPrompt;
                var topicRulesPrompt = string.IsNullOrWhiteSpace(request.TopicKeywordRulesPrompt)
                    ? request.KeywordRulesPrompt
                    : request.TopicKeywordRulesPrompt;

                var template = await _kbWriterPromptTemplates.UpdateAsync(
                    request.KeywordSystemPrompt,
                    request.KeywordRulesPrompt,
                    request.ExpectedQuestionSystemPrompt,
                    request.ExpectedQuestionRulesPrompt,
                    topicSystemPrompt,
                    topicRulesPrompt,
                    request.AnswerRefineSystemPrompt,
                    request.AnswerRefineRulesPrompt);

                return Ok(new KnowledgeBaseWriterPromptTemplateResponse
                {
                    KeywordSystemPrompt = template.KeywordSystemPrompt,
                    KeywordRulesPrompt = template.KeywordRulesPrompt,
                    ExpectedQuestionSystemPrompt = template.ExpectedQuestionSystemPrompt,
                    ExpectedQuestionRulesPrompt = template.ExpectedQuestionRulesPrompt,
                    TopicKeywordSystemPrompt = template.TopicKeywordSystemPrompt,
                    TopicKeywordRulesPrompt = template.TopicKeywordRulesPrompt,
                    AnswerRefineSystemPrompt = template.AnswerRefineSystemPrompt,
                    AnswerRefineRulesPrompt = template.AnswerRefineRulesPrompt
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [Authorize(Roles = "admin")]
        [HttpPut("chatbot-prompt-template")]
        public IActionResult UpdateChatbotPromptTemplate([FromBody] UpdateChatbotPromptTemplateRequest request)
        {
            try
            {
                _chatbotPromptTemplates.Update(
                    request.UserSystemPrompt,
                    request.AdminSystemPrompt,
                    request.UserRulesPrompt,
                    request.AdminRulesPrompt,
                    request.UserLowSimilarityMessage,
                    request.AdminLowSimilarityMessage,
                    request.SimilarityThreshold);

                return Ok(new ChatbotPromptTemplateResponse
                {
                    UserSystemPrompt = _chatbotPromptTemplates.UserSystemPrompt,
                    AdminSystemPrompt = _chatbotPromptTemplates.AdminSystemPrompt,
                    UserRulesPrompt = _chatbotPromptTemplates.UserRulesPrompt,
                    AdminRulesPrompt = _chatbotPromptTemplates.AdminRulesPrompt,
                    UserLowSimilarityMessage = _chatbotPromptTemplates.UserLowSimilarityMessage,
                    AdminLowSimilarityMessage = _chatbotPromptTemplates.AdminLowSimilarityMessage,
                    SimilarityThreshold = _chatbotPromptTemplates.SimilarityThreshold
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("low-similarity-questions")]
        public async Task<IActionResult> GetLowSimilarityQuestions(
            [FromQuery] bool includeResolved = false,
            [FromQuery] string? platform = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _context.LowSimilarityQuestions.AsQueryable();
            if (!includeResolved)
            {
                query = query.Where(x => !x.IsResolved);
            }
            if (!string.IsNullOrWhiteSpace(platform))
            {
                var normalizedPlatform = NormalizePlatform(platform);
                if (!string.Equals(normalizedPlatform, "전체 플랫폼", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(x => x.Platform == normalizedPlatform);
                }
            }

            var total = await query.CountAsync();

            var items = await query
                .OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var data = new List<object>(items.Count);
            foreach (var item in items)
            {
                int? matchedSessionId = item.SessionId;
                var actorName = string.IsNullOrWhiteSpace(item.ActorName) ? null : item.ActorName;

                if (!matchedSessionId.HasValue)
                {
                    var fallbackSessionId = await _context.ChatMessages
                        .AsNoTracking()
                        .Where(m => m.Role == "user" && m.Content == item.Question)
                        .Join(
                            _context.ChatSessions.AsNoTracking(),
                            m => m.SessionId,
                            s => s.Id,
                            (m, s) => new { m.SessionId, m.CreatedAt, s.UserRole, s.Platform })
                        .Where(x => x.UserRole == item.Role && x.Platform == item.Platform)
                        .OrderByDescending(x => x.CreatedAt)
                        .Select(x => x.SessionId)
                        .FirstOrDefaultAsync();

                    if (fallbackSessionId > 0)
                    {
                        matchedSessionId = fallbackSessionId;
                    }
                }

                if (string.IsNullOrWhiteSpace(actorName) && matchedSessionId.HasValue)
                {
                    actorName = await _context.ChatSessions
                        .AsNoTracking()
                        .Where(x => x.Id == matchedSessionId.Value)
                        .Select(x => x.ActorName)
                        .FirstOrDefaultAsync();
                }

                data.Add(new
                {
                    item.Id,
                    item.Question,
                    item.Role,
                    ActorName = string.IsNullOrWhiteSpace(actorName) ? "알 수 없음" : actorName,
                    item.Platform,
                    item.TopSimilarity,
                    item.TopMatchedEvidenceText,
                    item.TopMatchedKbTitle,
                    item.TopMatchedKbContent,
                    item.CreatedAt,
                    item.IsResolved,
                    item.ResolvedAt,
                    item.SessionId,
                    MatchedSessionId = matchedSessionId
                });
            }

            return Ok(new { total, page, pageSize, data });
        }

        [Authorize(Roles = "admin")]
        [HttpPut("low-similarity-questions/{id}/resolve")]
        public async Task<IActionResult> ResolveLowSimilarityQuestion(int id)
        {
            var item = await _context.LowSimilarityQuestions.FindAsync(id);
            if (item == null) return NotFound();

            item.IsResolved = true;
            item.ResolvedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { item.Id, item.IsResolved, item.ResolvedAt });
        }

        // ─── 전체 재임베딩 ──────────────────────────────────────────────────────
        [Authorize(Roles = "admin")]
        [HttpPost("reindex-all")]
        public async Task<IActionResult> ReindexAll(CancellationToken cancellationToken)
        {
            try
            {
                var totalKbCount = await _context.KnowledgeBases.CountAsync(cancellationToken);
                await _vectorSearchService.RebuildAllKnowledgeBasesAsync(cancellationToken);

                return Ok(new
                {
                    message = "벡터 인덱스를 초기화하고 전체 KB를 다시 임베딩했습니다.",
                    totalKbCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 벡터 인덱스 재구축 실패");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // ─── CSV 템플릿 다운로드 ──────────────────────────────────────────────
        [HttpGet("bulk-import/template")]
        public IActionResult DownloadBulkImportTemplate()
        {
            var header = "공개수준,제목,내용,키워드(|구분),플랫폼(|구분),예상질문1,예상질문2,예상질문3,예상질문4,예상질문5,예상질문6,예상질문7,예상질문8,예상질문9,예상질문10";
            var example = "user,인증서 오류 해결 방법,인증서가 만료되었을 때는 갱신을 진행하세요.,인증서|갱신|SSL,공통|windows,인증서가 안 보여요,인증서 어디서 받아요,SSL 오류가 나요,,,,,,,";
            var csv = $"{header}\n{example}\n";
            var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray();
            return File(bytes, "text/csv; charset=utf-8", "kb-import-template.csv");
        }

        // ─── CSV 대량 등록 ────────────────────────────────────────────────────
        [Authorize(Roles = "admin")]
        [HttpPost("bulk-import")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> BulkImport(IFormFile file, CancellationToken cancellationToken)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "CSV 파일을 첨부해주세요." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".csv")
                return BadRequest(new { error = "CSV 파일(.csv)만 지원합니다." });

            var actor = await ResolveActorNameAsync();
            
            // 활성 플랫폼 목록 로드
            var activePlatforms = await _context.KbPlatforms
                .Where(x => x.IsActive)
                .Select(x => x.Name)
                .ToListAsync(cancellationToken);
            
            var results = new List<object>();
            var successCount = 0;
            var failCount = 0;

            // 전체 스트림을 읽어 멀티라인 quoted 필드를 올바르게 파싱
            using var reader = new System.IO.StreamReader(file.OpenReadStream(), System.Text.Encoding.UTF8);
            var allText = await reader.ReadToEndAsync(cancellationToken);
            var csvRows = ParseCsvRows(allText);
            var rowIndex = 0;

            foreach (var cols in csvRows)
            {
                rowIndex++;
                // 헤더 행 스킵
                if (rowIndex == 1) continue;
                // 빈 행 스킵
                if (cols.Count == 0 || cols.All(c => string.IsNullOrWhiteSpace(c))) continue;


                if (cols.Count < 3)
                {
                    results.Add(new { row = rowIndex, status = "skip", reason = "컬럼 부족(최소: 공개수준, 제목, 내용)" });
                    failCount++;
                    continue;
                }

                var visibility = cols[0].Trim();
                var title = cols[1].Trim();
                var content = cols[2].Trim();
                var keywordsRaw = cols.Count > 3 ? cols[3].Trim() : "";
                var platformsRaw = cols.Count > 4 ? cols[4].Trim() : "";
                var expectedQuestions = new List<string>();
                for (var i = 5; i < Math.Min(cols.Count, 15); i++)
                {
                    var q = cols[i].Trim();
                    if (!string.IsNullOrWhiteSpace(q)) expectedQuestions.Add(q);
                }

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
                {
                    results.Add(new { row = rowIndex, status = "skip", reason = "제목 또는 내용이 비어 있음", title });
                    failCount++;
                    continue;
                }

                var request = new UpsertKbRequest
                {
                    Title = title,
                    Content = content,
                    Visibility = visibility,
                    Platforms = platformsRaw.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList(),
                    Keywords = keywordsRaw.Replace('|', ','),
                    ExpectedQuestions = expectedQuestions
                };

                var validationError = ValidateRequest(request, activePlatforms);
                if (!string.IsNullOrWhiteSpace(validationError))
                {
                    results.Add(new { row = rowIndex, status = "fail", reason = validationError, title });
                    failCount++;
                    continue;
                }

                try
                {
                    var resolvedContent = ResolveContent(request);
                    var resolvedQuestions = ResolveExpectedQuestions(request);
                    var now = DateTime.UtcNow;
                    var platforms = NormalizePlatforms(request.Platforms, request.Platform);

                    var kb = new KnowledgeBase
                    {
                        Title = request.Title!.Trim(),
                        Content = resolvedContent,
                        Visibility = NormalizeVisibility(request.Visibility),
                        Platform = SerializePlatforms(platforms),
                        Keywords = request.Keywords?.Trim(),
                        CreatedAt = now,
                        UpdatedAt = now,
                        CreatedBy = actor,
                        UpdatedBy = actor
                    };

                    foreach (var q in resolvedQuestions)
                        kb.ExpectedQuestions.Add(await BuildExpectedQuestionAsync(q));

                    _context.KnowledgeBases.Add(kb);
                    await _context.SaveChangesAsync();

                    _context.KnowledgeBaseHistories.Add(BuildKbHistory(kb, actor, KbHistoryActionCreate));
                    _context.KnowledgeBaseExpectedQuestionHistories.AddRange(
                        kb.ExpectedQuestions.Select(x => BuildExpectedQuestionHistory(kb.Id, actor, ExpectedQuestionHistoryActionAdd, null, x.Question)));
                    await _context.SaveChangesAsync();

                    try { await _vectorSearchService.UpsertKnowledgeBaseAsync(kb); }
                    catch (Exception ex) { _logger.LogWarning(ex, "⚠️ 벡터 동기화 실패(bulk). kbId={Id}", kb.Id); }

                    results.Add(new { row = rowIndex, status = "ok", id = kb.Id, title });
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ bulk-import 행 오류 row={Row}", rowIndex);
                    results.Add(new { row = rowIndex, status = "fail", reason = ex.Message, title });
                    failCount++;
                }
            }

            return Ok(new { successCount, failCount, total = successCount + failCount, results });
        }

        /// <summary>전체 CSV 텍스트를 파싱하여 멀티라인 quoted 필드를 올바르게 처리합니다.</summary>
        private static List<List<string>> ParseCsvRows(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var sb = new System.Text.StringBuilder();
            var inQuotes = false;
            var i = 0;
            // BOM 제거
            if (text.Length > 0 && text[0] == '\uFEFF') i = 1;

            while (i < text.Length)
            {
                var c = text[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        sb.Append('"');
                        i += 2;
                        continue;
                    }
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    row.Add(sb.ToString());
                    sb.Clear();
                }
                else if ((c == '\r' || c == '\n') && !inQuotes)
                {
                    // \r\n 처리
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(sb.ToString());
                    sb.Clear();
                    if (row.Count > 0) rows.Add(row);
                    row = new List<string>();
                }
                else
                {
                    sb.Append(c);
                }
                i++;
            }
            // 마지막 행 처리
            row.Add(sb.ToString());
            if (row.Any(c => !string.IsNullOrWhiteSpace(c))) rows.Add(row);

            return rows;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            var sb = new System.Text.StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
            result.Add(sb.ToString());
            return result;
        }

        [AllowAnonymous] // 챗봇 플랫폼 드롭다운에 비로그인 접근 허용
        [HttpGet("platforms")]
        public async Task<IActionResult> GetPlatforms()
        {
            var catalogList = await _context.KbPlatforms
                .Where(x => x.IsActive)
                .OrderBy(x => x.CreatedAt)
                .Select(x => x.Name)
                .ToListAsync();

            var kbList = await _context.KnowledgeBases
                .Select(x => x.Platform)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToListAsync();

            var kbPlatforms = kbList
                .SelectMany(SplitPlatforms)
                .ToList();

            var normalized = catalogList
                .Concat(kbPlatforms)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizePlatform)
                .Distinct()
                .ToList();

            if (!normalized.Contains("공통")) normalized.Insert(0, "공통");

            var ordered = normalized
                .OrderBy(x => x == "공통" ? 0 : 1)
                .ThenBy(x => catalogList.FindIndex(c => NormalizePlatform(c) == x) is var idx && idx >= 0 ? idx : int.MaxValue)
                .ThenBy(x => x)
                .ToList();

            return Ok(ordered);
        }

        [HttpPost("platforms")]
        public async Task<IActionResult> AddPlatform([FromBody] AddPlatformRequest request)
        {
            var name = NormalizePlatform(request.Name);
            if (name.Length < 2)
                return BadRequest(new { error = "플랫폼명은 2자 이상이어야 합니다." });

            var exists = await _context.KbPlatforms.FirstOrDefaultAsync(x => x.Name == name);
            if (exists != null)
            {
                if (!exists.IsActive)
                {
                    exists.IsActive = true;
                    await _context.SaveChangesAsync();
                }
                return Ok(new { name = exists.Name });
            }

            _context.KbPlatforms.Add(new KbPlatform
            {
                Name = name,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            return Ok(new { name });
        }

        [HttpPut("platforms/{name}")]
        public async Task<IActionResult> UpdatePlatform(string name, [FromBody] UpdatePlatformRequest request)
        {
            var oldName = NormalizePlatform(name);
            var newName = NormalizePlatform(request.NewName);

            if (oldName == "공통")
                return BadRequest(new { error = "기본 플랫폼(공통)은 수정할 수 없습니다." });
            if (newName.Length < 2)
                return BadRequest(new { error = "플랫폼명은 2자 이상이어야 합니다." });

            var target = await _context.KbPlatforms.FirstOrDefaultAsync(x => x.Name == oldName && x.IsActive);
            if (target == null)
                return NotFound(new { error = "플랫폼을 찾을 수 없습니다." });

            if (oldName == newName)
                return Ok(new { name = oldName });

            var duplicate = await _context.KbPlatforms.FirstOrDefaultAsync(x => x.Name == newName && x.IsActive);
            if (duplicate != null)
                return BadRequest(new { error = "이미 존재하는 플랫폼명입니다." });

            target.Name = newName;

            var kbs = await _context.KnowledgeBases.ToListAsync();
            foreach (var kb in kbs)
            {
                if (!ContainsPlatform(kb.Platform, oldName)) continue;
                kb.Platform = ReplacePlatform(kb.Platform, oldName, newName);
            }

            var lowItems = await _context.LowSimilarityQuestions.Where(x => x.Platform == oldName).ToListAsync();
            foreach (var item in lowItems)
            {
                item.Platform = newName;
            }

            var sessions = await _context.ChatSessions.Where(x => x.Platform == oldName).ToListAsync();
            foreach (var s in sessions)
            {
                s.Platform = newName;
            }

            await _context.SaveChangesAsync();
            return Ok(new { name = newName });
        }

        [HttpDelete("platforms/{name}")]
        public async Task<IActionResult> DeletePlatform(string name)
        {
            var normalized = NormalizePlatform(name);
            if (normalized == "공통")
                return BadRequest(new { error = "기본 플랫폼(공통)은 삭제할 수 없습니다." });

            var target = await _context.KbPlatforms.FirstOrDefaultAsync(x => x.Name == normalized && x.IsActive);
            if (target == null)
                return NotFound(new { error = "플랫폼을 찾을 수 없습니다." });

            // 플랫폼 관련 데이터 전체 삭제
            var kbs = (await _context.KnowledgeBases.ToListAsync())
                .Where(x => ContainsPlatform(x.Platform, normalized))
                .ToList();
            if (kbs.Count > 0)
            {
                var deleteIds = kbs.Select(x => x.Id).ToList();
                _context.KnowledgeBases.RemoveRange(kbs);
                foreach (var kbId in deleteIds)
                {
                    try
                    {
                        await _vectorSearchService.DeleteKnowledgeBaseAsync(kbId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ 벡터 인덱스 삭제 실패(플랫폼 삭제). kbId={KbId}", kbId);
                    }
                }
            }

            var lowItems = await _context.LowSimilarityQuestions.Where(x => x.Platform == normalized).ToListAsync();
            if (lowItems.Count > 0)
            {
                _context.LowSimilarityQuestions.RemoveRange(lowItems);
            }

            var sessions = await _context.ChatSessions.Where(x => x.Platform == normalized).ToListAsync();
            if (sessions.Count > 0)
            {
                _context.ChatSessions.RemoveRange(sessions);
            }

            _context.KbPlatforms.Remove(target);

            await _context.SaveChangesAsync();
            return Ok(new { name = normalized, deleted = true });
        }

        private static string? ValidateRequest(UpsertKbRequest request)
        {
            return ValidateRequest(request, null);
        }

        private static string? ValidateRequest(UpsertKbRequest request, List<string>? activePlatforms)
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return "제목을 입력해주세요.";
            if (request.Title.Trim().Length > 200)
                return "제목은 최대 200자까지 입력 가능합니다.";
            if (string.IsNullOrWhiteSpace(ResolveContent(request)))
                return "내용을 입력해주세요.";
            if (!string.IsNullOrWhiteSpace(request.Visibility)
                && !new[] { "admin", "user", "common", "internal" }
                    .Contains(request.Visibility.Trim().ToLowerInvariant()))
                return "공개수준은 admin 또는 user 여야 합니다.";
            var platforms = NormalizePlatforms(request.Platforms, request.Platform);
            if (platforms.Any(x => x.Length < 2 && x != "공통"))
                return "플랫폼명은 2자 이상이어야 합니다.";

            var serializedPlatforms = SerializePlatforms(platforms);
            if (serializedPlatforms.Length > 50)
                return "플랫폼 값이 너무 깁니다. 플랫폼 선택 수를 줄이거나 이름을 짧게 해주세요.";

            if (!string.IsNullOrWhiteSpace(request.Keywords) && request.Keywords.Trim().Length > 500)
                return "키워드는 최대 500자까지 입력 가능합니다.";
            var keywords = ResolveKeywords(request.Keywords);
            if (keywords.Count > MaxKeywords)
                return $"키워드는 최대 {MaxKeywords}개까지 등록 가능합니다.";

            var expectedQuestions = ResolveExpectedQuestions(request);
            if (expectedQuestions.Any(x => x.Length > 500))
                return "예상질문 항목은 각각 최대 500자까지 입력 가능합니다.";
            
            // 활성 플랫폼 목록이 제공된 경우 플랫폼 유효성 검사
            if (activePlatforms != null && activePlatforms.Count > 0)
            {
                foreach (var platform in platforms)
                {
                    if (!activePlatforms.Contains(platform))
                        return $"플랫폼 '{platform}'이(가) 존재하지 않습니다. 먼저 플랫폼을 생성해주세요.";
                }
            }
            
            if (expectedQuestions.Count > 10)
                return "예상질문은 최대 10개까지 등록 가능합니다.";
            return null;
        }

        private static string ResolveContent(UpsertKbRequest request)
        {
            return (request.Content ?? string.Empty).Trim();
        }

        private static List<string> ResolveExpectedQuestions(UpsertKbRequest request)
        {
            var source = request.ExpectedQuestions;
            return (source ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();
        }

        private static List<string> ResolveKeywords(string? keywordsRaw)
        {
            if (string.IsNullOrWhiteSpace(keywordsRaw))
            {
                return new List<string>();
            }

            return keywordsRaw
                .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
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

        private static string BuildKeywordGenerationSource(string? title, string? content, IEnumerable<string>? expectedQuestions)
        {
            var bodySource = BuildKbBodyEmbeddingSource(title, content);
            var expected = (expectedQuestions ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();

            if (expected.Count == 0) return bodySource;
            if (string.IsNullOrWhiteSpace(bodySource)) return "예상질문: " + string.Join(" | ", expected);
            return $"{bodySource}\n예상질문: {string.Join(" | ", expected)}";
        }

        private async Task<KnowledgeBaseExpectedQuestion> BuildExpectedQuestionAsync(string question)
        {
            var trimmed = question.Trim();

            return new KnowledgeBaseExpectedQuestion
            {
                Question = trimmed,
                CreatedAt = DateTime.UtcNow
            };
        }

        private static string FormatActorName(string? name, string? loginId)
        {
            name = name?.Trim();
            loginId = loginId?.Trim();
            string value;
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(loginId))
                value = $"{name}({loginId})";
            else
                value = !string.IsNullOrEmpty(name) ? name
                : !string.IsNullOrEmpty(loginId) ? loginId
                : "알 수 없음";

            // KnowledgeBase.CreatedBy/UpdatedBy, ChatSession.ActorName 최대 길이(100)에 맞춤
            return value.Length > 100 ? value[..100] : value;
        }

        private async Task<string> ResolveActorNameAsync(string? bodyUsername = null, string? bodyLoginId = null)
        {
            // 1. 외부 위젯이 body로 전달한 값 우선 (JSON body는 UTF-8이라 한글 안전)
            var bName = bodyUsername?.Trim();
            var bId = bodyLoginId?.Trim();
            if (!string.IsNullOrEmpty(bName) || !string.IsNullOrEmpty(bId))
                return FormatActorName(bName, bId);

            // 2. JWT → DB에서 Username + LoginId 조회
            var userIdRaw = User?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User?.FindFirstValue("sub");

            if (int.TryParse(userIdRaw, out var userId) && userId > 0)
            {
                var userMeta = await _context.Users
                    .AsNoTracking()
                    .Where(x => x.Id == userId)
                    .Select(x => new { x.Username, x.LoginId })
                    .FirstOrDefaultAsync();

                if (userMeta != null)
                    return FormatActorName(userMeta.Username, userMeta.LoginId);
            }

            // 3. X-Actor-Name 헤더 fallback
            if (Request.Headers.TryGetValue("X-Actor-Name", out var headerActor)
                && !string.IsNullOrWhiteSpace(headerActor.ToString()))
            {
                var actor = headerActor.ToString().Trim();
                return actor.Length > 100 ? actor[..100] : actor;
            }

            return "알 수 없음";
        }

        private static string NormalizeVisibility(string? value)
        {
            if (string.Equals(value, "user", StringComparison.OrdinalIgnoreCase)) return "user";
            if (string.Equals(value, "common", StringComparison.OrdinalIgnoreCase)) return "user";
            return "admin";
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

        private static List<string> NormalizePlatforms(IEnumerable<string>? values, string? fallback)
        {
            var merged = new List<string>();
            if (values != null)
            {
                merged.AddRange(values);
            }
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                merged.Add(fallback);
            }

            var normalized = merged
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizePlatform)
                .Where(x => !string.Equals(x, "전체 플랫폼", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();

            if (normalized.Count == 0)
            {
                return new List<string> { "공통" };
            }

            if (normalized.Count > 1 && normalized.Contains("공통"))
            {
                normalized.Remove("공통");
            }

            return normalized;
        }

        private static List<string> SplitPlatforms(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new List<string> { "공통" };

            var parsed = value
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizePlatform)
                .Where(x => !string.Equals(x, "전체 플랫폼", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();

            return parsed.Count == 0 ? new List<string> { "공통" } : parsed;
        }

        private static string NormalizeQuestionKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return value.Trim().ToLowerInvariant();
        }

        private static string SerializePlatforms(IEnumerable<string> platforms)
        {
            return string.Join(",", platforms
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizePlatform)
                .Where(x => !string.Equals(x, "전체 플랫폼", StringComparison.OrdinalIgnoreCase))
                .Distinct());
        }

        private static bool ContainsPlatform(string? serialized, string platform)
        {
            var normalized = NormalizePlatform(platform);
            return SplitPlatforms(serialized).Contains(normalized);
        }

        private static bool HasKbMetaChanges(
            string? beforeTitle,
            string? beforeContent,
            string? beforeVisibility,
            string? beforePlatform,
            string? beforeKeywords,
            string? afterTitle,
            string? afterContent,
            string? afterVisibility,
            string? afterPlatform,
            string? afterKeywords)
        {
            return !string.Equals(beforeTitle, afterTitle, StringComparison.Ordinal)
                || !string.Equals(beforeContent, afterContent, StringComparison.Ordinal)
                || !string.Equals(beforeVisibility, afterVisibility, StringComparison.Ordinal)
                || !string.Equals(beforePlatform, afterPlatform, StringComparison.Ordinal)
                || !string.Equals(beforeKeywords, afterKeywords, StringComparison.Ordinal);
        }

        private static KnowledgeBaseHistory BuildKbHistory(
            KnowledgeBase kb,
            string actor,
            string action,
            string? beforeTitle = null,
            string? beforeContent = null,
            string? beforeVisibility = null,
            string? beforePlatform = null,
            string? beforeKeywords = null)
        {
            return new KnowledgeBaseHistory
            {
                KnowledgeBaseId = kb.Id,
                Action = action,
                Actor = actor,
                ChangedAt = DateTime.UtcNow,
                BeforeTitle = beforeTitle,
                BeforeContent = beforeContent,
                BeforeVisibility = beforeVisibility,
                BeforePlatform = beforePlatform,
                BeforeKeywords = beforeKeywords,
                AfterTitle = kb.Title,
                AfterContent = kb.Content,
                AfterVisibility = kb.Visibility,
                AfterPlatform = kb.Platform,
                AfterKeywords = kb.Keywords
            };
        }

        private static KnowledgeBaseExpectedQuestionHistory BuildExpectedQuestionHistory(
            int kbId,
            string actor,
            string action,
            string? beforeQuestion,
            string? afterQuestion)
        {
            return new KnowledgeBaseExpectedQuestionHistory
            {
                KnowledgeBaseId = kbId,
                Action = action,
                Actor = actor,
                ChangedAt = DateTime.UtcNow,
                BeforeQuestion = beforeQuestion,
                AfterQuestion = afterQuestion
            };
        }

        private static string ReplacePlatform(string? serialized, string oldPlatform, string newPlatform)
        {
            var oldNormalized = NormalizePlatform(oldPlatform);
            var newNormalized = NormalizePlatform(newPlatform);
            var items = SplitPlatforms(serialized);
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] == oldNormalized)
                {
                    items[i] = newNormalized;
                }
            }
            return SerializePlatforms(items);
        }

    }

    public class AskRequest
    {
        public string? Question { get; set; }
        public string? Role { get; set; }
        public string? Platform { get; set; }
        public int? SessionId { get; set; }
        public bool? CreateSession { get; set; }
        public bool? NoSave { get; set; }
        /// <summary>최근 대화 이력을 메시지 개수 기준으로 지정 (기본 6, 최대 6)</summary>
        public int? HistoryMessageCount { get; set; }
        /// <summary>레거시 호환용 턴 개수(기본 3, 최대 3). 내부에서는 1턴=2메시지로 변환됨</summary>
        public int? HistoryTurnCount { get; set; }
        /// <summary>인라인 대화 이력 (세션 없이 직접 전달할 때 사용). SessionId가 있으면 DB 이력이 우선됨.</summary>
        public List<AskHistoryItem>? History { get; set; }
        public AskPromptOverrideRequest? PromptOverride { get; set; }
        /// <summary>외부 위젯에서 전달하는 사용자 표시명 (예: 나성민)</summary>
        public string? Username { get; set; }
        /// <summary>외부 위젯에서 전달하는 로그인 ID (예: smna)</summary>
        public string? UserLoginId { get; set; }
    }

    public class AskHistoryItem
    {
        public string? Role { get; set; }
        public string? Content { get; set; }
    }

    public class AskPromptOverrideRequest
    {
        public string? SystemPrompt { get; set; }
        public string? RulesPrompt { get; set; }
        public string? LowSimilarityMessage { get; set; }
        public float? SimilarityThreshold { get; set; }
    }

    public class UpsertKbRequest
    {
        public string? Title { get; set; }
        public string? Content { get; set; }
        public string? Visibility { get; set; }
        public List<string>? Platforms { get; set; }
        public string? Platform { get; set; }
        public string? Keywords { get; set; }
        public List<string>? ExpectedQuestions { get; set; }
    }

    public class AddPlatformRequest
    {
        public string? Name { get; set; }
    }

    public class GenerateExpectedQuestionsRequest
    {
        public string? Title { get; set; }
        public string? Content { get; set; }
        public int? Count { get; set; }
    }

    public class GenerateKeywordsRequest
    {
        public string? Title { get; set; }
        public string? Content { get; set; }
        public List<string>? ExpectedQuestions { get; set; }
        public int? Count { get; set; }
    }

    public class RefineSolutionRequest
    {
        public string? Title { get; set; }
        public string? Content { get; set; }
    }

    public class RecommendTitleRequest
    {
        public string? Content { get; set; }
        public int? Count { get; set; }
    }

    public class UpdatePlatformRequest
    {
        public string? NewName { get; set; }
    }

    public class ChatbotPromptTemplateResponse
    {
        public string UserSystemPrompt { get; set; } = string.Empty;
        public string AdminSystemPrompt { get; set; } = string.Empty;
        public string UserRulesPrompt { get; set; } = string.Empty;
        public string AdminRulesPrompt { get; set; } = string.Empty;
        public string UserLowSimilarityMessage { get; set; } = string.Empty;
        public string AdminLowSimilarityMessage { get; set; } = string.Empty;
        public float SimilarityThreshold { get; set; }
    }

    public class UpdateChatbotPromptTemplateRequest
    {
        public string UserSystemPrompt { get; set; } = string.Empty;
        public string AdminSystemPrompt { get; set; } = string.Empty;
        public string UserRulesPrompt { get; set; } = string.Empty;
        public string AdminRulesPrompt { get; set; } = string.Empty;
        public string UserLowSimilarityMessage { get; set; } = string.Empty;
        public string AdminLowSimilarityMessage { get; set; } = string.Empty;
        public float SimilarityThreshold { get; set; }
    }

    public class KnowledgeBaseWriterPromptTemplateResponse
    {
        public string KeywordSystemPrompt { get; set; } = string.Empty;
        public string KeywordRulesPrompt { get; set; } = string.Empty;
        public string ExpectedQuestionSystemPrompt { get; set; } = string.Empty;
        public string ExpectedQuestionRulesPrompt { get; set; } = string.Empty;
        public string TopicKeywordSystemPrompt { get; set; } = string.Empty;
        public string TopicKeywordRulesPrompt { get; set; } = string.Empty;
        public string AnswerRefineSystemPrompt { get; set; } = string.Empty;
        public string AnswerRefineRulesPrompt { get; set; } = string.Empty;
    }

    public class UpdateKnowledgeBaseWriterPromptTemplateRequest
    {
        public string KeywordSystemPrompt { get; set; } = string.Empty;
        public string KeywordRulesPrompt { get; set; } = string.Empty;
        public string ExpectedQuestionSystemPrompt { get; set; } = string.Empty;
        public string ExpectedQuestionRulesPrompt { get; set; } = string.Empty;
        public string TopicKeywordSystemPrompt { get; set; } = string.Empty;
        public string TopicKeywordRulesPrompt { get; set; } = string.Empty;
        public string AnswerRefineSystemPrompt { get; set; } = string.Empty;
        public string AnswerRefineRulesPrompt { get; set; } = string.Empty;
    }

}
