using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AiDeskApi.Data;
using AiDeskApi.Models;
using AiDeskApi.Services;

namespace AiDeskApi.Controllers
{
    // 상담 CRUD와 요약 기능을 담당하는 컨트롤러
    [ApiController]
    [Route("api/[controller]")]
    public class InteractionController : ControllerBase
    {
        private readonly AiDeskContext _context;
        private readonly IGeminiService _geminiService;
        private readonly IGptService _gptService;
        private readonly ISummaryPromptTemplateService _promptTemplates;

        public InteractionController(
            AiDeskContext context,
            IGeminiService geminiService,
            IGptService gptService,
            ISummaryPromptTemplateService promptTemplates)
        {
            _context = context;
            _geminiService = geminiService;
            _gptService = gptService;
            _promptTemplates = promptTemplates;
        }

        // GET: api/interaction
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Interaction>>> GetInteractions()
        {
            return await _context.Interactions
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync();
        }

        // GET: api/interaction/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<Interaction>> GetInteraction(int id)
        {
            var interaction = await _context.Interactions.FindAsync(id);
            if (interaction == null)
            {
                return NotFound();
            }
            return interaction;
        }

        // POST: api/interaction
        [HttpPost]
        public async Task<ActionResult<Interaction>> CreateInteraction(Interaction interaction)
        {
            // 상담은 고객 소속 데이터이므로 고객 존재를 먼저 검증
            var customer = await _context.Customers.FindAsync(interaction.CustomerId);
            if (customer == null)
            {
                return BadRequest("Customer not found");
            }

            interaction.CreatedAt = DateTime.UtcNow;
            customer.LastContactDate = DateTime.UtcNow;

            _context.Interactions.Add(interaction);
            _context.Customers.Update(customer);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetInteraction), new { id = interaction.Id }, interaction);
        }

        // PUT: api/interaction/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateInteraction(int id, Interaction interaction)
        {
            if (id != interaction.Id)
            {
                return BadRequest();
            }

            var existingInteraction = await _context.Interactions.FindAsync(id);
            if (existingInteraction == null)
            {
                return NotFound();
            }

            existingInteraction.Type = interaction.Type;
            existingInteraction.Content = interaction.Content;
            existingInteraction.Outcome = interaction.Outcome;
            existingInteraction.IsCompleted = interaction.IsCompleted;
            existingInteraction.ScheduledDate = interaction.ScheduledDate;
            existingInteraction.IsExternalProvided = interaction.IsExternalProvided;

            _context.Interactions.Update(existingInteraction);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // DELETE: api/interaction/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteInteraction(int id)
        {
            var interaction = await _context.Interactions.FindAsync(id);
            if (interaction == null)
            {
                return NotFound();
            }

            _context.Interactions.Remove(interaction);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // PATCH: api/interaction/{id}/complete
        [HttpPatch("{id}/complete")]
        public async Task<IActionResult> CompleteInteraction(int id)
        {
            var interaction = await _context.Interactions.FindAsync(id);
            if (interaction == null)
            {
                return NotFound();
            }

            interaction.IsCompleted = true;
            _context.Interactions.Update(interaction);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // POST: api/interaction/summarize - 사전 정리 (저장 전)
        [HttpPost("summarize")]
        public async Task<ActionResult<SummaryResponse>> PreviewSummarize([FromBody] PreviewSummarizeRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Content))
            {
                return BadRequest("상담 내용을 입력해 주세요.");
            }

            try
            {
                var summary = await SummarizeSingleAsync(request.Provider, request.Content, request.Type ?? "Call");
                return Ok(new SummaryResponse { Summary = summary });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // POST: api/interaction/{id}/summarize - 저장된 상담 정리
        [HttpPost("{id}/summarize")]
        public async Task<ActionResult<SummaryResponse>> SummarizeInteraction(int id, [FromQuery] string? provider = null)
        {
            var interaction = await _context.Interactions.FindAsync(id);
            if (interaction == null)
            {
                return NotFound("상담 기록을 찾을 수 없습니다.");
            }

            try
            {
                var summary = await SummarizeSingleAsync(provider, interaction.Content ?? string.Empty, interaction.Type ?? "Call");
                return Ok(new SummaryResponse { Summary = summary });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // POST: api/interaction/customer/{customerId}/summarize-all - 업체별 전체 이력 요약
        [HttpPost("customer/{customerId}/summarize-all")]
        public async Task<ActionResult<SummaryResponse>> SummarizeAllInteractions(int customerId, [FromQuery] string? provider = null)
        {
            var customer = await _context.Customers.FindAsync(customerId);
            if (customer == null)
            {
                return NotFound("고객을 찾을 수 없습니다.");
            }

            var interactions = await _context.Interactions
                .Where(i => i.CustomerId == customerId)
                .OrderBy(i => i.CreatedAt)
                .ToListAsync();

            if (interactions.Count == 0)
            {
                return BadRequest("상담 이력이 없습니다.");
            }

            try
            {
                var consultationDataList = interactions.Select(i => new ConsultationData
                {
                    Id = i.Id,
                    CustomerId = i.CustomerId,
                    Type = i.Type ?? "Call",
                    Content = i.Content ?? string.Empty,
                    CreatedAt = i.CreatedAt,
                    Outcome = i.Outcome,
                    ScheduledDate = i.ScheduledDate,
                    IsCompleted = i.IsCompleted
                }).ToList();

                var companyData = new CompanyData
                {
                    Id = customer.Id,
                    Name = customer.Name ?? "Unknown",
                    PhoneNumber = customer.PhoneNumber,
                    Email = customer.Email,
                    Company = customer.Company,
                    Position = customer.Position
                };

                var summary = await SummarizeAllAsync(provider, consultationDataList, companyData);
                return Ok(new SummaryResponse { Summary = summary });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET: api/interaction/prompt-template
        [HttpGet("prompt-template")]
        public ActionResult<SummaryPromptTemplateResponse> GetPromptTemplate()
        {
            return Ok(new SummaryPromptTemplateResponse
            {
                SingleConsultationTemplate = _promptTemplates.SingleConsultationTemplate,
                AllConsultationsTemplate = _promptTemplates.AllConsultationsTemplate
            });
        }

        // PUT: api/interaction/prompt-template
        [HttpPut("prompt-template")]
        public ActionResult<SummaryPromptTemplateResponse> UpdatePromptTemplate([FromBody] UpdateSummaryPromptTemplateRequest request)
        {
            try
            {
                _promptTemplates.Update(request.SingleConsultationTemplate, request.AllConsultationsTemplate);

                return Ok(new SummaryPromptTemplateResponse
                {
                    SingleConsultationTemplate = _promptTemplates.SingleConsultationTemplate,
                    AllConsultationsTemplate = _promptTemplates.AllConsultationsTemplate
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        private async Task<string> SummarizeSingleAsync(string? provider, string content, string type)
        {
            var normalized = (provider ?? "gpt").Trim().ToLowerInvariant();
            return normalized switch
            {
                "gpt" or "openai" => await _gptService.SummarizeConsultationAsync(content, type),
                _ => await _geminiService.SummarizeConsultationAsync(content, type)
            };
        }

        private async Task<string> SummarizeAllAsync(string? provider, List<ConsultationData> consultations, CompanyData company)
        {
            var normalized = (provider ?? "gpt").Trim().ToLowerInvariant();
            return normalized switch
            {
                "gpt" or "openai" => await _gptService.SummarizeAllConsultationsAsync(consultations, company),
                _ => await _geminiService.SummarizeAllConsultationsAsync(consultations, company)
            };
        }
    }

    // DTO for summary response
    public class SummaryResponse
    {
        public string Summary { get; set; } = string.Empty;
    }

    // DTO for preview summarize request
    public class PreviewSummarizeRequest
    {
        public string Content { get; set; } = string.Empty;
        public string? Type { get; set; }
        public string? Provider { get; set; }
    }

    public class SummaryPromptTemplateResponse
    {
        public string SingleConsultationTemplate { get; set; } = string.Empty;
        public string AllConsultationsTemplate { get; set; } = string.Empty;
    }

    public class UpdateSummaryPromptTemplateRequest
    {
        public string SingleConsultationTemplate { get; set; } = string.Empty;
        public string AllConsultationsTemplate { get; set; } = string.Empty;
    }

}

