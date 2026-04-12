using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using AiDeskApi.Data;
using AiDeskApi.Models;

namespace AiDeskApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly AiDeskContext _context;
        private readonly ILogger<ChatController> _logger;

        public ChatController(AiDeskContext context, ILogger<ChatController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// 세션 목록 조회 (role/platform/page/pageSize 필터 가능)
        /// GET /api/chat/sessions?role=admin&platform=web&page=1&pageSize=10
        /// </summary>
        [HttpGet("sessions")]
        public async Task<IActionResult> GetSessions(
            [FromQuery] string? role,
            [FromQuery] string? platform,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            try
            {
                page = Math.Max(1, page);
                pageSize = Math.Clamp(pageSize, 1, 100);

                var query = _context.ChatSessions.AsQueryable();
                if (!string.IsNullOrWhiteSpace(role))
                    query = query.Where(s => s.UserRole == role);
                if (!string.IsNullOrWhiteSpace(platform))
                {
                    var normalizedPlatform = platform.Trim().ToLowerInvariant();
                    query = query.Where(s => s.Platform == normalizedPlatform);
                }

                var total = await query.CountAsync();

                var sessions = await query
                    .OrderByDescending(s => s.UpdatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(s => new
                    {
                        s.Id,
                        s.Title,
                        s.UserRole,
                        s.Platform,
                        s.CreatedAt,
                        s.UpdatedAt,
                        s.MessageCount
                    })
                    .ToListAsync();

                return Ok(new { total, page, pageSize, data = sessions });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// 새 세션 생성
        /// POST /api/chat/sessions
        /// </summary>
        [HttpPost("sessions")]
        public async Task<IActionResult> CreateSession([FromBody] CreateSessionRequest request)
        {
            try
            {
                var session = new ChatSession
                {
                    Title = request.Title,
                    UserRole = string.IsNullOrWhiteSpace(request.Role) ? "user" : request.Role,
                    Platform = string.IsNullOrWhiteSpace(request.Platform) ? "web" : request.Platform.Trim().ToLowerInvariant(),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.ChatSessions.Add(session);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"✓ 세션 생성: id={session.Id}, role={session.UserRole}");
                return Ok(new { session.Id, session.Title, session.UserRole, session.Platform, session.CreatedAt });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// 세션 상세 (메시지 포함)
        /// GET /api/chat/sessions/{id}
        /// </summary>
        [HttpGet("sessions/{id}")]
        public async Task<IActionResult> GetSession(int id)
        {
            try
            {
                var session = await _context.ChatSessions
                    .Include(s => s.Messages)
                    .FirstOrDefaultAsync(s => s.Id == id);

                if (session == null)
                    return NotFound("세션을 찾을 수 없습니다.");

                return Ok(new
                {
                    session.Id,
                    session.Title,
                    session.UserRole,
                    session.Platform,
                    session.CreatedAt,
                    session.UpdatedAt,
                    session.MessageCount,
                    messages = session.Messages?
                        .OrderBy(m => m.CreatedAt)
                        .Select(m => new
                        {
                            m.Id,
                            m.Role,
                            m.Content,
                            m.CreatedAt,
                            m.RelatedKbIds,
                            m.TopSimilarity,
                            m.IsLowSimilarity
                        })
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// 세션 삭제
        /// DELETE /api/chat/sessions/{id}
        /// </summary>
        [HttpDelete("sessions/{id}")]
        public async Task<IActionResult> DeleteSession(int id)
        {
            try
            {
                var session = await _context.ChatSessions.FindAsync(id);
                if (session == null)
                    return NotFound("세션을 찾을 수 없습니다.");

                _context.ChatSessions.Remove(session);
                await _context.SaveChangesAsync();

                return Ok(new { message = "세션이 삭제되었습니다." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// 기간별 질문 통계/요약
        /// GET /api/chat/questions-summary?days=7&top=10&role=user&platform=web
        /// </summary>
        [HttpGet("questions-summary")]
        public async Task<IActionResult> GetQuestionSummary(
            [FromQuery] int days = 7,
            [FromQuery] int top = 10,
            [FromQuery] string? role = null,
            [FromQuery] string? platform = null)
        {
            try
            {
                var clampedDays = Math.Clamp(days, 1, 365);
                var clampedTop = Math.Clamp(top, 1, 30);
                var fromUtc = DateTime.UtcNow.AddDays(-clampedDays);

                var query = _context.ChatMessages
                    .AsNoTracking()
                    .Where(m => m.Role == "user" && m.CreatedAt >= fromUtc)
                    .Join(
                        _context.ChatSessions.AsNoTracking(),
                        m => m.SessionId,
                        s => s.Id,
                        (m, s) => new
                        {
                            m.Content,
                            m.CreatedAt,
                            SessionRole = s.UserRole,
                            SessionPlatform = s.Platform
                        });

                if (!string.IsNullOrWhiteSpace(role))
                {
                    var normalizedRole = role.Trim().ToLowerInvariant();
                    query = query.Where(x => x.SessionRole == normalizedRole);
                }

                if (!string.IsNullOrWhiteSpace(platform))
                {
                    var normalizedPlatform = platform.Trim().ToLowerInvariant();
                    query = query.Where(x => x.SessionPlatform == normalizedPlatform);
                }

                var rows = await query.ToListAsync();

                var normalized = rows
                    .Select(x => new
                    {
                        Original = x.Content,
                        Normalized = NormalizeQuestion(x.Content),
                        x.CreatedAt
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Normalized))
                    .ToList();

                var topQuestions = normalized
                    .GroupBy(x => x.Normalized)
                    .Select(g => new
                    {
                        Question = g.OrderByDescending(x => x.CreatedAt).Select(x => x.Original.Trim()).FirstOrDefault() ?? g.Key,
                        Count = g.Count(),
                        LastAskedAt = g.Max(x => x.CreatedAt)
                    })
                    .OrderByDescending(x => x.Count)
                    .ThenByDescending(x => x.LastAskedAt)
                    .Take(clampedTop)
                    .ToList();

                var topKeywords = normalized
                    .SelectMany(x => ExtractKeywords(x.Original))
                    .GroupBy(x => x)
                    .Select(g => new { Keyword = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .ThenBy(x => x.Keyword)
                    .Take(12)
                    .ToList();

                var dailyCounts = normalized
                    .GroupBy(x => x.CreatedAt.Date)
                    .Select(g => new
                    {
                        Date = g.Key.ToString("yyyy-MM-dd"),
                        Count = g.Count()
                    })
                    .OrderBy(x => x.Date)
                    .ToList();

                return Ok(new
                {
                    days = clampedDays,
                    from = fromUtc,
                    to = DateTime.UtcNow,
                    totalQuestions = normalized.Count,
                    uniqueQuestions = normalized.Select(x => x.Normalized).Distinct().Count(),
                    topQuestions,
                    topKeywords,
                    dailyCounts
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private static string NormalizeQuestion(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var value = raw.Trim();
            value = Regex.Replace(value, "\\s+", " ");
            value = Regex.Replace(value, @"[!?.,~`'""(){}\[\]<>]", "");
            value = value.Trim();

            return value.Length > 160 ? value[..160] : value;
        }

        private static IEnumerable<string> ExtractKeywords(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Enumerable.Empty<string>();
            }

            var stopwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "문의", "관련", "처리", "요청", "문제", "해결", "오류", "에러", "이슈", "안내",
                "합니다", "해주세요", "됩니다", "안돼요", "안되요", "확인", "방법", "문의드려요"
            };

            return Regex.Matches(raw, "[\\p{L}\\p{Nd}]{2,}")
                .Select(m => m.Value.Trim().ToLowerInvariant())
                .Where(x => !stopwords.Contains(x));
        }
    }

    public class CreateSessionRequest
    {
        public string? Title { get; set; }
        public string? Role { get; set; }
        public string? Platform { get; set; }
    }
}
