using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using CrmApi.Data;
using CrmApi.Models;
using CrmApi.Services;

namespace CrmApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class KnowledgeBaseController : ControllerBase
    {
        private readonly CrmContext _context;
        private readonly IEmbeddingService _embeddingService;
        private readonly IKnowledgeExtractorService _extractor;
        private readonly IRagService _ragService;
        private readonly ILogger<KnowledgeBaseController> _logger;

        public KnowledgeBaseController(
            CrmContext context,
            IEmbeddingService embeddingService,
            IKnowledgeExtractorService extractor,
            IRagService ragService,
            ILogger<KnowledgeBaseController> logger)
        {
            _context = context;
            _embeddingService = embeddingService;
            _extractor = extractor;
            _ragService = ragService;
            _logger = logger;
        }

        /// <summary>
        /// 상담내역에서 AI로 문제/해결 추출
        /// </summary>
        [HttpPost("interaction/{id}/extract")]
        public async Task<IActionResult> ExtractFromInteraction(int id)
        {
            try
            {
                var interaction = await _context.Interactions.FindAsync(id);
                if (interaction == null)
                    return NotFound("상담내역을 찾을 수 없습니다.");

                _logger.LogInformation($"📝 상담 분석 시작: {id}");

                var extracted = await _extractor.ExtractFromInteractionAsync(interaction);

                return Ok(new
                {
                    interactionId = interaction.Id,
                    interactionContent = interaction.Content?.Substring(0, Math.Min(100, interaction.Content?.Length ?? 0)) + "...",
                    extracted = new
                    {
                        problem = extracted.Problem,
                        solution = extracted.Solution
                    },
                    message = "상담사가 검수 후 저장해주세요."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"추출 오류: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// 추출된 KB 저장
        /// </summary>
        [HttpPost("save")]
        public async Task<IActionResult> SaveKnowledgeBase([FromBody] SaveKbRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Problem) || string.IsNullOrWhiteSpace(request.Solution))
                    return BadRequest("문제와 해결방법을 입력해주세요.");

                _logger.LogInformation($"💾 KB 저장 시작");

                // 1. 문제를 벡터로 변환
                var embedding = await _embeddingService.EmbedTextAsync(request.Problem);

                // 2. KB 저장
                var kb = new KnowledgeBase
                {
                    Problem = request.Problem,
                    Solution = request.Solution,
                    ProblemEmbedding = JsonSerializer.Serialize(embedding),
                    SourceInteractionId = request.InteractionId,
                    CreatedAt = DateTime.UtcNow
                };

                _context.KnowledgeBases.Add(kb);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"✓ KB 저장 완료 (ID: {kb.Id})");

                return Ok(new { id = kb.Id, message = "KB가 성공적으로 저장되었습니다." });
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ 저장 오류: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// RAG 질문 (답변 생성) — role에 따라 KB 필터 적용, 세션에 메시지 저장
        /// </summary>
        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] AskRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Question))
                    return BadRequest("질문을 입력해주세요.");

                var role = string.IsNullOrWhiteSpace(request.Role) ? "user" : request.Role;
                _logger.LogInformation($"❓ [{role}] 질문: {request.Question}");

                // 세션 처리
                ChatSession? session = null;
                if (request.SessionId.HasValue)
                {
                    session = await _context.ChatSessions.FindAsync(request.SessionId.Value);
                }
                else if (request.CreateSession == true)
                {
                    session = new ChatSession
                    {
                        Title = request.Question.Length > 50
                            ? request.Question[..50] + "..."
                            : request.Question,
                        UserRole = role,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.ChatSessions.Add(session);
                    await _context.SaveChangesAsync();
                }

                var result = await _ragService.SearchAndGenerateAsync(request.Question, role);

                // 세션이 있으면 메시지 저장
                if (session != null)
                {
                    var relatedIds = result.RelatedKBs.Select(k => k.Id).ToList();
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
                            RelatedKbIds = JsonSerializer.Serialize(relatedIds)
                        }
                    );
                    session.MessageCount += 2;
                    session.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }

                return Ok(new
                {
                    result.Answer,
                    result.RelatedKBs,
                    result.ConflictDetected,
                    result.DecisionRule,
                    sessionId = session?.Id
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ 질문 오류: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// 관리자 공식 KB 직접 작성
        /// </summary>
        [HttpPost("official")]
        public async Task<IActionResult> CreateOfficialKb([FromBody] OfficialKbRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Problem) || string.IsNullOrWhiteSpace(request.Solution))
                    return BadRequest("문제와 해결방법을 입력해주세요.");

                var embedding = await _embeddingService.EmbedTextAsync(request.Problem);

                var kb = new KnowledgeBase
                {
                    Problem = request.Problem,
                    Solution = request.Solution,
                    ProblemEmbedding = JsonSerializer.Serialize(embedding),
                    SourceType = "official",
                    Visibility = request.Visibility ?? "internal",
                    IsApproved = request.Visibility == "common",
                    ApprovedBy = request.ApprovedBy,
                    ApprovedAt = request.Visibility == "common" ? DateTime.UtcNow : null,
                    Tags = request.Tags,
                    CreatedAt = DateTime.UtcNow
                };

                _context.KnowledgeBases.Add(kb);
                await _context.SaveChangesAsync();

                return Ok(new { id = kb.Id, message = "공식 KB가 등록되었습니다." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// KB 승인 (visibility=common으로 전환)
        /// </summary>
        [HttpPut("{id}/approve")]
        public async Task<IActionResult> ApproveKb(int id, [FromBody] ApproveRequest request)
        {
            try
            {
                var kb = await _context.KnowledgeBases.FindAsync(id);
                if (kb == null) return NotFound();

                kb.IsApproved = true;
                kb.Visibility = "common";
                kb.ApprovedBy = request.ApprovedBy ?? "admin";
                kb.ApprovedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return Ok(new { message = "KB가 승인되었습니다.", id = kb.Id });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// KB visibility 토글 (internal ↔ common)
        /// </summary>
        [HttpPut("{id}/visibility")]
        public async Task<IActionResult> ToggleVisibility(int id)
        {
            try
            {
                var kb = await _context.KnowledgeBases.FindAsync(id);
                if (kb == null) return NotFound();

                kb.Visibility = kb.Visibility == "common" ? "internal" : "common";
                if (kb.Visibility == "common" && !kb.IsApproved)
                {
                    kb.IsApproved = true;
                    kb.ApprovedAt = DateTime.UtcNow;
                }
                await _context.SaveChangesAsync();

                return Ok(new { id = kb.Id, visibility = kb.Visibility });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// 모든 KB 조회
        /// </summary>
        [HttpGet("list")]
        public async Task<IActionResult> GetAllKnowledgeBases([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var total = await _context.KnowledgeBases.CountAsync();
                var kbs = await _context.KnowledgeBases
                    .OrderByDescending(x => x.ViewCount)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(x => new
                    {
                        x.Id,
                        x.Problem,
                        x.Solution,
                        x.CreatedAt,
                        x.ViewCount,
                        x.SourceType,
                        x.Visibility,
                        x.IsApproved,
                        x.Tags,
                        x.SourceInteractionId,
                        x.ApprovedAt,
                        x.ApprovedBy
                    })
                    .ToListAsync();

                return Ok(new
                {
                    total,
                    page,
                    pageSize,
                    data = kbs
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// KB 상세 조회
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetKnowledgeBase(int id)
        {
            try
            {
                var kb = await _context.KnowledgeBases.FindAsync(id);
                if (kb == null)
                    return NotFound("KB를 찾을 수 없습니다.");

                return Ok(new
                {
                    kb.Id,
                    kb.Problem,
                    kb.Solution,
                    kb.CreatedAt,
                    kb.ViewCount
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// KB 삭제
        /// </summary>
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

                return Ok(new { message = "KB가 삭제되었습니다." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// KB 통계
        /// </summary>
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            try
            {
                var kbs = await _context.KnowledgeBases.ToListAsync();
                var totalKBs = kbs.Count;
                var totalViews = kbs.Sum(x => x.ViewCount);
                var topKBs = kbs
                    .OrderByDescending(x => x.ViewCount)
                    .Take(5)
                    .Select(x => new { x.Problem, x.ViewCount })
                    .ToList();

                return Ok(new
                {
                    totalKBs,
                    totalViews,
                    topKBs
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class SaveKbRequest
    {
        public int? InteractionId { get; set; }
        public string? Problem { get; set; }
        public string? Solution { get; set; }
    }

    public class AskRequest
    {
        public string? Question { get; set; }
        public string? Role { get; set; }            // "admin" | "user"
        public int? SessionId { get; set; }          // 기존 세션 ID
        public bool? CreateSession { get; set; }     // true이면 새 세션 생성
    }

    public class OfficialKbRequest
    {
        public string? Problem { get; set; }
        public string? Solution { get; set; }
        public string? Visibility { get; set; }      // "internal" | "common"
        public string? Tags { get; set; }
        public string? ApprovedBy { get; set; }
    }

    public class ApproveRequest
    {
        public string? ApprovedBy { get; set; }
    }
}
