using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CrmApi.Data;
using CrmApi.Models;

namespace CrmApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly CrmContext _context;
        private readonly ILogger<ChatController> _logger;

        public ChatController(CrmContext context, ILogger<ChatController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// 세션 목록 조회 (role 필터 가능)
        /// GET /api/chat/sessions?role=admin
        /// </summary>
        [HttpGet("sessions")]
        public async Task<IActionResult> GetSessions([FromQuery] string? role)
        {
            try
            {
                var query = _context.ChatSessions.AsQueryable();
                if (!string.IsNullOrWhiteSpace(role))
                    query = query.Where(s => s.UserRole == role);

                var sessions = await query
                    .OrderByDescending(s => s.UpdatedAt)
                    .Select(s => new
                    {
                        s.Id,
                        s.Title,
                        s.UserRole,
                        s.CreatedAt,
                        s.UpdatedAt,
                        s.MessageCount
                    })
                    .ToListAsync();

                return Ok(sessions);
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
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.ChatSessions.Add(session);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"✓ 세션 생성: id={session.Id}, role={session.UserRole}");
                return Ok(new { session.Id, session.Title, session.UserRole, session.CreatedAt });
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
                            m.RelatedKbIds
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
    }

    public class CreateSessionRequest
    {
        public string? Title { get; set; }
        public string? Role { get; set; }
    }
}
