using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
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
        /// 세션 목록 조회 (role/platform/keyword/page/pageSize 필터 가능)
        /// GET /api/chat/sessions?role=admin&platform=web&keyword=kim&page=1&pageSize=10
        /// </summary>
        [HttpGet("sessions")]
        public async Task<IActionResult> GetSessions(
            [FromQuery] string? role,
            [FromQuery] string? platform,
            [FromQuery] string? keyword,
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
                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    var normalizedKeyword = keyword.Trim().ToLowerInvariant();
                    var likePattern = $"%{normalizedKeyword}%";
                    query = query.Where(s =>
                        (!string.IsNullOrEmpty(s.ActorName) && EF.Functions.Like(s.ActorName.ToLower(), likePattern)) ||
                        (!string.IsNullOrEmpty(s.Title) && EF.Functions.Like(s.Title.ToLower(), likePattern)) ||
                        _context.ChatMessages.Any(m =>
                            m.SessionId == s.Id &&
                            !string.IsNullOrEmpty(m.Content) &&
                            EF.Functions.Like(m.Content.ToLower(), likePattern))
                    );
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
                        s.ActorName,
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
                    ActorName = await ResolveActorNameAsync(),
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
                    session.ActorName,
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
                            m.RelatedKbMeta,
                            m.RetrievalDebugMeta,
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

                var botQuery = _context.ChatMessages
                    .AsNoTracking()
                    .Where(m => m.Role == "bot" && m.CreatedAt >= fromUtc)
                    .Join(
                        _context.ChatSessions.AsNoTracking(),
                        m => m.SessionId,
                        s => s.Id,
                        (m, s) => new
                        {
                            m.RelatedKbMeta,
                            m.RelatedKbIds,
                            m.CreatedAt,
                            SessionRole = s.UserRole,
                            SessionPlatform = s.Platform
                        });

                if (!string.IsNullOrWhiteSpace(role))
                {
                    var normalizedRole = role.Trim().ToLowerInvariant();
                    botQuery = botQuery.Where(x => x.SessionRole == normalizedRole);
                }

                if (!string.IsNullOrWhiteSpace(platform))
                {
                    var normalizedPlatform = platform.Trim().ToLowerInvariant();
                    botQuery = botQuery.Where(x => x.SessionPlatform == normalizedPlatform);
                }

                var botRows = await botQuery.ToListAsync();

                // 답변 품질 지표 집계 (TopSimilarity / IsLowSimilarity 기반)
                var qualityQuery = _context.ChatMessages
                    .AsNoTracking()
                    .Where(m => m.Role == "bot" && m.CreatedAt >= fromUtc && m.TopSimilarity != null)
                    .Join(
                        _context.ChatSessions.AsNoTracking(),
                        m => m.SessionId,
                        s => s.Id,
                        (m, s) => new
                        {
                            m.TopSimilarity,
                            m.IsLowSimilarity,
                            SessionRole = s.UserRole,
                            SessionPlatform = s.Platform
                        });

                if (!string.IsNullOrWhiteSpace(role))
                    qualityQuery = qualityQuery.Where(x => x.SessionRole == role.Trim().ToLowerInvariant());
                if (!string.IsNullOrWhiteSpace(platform))
                    qualityQuery = qualityQuery.Where(x => x.SessionPlatform == platform.Trim().ToLowerInvariant());

                var qualityRows = await qualityQuery.ToListAsync();

                var totalAnswers = qualityRows.Count;
                var avgSimilarity = totalAnswers > 0 ? qualityRows.Average(x => (double)x.TopSimilarity!) : 0.0;
                var lowSimilarityCount = qualityRows.Count(x => x.IsLowSimilarity);
                var lowSimilarityRate = totalAnswers > 0 ? (double)lowSimilarityCount / totalAnswers : 0.0;
                var highConfidenceCount = qualityRows.Count(x => x.TopSimilarity >= 0.82f);
                var highConfidenceRate = totalAnswers > 0 ? (double)highConfidenceCount / totalAnswers : 0.0;

                // 유사도 구간별 분포
                var similarityDistribution = new[]
                {
                    new { Range = "0.9+",   Count = qualityRows.Count(x => x.TopSimilarity >= 0.9f) },
                    new { Range = "0.8~0.9", Count = qualityRows.Count(x => x.TopSimilarity >= 0.8f && x.TopSimilarity < 0.9f) },
                    new { Range = "0.7~0.8", Count = qualityRows.Count(x => x.TopSimilarity >= 0.7f && x.TopSimilarity < 0.8f) },
                    new { Range = "0.5~0.7", Count = qualityRows.Count(x => x.TopSimilarity >= 0.5f && x.TopSimilarity < 0.7f) },
                    new { Range = "~0.5",    Count = qualityRows.Count(x => x.TopSimilarity < 0.5f) },
                };

                var kbRefMap = new Dictionary<int, (int Count, DateTime LastReferencedAt)>();
                foreach (var row in botRows)
                {
                    var ids = ParseReferencedKbIds(row.RelatedKbMeta, row.RelatedKbIds);
                    foreach (var kbId in ids)
                    {
                        if (kbRefMap.TryGetValue(kbId, out var existing))
                        {
                            kbRefMap[kbId] = (
                                existing.Count + 1,
                                existing.LastReferencedAt > row.CreatedAt ? existing.LastReferencedAt : row.CreatedAt
                            );
                        }
                        else
                        {
                            kbRefMap[kbId] = (1, row.CreatedAt);
                        }
                    }
                }

                var rankedKbRefs = kbRefMap
                    .OrderByDescending(x => x.Value.Count)
                    .ThenByDescending(x => x.Value.LastReferencedAt)
                    .Take(clampedTop)
                    .ToList();

                var rankedKbIds = rankedKbRefs.Select(x => x.Key).ToList();
                var kbTitleMap = await _context.KnowledgeBases
                    .AsNoTracking()
                    .Where(k => rankedKbIds.Contains(k.Id))
                    .Select(k => new { k.Id, k.Title })
                    .ToDictionaryAsync(k => k.Id, k => k.Title ?? $"KB #{k.Id}");

                var topReferencedKbs = rankedKbRefs
                    .Select(x => new
                    {
                        KbId = x.Key,
                        Title = kbTitleMap.TryGetValue(x.Key, out var title) ? title : $"KB #{x.Key}",
                        Count = x.Value.Count,
                        LastReferencedAt = x.Value.LastReferencedAt
                    })
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
                    uniqueReferencedKbs = kbRefMap.Count,
                    rankingBasis = "kb-reference",
                    // 답변 품질 지표
                    totalAnswers,
                    avgSimilarity = Math.Round(avgSimilarity, 3),
                    lowSimilarityCount,
                    lowSimilarityRate = Math.Round(lowSimilarityRate, 4),
                    highConfidenceCount,
                    highConfidenceRate = Math.Round(highConfidenceRate, 4),
                    similarityDistribution,
                    topReferencedKbs,
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

        private static IReadOnlyCollection<int> ParseReferencedKbIds(string? relatedKbMeta, string? relatedKbIds)
        {
            var result = new HashSet<int>();

            if (!string.IsNullOrWhiteSpace(relatedKbMeta))
            {
                try
                {
                    using var doc = JsonDocument.Parse(relatedKbMeta);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in doc.RootElement.EnumerateArray())
                        {
                            if (item.ValueKind != JsonValueKind.Object) continue;
                            if (item.TryGetProperty("isSelected", out var selectedProp)
                                && selectedProp.ValueKind == JsonValueKind.False)
                            {
                                continue;
                            }
                            if (item.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var id) && id > 0)
                            {
                                result.Add(id);
                            }
                        }
                    }
                }
                catch
                {
                    // ignore malformed json
                }
            }

            if (result.Count == 0 && !string.IsNullOrWhiteSpace(relatedKbIds))
            {
                try
                {
                    using var doc = JsonDocument.Parse(relatedKbIds);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in doc.RootElement.EnumerateArray())
                        {
                            if (item.TryGetInt32(out var id) && id > 0)
                            {
                                result.Add(id);
                            }
                        }
                    }
                }
                catch
                {
                    // ignore malformed json
                }
            }

            return result;
        }

        private static string FormatActorName(string? name, string? loginId)
        {
            name = name?.Trim();
            loginId = loginId?.Trim();
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(loginId))
                return $"{name}({loginId})";
            return !string.IsNullOrEmpty(name) ? name
                : !string.IsNullOrEmpty(loginId) ? loginId
                : "알 수 없음";
        }

        private async Task<string> ResolveActorNameAsync()
        {
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

            var actor = User?.Identity?.Name
                ?? User?.FindFirstValue(ClaimTypes.Name)
                ?? User?.FindFirstValue("name")
                ?? User?.FindFirstValue("unique_name");

            return string.IsNullOrWhiteSpace(actor) ? "알 수 없음" : actor.Trim();
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
