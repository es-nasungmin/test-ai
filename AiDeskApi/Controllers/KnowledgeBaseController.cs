using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Security.Claims;
using AiDeskApi.Data;
using AiDeskApi.Models;
using AiDeskApi.Services;

namespace AiDeskApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class KnowledgeBaseController : ControllerBase
    {
        private readonly AiDeskContext _context;
        private readonly IEmbeddingService _embeddingService;
        private readonly IRagService _ragService;
        private readonly IVectorSearchService _vectorSearchService;
        private readonly IChatbotPromptTemplateService _chatbotPromptTemplates;
        private readonly IKnowledgeBaseWriterPromptTemplateService _kbWriterPromptTemplates;
        private readonly IKnowledgeExtractorService _knowledgeExtractorService;
        private readonly IDocumentKnowledgeService _documentKnowledgeService;
        private readonly ILogger<KnowledgeBaseController> _logger;

        public KnowledgeBaseController(
            AiDeskContext context,
            IEmbeddingService embeddingService,
            IRagService ragService,
            IVectorSearchService vectorSearchService,
            IKnowledgeExtractorService knowledgeExtractorService,
            IDocumentKnowledgeService documentKnowledgeService,
            IChatbotPromptTemplateService chatbotPromptTemplates,
            IKnowledgeBaseWriterPromptTemplateService kbWriterPromptTemplates,
            ILogger<KnowledgeBaseController> logger)
        {
            _context = context;
            _embeddingService = embeddingService;
            _ragService = ragService;
            _vectorSearchService = vectorSearchService;
            _knowledgeExtractorService = knowledgeExtractorService;
            _documentKnowledgeService = documentKnowledgeService;
            _chatbotPromptTemplates = chatbotPromptTemplates;
            _kbWriterPromptTemplates = kbWriterPromptTemplates;
            _logger = logger;
        }

        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] AskRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Question))
                    return BadRequest("질문을 입력해주세요.");

                var role = string.IsNullOrWhiteSpace(request.Role) ? "user" : request.Role;
                var platform = NormalizePlatform(request.Platform);
                _logger.LogInformation($"❓ [{role}] 질문: {request.Question}");

                var noSave = request.NoSave == true;
                var historyTurnCount = Math.Clamp(request.HistoryTurnCount ?? 6, 0, 20);
                var runtimeOptions = new RagRuntimeOptions
                {
                    DisablePersistence = noSave,
                    PromptOnly = request.PromptOverride?.PromptOnly == true,
                    SystemPromptOverride = request.PromptOverride?.SystemPrompt,
                    RulesPromptOverride = request.PromptOverride?.RulesPrompt,
                    LowSimilarityMessageOverride = request.PromptOverride?.LowSimilarityMessage,
                    SimilarityThresholdOverride = request.PromptOverride?.SimilarityThreshold
                };

                ChatSession? session = null;
                List<(string Role, string Content)> history = new();

                if (request.SessionId.HasValue)
                {
                    session = await _context.ChatSessions.FindAsync(request.SessionId.Value);

                    if (session == null)
                        return NotFound("세션을 찾을 수 없습니다.");

                    if (historyTurnCount > 0)
                    {
                        history = await _context.ChatMessages
                            .AsNoTracking()
                            .Where(x => x.SessionId == request.SessionId.Value)
                            .OrderByDescending(x => x.CreatedAt)
                            .Take(historyTurnCount)
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
                        Platform = platform,
                        TopSimilarity = result.TopSimilarity,
                        TopMatchedQuestion = result.TopMatchedQuestion ?? result.RelatedKBs.FirstOrDefault()?.MatchedQuestion,
                        CreatedAt = DateTime.UtcNow,
                        IsResolved = false
                    });
                }

                if (!noSave && session != null)
                {
                    session.Platform = platform;
                    var diagnosticsById = (result.RetrievalDiagnostics?.Candidates ?? new List<RetrievalCandidateDiagnostic>())
                        .GroupBy(c => c.Id)
                        .ToDictionary(g => g.Key, g => g.First());

                    var relatedKbMeta = result.RelatedKBs
                        .GroupBy(k => k.Id)
                        .Select(g =>
                        {
                            var similarity = g.Max(x => x.Similarity);
                            diagnosticsById.TryGetValue(g.Key, out var cand);

                            return new
                            {
                                id = g.Key,
                                similarity,
                                includedBySemantic = cand?.IncludedBySemantic == true,
                                includedByKeyword = cand?.IncludedByKeyword == true,
                                matchedKeywords = cand?.MatchedKeywords ?? new List<string>(),
                                keywordMatchCount = cand?.KeywordMatchCount ?? 0,
                                baseSimilarity = cand?.BaseSimilarity ?? similarity,
                                keywordBoost = cand?.KeywordBoost ?? 0f
                            };
                        })
                        .ToList();
                    var relatedIds = relatedKbMeta.Select(x => x.id).ToList();
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
                            RelatedDocumentMeta = JsonSerializer.Serialize(result.RelatedDocuments),
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
                    result.RelatedKBs,
                    result.RelatedDocuments,
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
                var embeddingSource = BuildKbEmbeddingSource(request.Title, content, expectedQuestions);
                var kbEmbedding = await _embeddingService.EmbedTextAsync(embeddingSource);
                var now = DateTime.Now;
                var actor = await ResolveActorNameAsync();
                var platforms = NormalizePlatforms(request.Platforms, request.Platform);

                var kb = new KnowledgeBase
                {
                    Title = request.Title?.Trim(),
                    // 레거시 호환: Problem/Solution 컬럼은 유지하되 문서형 구조에서는 Title/Content 의미로 저장
                    Problem = request.Title!.Trim(),
                    Solution = content,
                    ProblemEmbedding = JsonSerializer.Serialize(kbEmbedding),
                    Visibility = NormalizeVisibility(request.Visibility),
                    Platform = SerializePlatforms(platforms),
                    Keywords = (request.Keywords ?? request.Tags)?.Trim(),
                    CreatedAt = now,
                    UpdatedAt = now,
                    CreatedBy = actor,
                    UpdatedBy = actor
                };

                foreach (var q in expectedQuestions)
                {
                    kb.SimilarQuestions.Add(new KnowledgeBaseSimilarQuestion
                    {
                        Question = q,
                        QuestionEmbedding = null,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                _context.KnowledgeBases.Add(kb);
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

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateKnowledgeBase(int id, [FromBody] UpsertKbRequest request)
        {
            try
            {
                var validationError = ValidateRequest(request);
                if (!string.IsNullOrWhiteSpace(validationError))
                    return BadRequest(validationError);

                var kb = await _context.KnowledgeBases
                    .Include(x => x.SimilarQuestions)
                    .FirstOrDefaultAsync(x => x.Id == id);

                if (kb == null)
                    return NotFound("KB를 찾을 수 없습니다.");

                var content = ResolveContent(request);
                var expectedQuestions = ResolveExpectedQuestions(request);
                var embeddingSource = BuildKbEmbeddingSource(request.Title, content, expectedQuestions);
                var kbEmbedding = await _embeddingService.EmbedTextAsync(embeddingSource);
                kb.ProblemEmbedding = JsonSerializer.Serialize(kbEmbedding);

                var platforms = NormalizePlatforms(request.Platforms, request.Platform);
                var actor = await ResolveActorNameAsync();
                kb.Title = request.Title?.Trim();
                kb.Problem = request.Title!.Trim();
                kb.Solution = content;
                kb.Visibility = NormalizeVisibility(request.Visibility);
                kb.Platform = SerializePlatforms(platforms);
                kb.Keywords = (request.Keywords ?? request.Tags)?.Trim();
                kb.UpdatedAt = DateTime.Now;
                kb.UpdatedBy = actor;

                var incoming = expectedQuestions;

                var existingByKey = kb.SimilarQuestions
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
                        existing.Question = q;
                        continue;
                    }

                    kb.SimilarQuestions.Add(new KnowledgeBaseSimilarQuestion
                    {
                        Question = q,
                        QuestionEmbedding = null,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                var toRemove = kb.SimilarQuestions
                    .Where(x => !incomingKeys.Contains(NormalizeQuestionKey(x.Question)))
                    .ToList();

                if (toRemove.Count > 0)
                {
                    _context.KnowledgeBaseSimilarQuestions.RemoveRange(toRemove);
                }

                await _context.SaveChangesAsync();
                try
                {
                    await _vectorSearchService.UpsertKnowledgeBaseAsync(kb);
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
                    .Include(x => x.SimilarQuestions)
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
                    query = query.Where(x =>
                        (x.Title != null && EF.Functions.Like(x.Title, $"%{q}%")) ||
                        EF.Functions.Like(x.Solution, $"%{q}%") ||
                        (x.Keywords != null && EF.Functions.Like(x.Keywords, $"%{q}%")) ||
                        x.SimilarQuestions.Any(s => EF.Functions.Like(s.Question, $"%{q}%")));
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
                        content = x.Solution,
                        x.CreatedAt,
                        x.UpdatedAt,
                        x.CreatedBy,
                        x.UpdatedBy,
                        x.ViewCount,
                        x.Visibility,
                        x.Platform,
                        platforms = SplitPlatforms(x.Platform),
                        keywords = x.Keywords,
                        expectedQuestions = x.SimilarQuestions
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

        [HttpPost("documents/upload")]
        [RequestSizeLimit(1024L * 1024L * 30L)]
        public async Task<IActionResult> UploadDocument([FromForm] UploadDocumentRequest request, CancellationToken cancellationToken)
        {
            try
            {
                if (request.File == null || request.File.Length == 0)
                    return BadRequest(new { error = "PDF 파일을 첨부해주세요." });

                var ext = Path.GetExtension(request.File.FileName);
                if (!string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
                    return BadRequest(new { error = "현재는 PDF 파일만 지원합니다." });

                var actor = await ResolveActorNameAsync();
                await using var stream = request.File.OpenReadStream();
                var result = await _documentKnowledgeService.UploadPdfAsync(
                    stream,
                    request.File.FileName,
                    request.DisplayName ?? request.File.FileName,
                    request.Visibility ?? "admin",
                    request.Platform ?? "공통",
                    actor,
                    cancellationToken);

                return Ok(new
                {
                    result.DocumentId,
                    result.DisplayName,
                    result.Status,
                    result.ChunkCount
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 문서 업로드/인덱싱 오류");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("documents")]
        public async Task<IActionResult> ListDocuments([FromQuery] string? role = "admin", [FromQuery] string? platform = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var items = await _documentKnowledgeService.ListAsync(role ?? "admin", platform, cancellationToken);
                return Ok(new { data = items, total = items.Count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 문서 목록 조회 오류");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPut("documents/{id:int}")]
        public async Task<IActionResult> UpdateDocument(int id, [FromBody] UpdateDocumentRequest request, CancellationToken cancellationToken)
        {
            try
            {
                if (request == null)
                    return BadRequest(new { error = "요청 본문이 필요합니다." });

                var hasAnyField = request.DisplayName != null
                    || request.Visibility != null
                    || request.Platform != null;

                if (!hasAnyField)
                    return BadRequest(new { error = "수정할 항목을 하나 이상 입력해주세요." });

                if (request.DisplayName != null && string.IsNullOrWhiteSpace(request.DisplayName))
                    return BadRequest(new { error = "표시 이름은 비워둘 수 없습니다." });

                var actor = await ResolveActorNameAsync();
                var updated = await _documentKnowledgeService.UpdateAsync(
                    id,
                    request.DisplayName,
                    request.Visibility,
                    request.Platform,
                    actor,
                    cancellationToken);

                if (updated == null)
                    return NotFound(new { error = "문서를 찾을 수 없습니다." });

                return Ok(new
                {
                    message = "문서가 수정되었습니다.",
                    data = updated
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 문서 수정 오류 id={Id}", id);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpDelete("documents/{id:int}")]
        public async Task<IActionResult> DeleteDocument(int id, CancellationToken cancellationToken)
        {
            try
            {
                var deleted = await _documentKnowledgeService.DeleteAsync(id, cancellationToken);
                if (!deleted) return NotFound(new { error = "문서를 찾을 수 없습니다." });
                return Ok(new { message = "문서가 삭제되었습니다." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 문서 삭제 오류 id={Id}", id);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("documents/{id:int}/reindex")]
        [RequestSizeLimit(30 * 1024 * 1024)]
        public async Task<IActionResult> ReindexDocument(int id, IFormFile file, CancellationToken cancellationToken)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return BadRequest(new { error = "파일을 첨부해주세요." });

                var ext = Path.GetExtension(file.FileName);
                if (!string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
                    return BadRequest(new { error = "현재는 PDF 파일만 지원합니다." });

                var actor = await ResolveActorNameAsync();
                await using var stream = file.OpenReadStream();
                var result = await _documentKnowledgeService.ReindexAsync(id, stream, actor, cancellationToken);

                return Ok(new
                {
                    result.DocumentId,
                    result.DisplayName,
                    result.Status,
                    result.ChunkCount
                });
            }
            catch (InvalidOperationException ex)
            {
                if (ex.Message.Contains("문서를 찾을 수 없습니다", StringComparison.OrdinalIgnoreCase))
                {
                    return NotFound(new { error = ex.Message });
                }

                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 문서 재인덱싱 오류 id={Id}", id);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("documents/{id:int}/download")]
        public async Task<IActionResult> DownloadDocument(int id, CancellationToken cancellationToken)
        {
            try
            {
                var fileInfo = await _documentKnowledgeService.GetDownloadInfoAsync(id, cancellationToken);
                if (fileInfo == null)
                {
                    return NotFound(new { error = "다운로드 가능한 원본 PDF를 찾을 수 없습니다." });
                }

                var stream = new FileStream(fileInfo.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return File(stream, "application/pdf", fileInfo.DownloadFileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 문서 다운로드 오류 id={Id}", id);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("generate-similar-questions")]
        public async Task<IActionResult> GenerateSimilarQuestions([FromBody] GenerateSimilarQuestionsRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Content))
                    return BadRequest(new { error = "내용을 먼저 작성해주세요." });

                var generated = await _knowledgeExtractorService.GenerateSimilarQuestionsAsync(
                    request.Title?.Trim() ?? string.Empty,
                    request.Content.Trim(),
                    Math.Clamp(request.Count ?? 5, 1, 5));

                return Ok(new { items = generated });
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ 유사 질문 생성 오류: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("generate-keywords")]
        public async Task<IActionResult> GenerateKeywords([FromBody] GenerateKeywordsRequest request)
        {
            try
            {
                var prompts = await _kbWriterPromptTemplates.GetAsync();
                var source = BuildKbEmbeddingSource(request.Title, request.Content, request.ExpectedQuestions);

                if (string.IsNullOrWhiteSpace(source))
                {
                    return BadRequest(new { error = "제목 또는 내용을 먼저 입력해주세요" });
                }

                var count = Math.Clamp(request.Count ?? 5, 1, 5);
                var generated = await _knowledgeExtractorService.GenerateKeywordsAsync(
                    source,
                    request.ExpectedQuestions,
                    count,
                    prompts.KeywordSystemPrompt,
                    prompts.KeywordRulesPrompt,
                    source: "document");

                return Ok(new 
                { 
                    items = generated,
                    combined = generated
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ 키워드 생성 오류: {ex.Message}");
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
                    request.Title?.Trim() ?? string.Empty,
                    request.Content.Trim(),
                    prompts.AnswerRefineSystemPrompt,
                    prompts.AnswerRefineRulesPrompt);

                return Ok(new { solution = refined });
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ 답변 정리 오류: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetKnowledgeBase(int id)
        {
            try
            {
                var kb = await _context.KnowledgeBases
                    .Include(x => x.SimilarQuestions)
                    .FirstOrDefaultAsync(x => x.Id == id);

                if (kb == null)
                    return NotFound("KB를 찾을 수 없습니다.");

                return Ok(new
                {
                    kb.Id,
                    kb.Title,
                    content = kb.Solution,
                    kb.CreatedAt,
                    kb.UpdatedAt,
                    kb.CreatedBy,
                    kb.UpdatedBy,
                    kb.ViewCount,
                    kb.Visibility,
                    kb.Platform,
                    platforms = SplitPlatforms(kb.Platform),
                    keywords = kb.Keywords,
                    expectedQuestions = kb.SimilarQuestions
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

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteKnowledgeBase(int id)
        {
            try
            {
                var kb = await _context.KnowledgeBases.FindAsync(id);
                if (kb == null)
                    return NotFound("KB를 찾을 수 없습니다.");

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
                TopicKeywordSystemPrompt = template.TopicKeywordSystemPrompt,
                TopicKeywordRulesPrompt = template.TopicKeywordRulesPrompt,
                AnswerRefineSystemPrompt = template.AnswerRefineSystemPrompt,
                AnswerRefineRulesPrompt = template.AnswerRefineRulesPrompt
            });
        }

        [HttpPut("writer-prompt-template")]
        public async Task<IActionResult> UpdateWriterPromptTemplate([FromBody] UpdateKnowledgeBaseWriterPromptTemplateRequest request)
        {
            try
            {
                var template = await _kbWriterPromptTemplates.UpdateAsync(
                    request.KeywordSystemPrompt,
                    request.KeywordRulesPrompt,
                    request.TopicKeywordSystemPrompt,
                    request.TopicKeywordRulesPrompt,
                    request.AnswerRefineSystemPrompt,
                    request.AnswerRefineRulesPrompt);

                return Ok(new KnowledgeBaseWriterPromptTemplateResponse
                {
                    KeywordSystemPrompt = template.KeywordSystemPrompt,
                    KeywordRulesPrompt = template.KeywordRulesPrompt,
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

            var data = await query
                .OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(new { total, page, pageSize, data });
        }

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
            if (string.IsNullOrWhiteSpace(request.Title))
                return "제목을 입력해주세요.";
            if (string.IsNullOrWhiteSpace(ResolveContent(request)))
                return "내용을 입력해주세요.";
            if (!string.IsNullOrWhiteSpace(request.Visibility)
                && !new[] { "admin", "user", "common", "internal" }
                    .Contains(request.Visibility.Trim().ToLowerInvariant()))
                return "공개수준은 admin 또는 user 여야 합니다.";
            var platforms = NormalizePlatforms(request.Platforms, request.Platform);
            if (platforms.Any(x => x.Length < 2 && x != "공통"))
                return "플랫폼명은 2자 이상이어야 합니다.";
            if (ResolveExpectedQuestions(request).Count > 5)
                return "예상질문은 최대 5개까지 등록 가능합니다.";
            return null;
        }

        private static string ResolveContent(UpsertKbRequest request)
        {
            return (request.Content ?? request.Solution ?? string.Empty).Trim();
        }

        private static List<string> ResolveExpectedQuestions(UpsertKbRequest request)
        {
            var source = request.ExpectedQuestions ?? request.SimilarQuestions;
            return (source ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();
        }

        private static string BuildKbEmbeddingSource(string? title, string? content, IEnumerable<string>? expectedQuestions)
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

            var expected = (expectedQuestions ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();

            if (expected.Count > 0)
            {
                parts.Add("예상질문: " + string.Join(" | ", expected));
            }

            return string.Join("\n", parts);
        }

        private async Task<string> ResolveActorNameAsync()
        {
            var actor = User?.Identity?.Name
                ?? User?.FindFirstValue(ClaimTypes.Name)
                ?? User?.FindFirstValue("name")
                ?? User?.FindFirstValue("unique_name");

            if (!string.IsNullOrWhiteSpace(actor))
            {
                return actor.Trim();
            }

            var userIdRaw = User?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User?.FindFirstValue("sub");

            if (int.TryParse(userIdRaw, out var userId) && userId > 0)
            {
                var username = await _context.Users
                    .AsNoTracking()
                    .Where(x => x.Id == userId)
                    .Select(x => x.Username)
                    .FirstOrDefaultAsync();

                if (!string.IsNullOrWhiteSpace(username))
                {
                    return username.Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(actor) && Request.Headers.TryGetValue("X-Actor-Name", out var headerActor))
            {
                actor = headerActor.ToString();
            }

            return string.IsNullOrWhiteSpace(actor) ? "알 수 없음" : actor.Trim();
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
        public int? HistoryTurnCount { get; set; }
        public AskPromptOverrideRequest? PromptOverride { get; set; }
    }

    public class AskPromptOverrideRequest
    {
        public bool? PromptOnly { get; set; }
        public string? SystemPrompt { get; set; }
        public string? RulesPrompt { get; set; }
        public string? LowSimilarityMessage { get; set; }
        public float? SimilarityThreshold { get; set; }
    }

    public class UpsertKbRequest
    {
        public string? Title { get; set; }
        public string? Content { get; set; }
        public string? RepresentativeQuestion { get; set; }
        public string? Solution { get; set; }
        public string? Visibility { get; set; }
        public List<string>? Platforms { get; set; }
        public string? Platform { get; set; }
        public string? Keywords { get; set; }
        // 하위 호환: 구 필드명(tags)
        public string? Tags { get; set; }
        public List<string>? ExpectedQuestions { get; set; }
        public List<string>? SimilarQuestions { get; set; }
    }

    public class UploadDocumentRequest
    {
        public IFormFile? File { get; set; }
        public string? DisplayName { get; set; }
        public string? Visibility { get; set; }
        public string? Platform { get; set; }
    }

    public class UpdateDocumentRequest
    {
        public string? DisplayName { get; set; }
        public string? Visibility { get; set; }
        public string? Platform { get; set; }
    }

    public class AddPlatformRequest
    {
        public string? Name { get; set; }
    }

    public class GenerateSimilarQuestionsRequest
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
        public string TopicKeywordSystemPrompt { get; set; } = string.Empty;
        public string TopicKeywordRulesPrompt { get; set; } = string.Empty;
        public string AnswerRefineSystemPrompt { get; set; } = string.Empty;
        public string AnswerRefineRulesPrompt { get; set; } = string.Empty;
    }

    public class UpdateKnowledgeBaseWriterPromptTemplateRequest
    {
        public string KeywordSystemPrompt { get; set; } = string.Empty;
        public string KeywordRulesPrompt { get; set; } = string.Empty;
        public string TopicKeywordSystemPrompt { get; set; } = string.Empty;
        public string TopicKeywordRulesPrompt { get; set; } = string.Empty;
        public string AnswerRefineSystemPrompt { get; set; } = string.Empty;
        public string AnswerRefineRulesPrompt { get; set; } = string.Empty;
    }
}
