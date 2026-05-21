using AiDeskApi.Data;
using AiDeskApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiDeskApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ChatbotCategoryController : ControllerBase
    {
        private readonly AiDeskContext _context;

        public ChatbotCategoryController(AiDeskContext context)
        {
            _context = context;
        }

        [AllowAnonymous]
        [HttpGet("nodes")]
        public async Task<IActionResult> GetNodes([FromQuery] int? parentCategoryId = null)
        {
            var items = await QueryChildren(parentCategoryId);

            return Ok(new
            {
                parentCategoryId,
                items
            });
        }

        [AllowAnonymous]
        [HttpPost("select")]
        public async Task<IActionResult> SelectNode([FromBody] ChatbotCategorySelectRequest request)
        {
            if (request.NodeId <= 0)
            {
                return BadRequest(new { error = "유효한 nodeId가 필요합니다." });
            }

            var node = await _context.ChatbotCategories
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CategoryId == request.NodeId && x.UseYN == "Y");

            if (node == null)
            {
                return NotFound(new { error = "선택한 카테고리를 찾을 수 없습니다." });
            }

            var nodeType = NormalizeType(node.Type);
            var canExpand = nodeType == "MENU" || nodeType == "QUESTION";
            var children = canExpand
                ? await QueryChildren(node.CategoryId)
                : new List<ChatbotCategoryNodeDto>();

            var botMessage = string.IsNullOrWhiteSpace(node.Content)
                ? null
                : node.Content!.Trim();

            if (nodeType == "ANSWER" && string.IsNullOrWhiteSpace(botMessage))
            {
                botMessage = node.Title;
            }

            var isTerminal = nodeType == "ANSWER" || children.Count == 0;

            return Ok(new
            {
                node = new
                {
                    categoryId = node.CategoryId,
                    parentCategoryId = node.ParentCategoryId,
                    type = nodeType,
                    title = node.Title,
                    content = node.Content,
                    sortOrder = node.SortOrder
                },
                botMessage,
                showChildren = canExpand,
                children,
                isTerminal
            });
        }

        private async Task<List<ChatbotCategoryNodeDto>> QueryChildren(int? parentCategoryId)
        {
            var rows = await _context.ChatbotCategories
                .AsNoTracking()
                .Where(x => x.UseYN == "Y" && x.ParentCategoryId == parentCategoryId)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.CategoryId)
                .Select(x => new
                {
                    categoryId = x.CategoryId,
                    parentCategoryId = x.ParentCategoryId,
                    type = x.Type,
                    title = x.Title,
                    content = x.Content,
                    sortOrder = x.SortOrder,
                    hasChildren = _context.ChatbotCategories.Any(c => c.UseYN == "Y" && c.ParentCategoryId == x.CategoryId)
                })
                .ToListAsync();

            return rows
                .Select(x => new ChatbotCategoryNodeDto
                {
                    CategoryId = x.categoryId,
                    ParentCategoryId = x.parentCategoryId,
                    Type = NormalizeType(x.type),
                    Title = x.title,
                    Content = x.content,
                    SortOrder = x.sortOrder,
                    HasChildren = x.hasChildren
                })
                .ToList();
        }

        private static string NormalizeType(string? type)
        {
            var value = type?.Trim().ToUpperInvariant() ?? "MENU";
            return value switch
            {
                "MENU" => "MENU",
                "QUESTION" => "QUESTION",
                "ANSWER" => "ANSWER",
                _ => "MENU"
            };
        }
    }

    public class ChatbotCategorySelectRequest
    {
        public int NodeId { get; set; }
    }

    public class ChatbotCategoryNodeDto
    {
        public int CategoryId { get; set; }
        public int? ParentCategoryId { get; set; }
        public string Type { get; set; } = "MENU";
        public string Title { get; set; } = string.Empty;
        public string? Content { get; set; }
        public int SortOrder { get; set; }
        public bool HasChildren { get; set; }
    }
}
