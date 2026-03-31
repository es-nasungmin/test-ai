using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CrmApi.Data;
using CrmApi.Models;
using CrmApi.Services;

namespace CrmApi.Controllers
{
    // 상담 CRUD와 저장 후 KB 자동추출 파이프라인을 담당하는 컨트롤러
    [ApiController]
    [Route("api/[controller]")]
    public class InteractionController : ControllerBase
    {
        private readonly CrmContext _context;
        private readonly IGeminiService _geminiService;
        private readonly IGptService _gptService;
        private readonly ISummaryPromptTemplateService _promptTemplates;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<InteractionController> _logger;

        public InteractionController(
            CrmContext context,
            IGeminiService geminiService,
            IGptService gptService,
            ISummaryPromptTemplateService promptTemplates,
            IServiceScopeFactory scopeFactory,
            ILogger<InteractionController> logger)
        {
            _context = context;
            _geminiService = geminiService;
            _gptService = gptService;
            _promptTemplates = promptTemplates;
            _scopeFactory = scopeFactory;
            _logger = logger;
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

            // 백그라운드에서 KB 자동 추출 (저장 완료 후 비동기)
            _ = Task.Run(() => ExtractKbAsync(interaction.Id));

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

            // 상담 내용 변경 시 KB 재추출
            _ = Task.Run(() => ExtractKbAsync(existingInteraction.Id));

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

        // 내부 메서드: KB 자동 추출 및 저장
        private async Task ExtractKbAsync(int interactionId)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var scopedContext = scope.ServiceProvider.GetRequiredService<CrmContext>();
                var extractor = scope.ServiceProvider.GetRequiredService<IKnowledgeExtractorService>();
                var embedding = scope.ServiceProvider.GetRequiredService<IEmbeddingService>();

                var interaction = await scopedContext.Interactions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(i => i.Id == interactionId);

                if (interaction == null)
                {
                    _logger.LogWarning($"⚠️ 상담 #{interactionId}를 찾을 수 없어 KB 추출을 건너뜁니다.");
                    return;
                }

                var extracted = await extractor.ExtractFromInteractionAsync(interaction);
                if (string.IsNullOrWhiteSpace(extracted.Problem)) return;

                var embeddingVec = await embedding.EmbedTextAsync(extracted.Problem);

                // 같은 상담내역으로 이미 추출된 KB가 있으면 업데이트
                var existing = await scopedContext.KnowledgeBases
                    .FirstOrDefaultAsync(k => k.SourceInteractionId == interaction.Id);

                if (existing != null)
                {
                    existing.Problem = extracted.Problem;
                    existing.Solution = extracted.Solution;
                    existing.ProblemEmbedding = System.Text.Json.JsonSerializer.Serialize(embeddingVec);
                    existing.Visibility = interaction.IsExternalProvided ? "common" : "internal";
                    existing.IsApproved = interaction.IsExternalProvided;
                    existing.ApprovedAt = interaction.IsExternalProvided ? DateTime.UtcNow : null;
                    existing.ApprovedBy = interaction.IsExternalProvided ? "interaction-external" : null;
                    scopedContext.KnowledgeBases.Update(existing);
                }
                else
                {
                    scopedContext.KnowledgeBases.Add(new KnowledgeBase
                    {
                        SourceInteractionId = interaction.Id,
                        Problem = extracted.Problem,
                        Solution = extracted.Solution,
                        ProblemEmbedding = System.Text.Json.JsonSerializer.Serialize(embeddingVec),
                        SourceType = "case",
                        Visibility = interaction.IsExternalProvided ? "common" : "internal",
                        IsApproved = interaction.IsExternalProvided,
                        ApprovedAt = interaction.IsExternalProvided ? DateTime.UtcNow : null,
                        ApprovedBy = interaction.IsExternalProvided ? "interaction-external" : null,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                await scopedContext.SaveChangesAsync();
                _logger.LogInformation($"✅ 상담 #{interaction.Id} KB 자동 추출 완료");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"⚠️ 상담 #{interactionId} KB 자동 추출 실패 (무시됨): {ex.Message}");
            }
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

        // PATCH: api/interaction/{id}/external-provide
        [HttpPatch("{id}/external-provide")]
        public async Task<IActionResult> SetExternalProvided(int id, [FromBody] ExternalProvideRequest request)
        {
            var interaction = await _context.Interactions.FindAsync(id);
            if (interaction == null)
            {
                return NotFound();
            }

            interaction.IsExternalProvided = request.IsExternalProvided;
            _context.Interactions.Update(interaction);

            var kb = await _context.KnowledgeBases.FirstOrDefaultAsync(k => k.SourceInteractionId == id);
            if (kb != null)
            {
                kb.Visibility = request.IsExternalProvided ? "common" : "internal";
                kb.IsApproved = request.IsExternalProvided;
                kb.ApprovedAt = request.IsExternalProvided ? DateTime.UtcNow : null;
                kb.ApprovedBy = request.IsExternalProvided ? "interaction-external" : null;
                _context.KnowledgeBases.Update(kb);
            }

            await _context.SaveChangesAsync();
            return Ok(new { interaction.Id, interaction.IsExternalProvided });
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

    public class ExternalProvideRequest
    {
        public bool IsExternalProvided { get; set; }
    }
}
