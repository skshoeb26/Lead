using FluentValidation;
using LeadPilot.Application.Analysis;
using LeadPilot.Application.Common;
using LeadPilot.Application.Crm;
using LeadPilot.Application.Intelligence;
using LeadPilot.Application.Leads;
using LeadPilot.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LeadPilot.API.Controllers;

[ApiController]
[Route("api/v1/leads")]
[Produces("application/json")]
public sealed class LeadsController(LeadService leads, CsvLeadService csv, LeadAnalysisPipeline pipeline, CrmService crm, IValidator<CreateLeadRequest> createValidator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<LeadListItem>>> List([FromQuery] LeadQuery query, CancellationToken ct) => Ok(await leads.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LeadDetail>> Get(Guid id, CancellationToken ct) => Ok(await leads.GetAsync(id, ct));

    public sealed record CreateLeadResponse(Guid Id, IReadOnlyList<DuplicateCandidate> PossibleDuplicates);

    [HttpPost]
    public async Task<ActionResult<CreateLeadResponse>> Create(CreateLeadRequest request, CancellationToken ct)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        var (id, dups) = await leads.CreateAsync(request, "manual", ct);
        return CreatedAtAction(nameof(Get), new { id }, new CreateLeadResponse(id, dups));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateLeadRequest request, CancellationToken ct)
    {
        await leads.UpdateAsync(id, request, ct);
        return NoContent();
    }

    /// <summary>Soft delete (recoverable by an administrator).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await leads.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Permanent erasure of a prospect's data (data-subject request). Optionally adds their contacts to the suppression list.</summary>
    [HttpDelete("{id:guid}/erase")]
    public async Task<IActionResult> Erase(Guid id, [FromQuery] bool suppress = true, CancellationToken ct = default)
    {
        await leads.EraseAsync(id, suppress, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/contacts")]
    public async Task<ActionResult<ContactDto>> AddContact(Guid id, AddContactRequest request, CancellationToken ct) => Ok(await leads.AddContactAsync(id, request, ct));

    [HttpDelete("{id:guid}/contacts/{contactId:guid}")]
    public async Task<IActionResult> RemoveContact(Guid id, Guid contactId, CancellationToken ct)
    {
        await leads.RemoveContactAsync(id, contactId, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/opportunities/{opportunityId:guid}/dismiss")]
    public async Task<IActionResult> DismissOpportunity(Guid id, Guid opportunityId, CancellationToken ct)
    {
        await leads.DismissOpportunityAsync(id, opportunityId, ct);
        await pipeline.ScoreAsync(id, ct);
        return NoContent();
    }

    /// <summary>Merge the duplicate lead into this one.</summary>
    [HttpPost("{id:guid}/merge")]
    public async Task<IActionResult> Merge(Guid id, MergeLeadRequest request, CancellationToken ct)
    {
        await leads.MergeAsync(id, request.DuplicateLeadId, ct);
        return NoContent();
    }

    [HttpGet("duplicates")]
    public async Task<ActionResult<List<DuplicatePairDto>>> Duplicates([FromQuery] int max = 100, CancellationToken ct = default) =>
        Ok(await leads.ListDuplicatePairsAsync(Math.Clamp(max, 1, 500), ct));

    [HttpGet("{id:guid}/duplicates")]
    public async Task<ActionResult<List<DuplicateCandidate>>> DuplicatesOf(Guid id, CancellationToken ct)
    {
        var l = await leads.GetAsync(id, ct);
        return Ok(await leads.FindDuplicatesAsync(new DuplicateProbe(l.SourceProvider, l.ProviderPlaceId, l.BusinessName, l.WebsiteUrl, l.Phone, l.City, l.PinCode), id, ct));
    }

    [HttpGet("{id:guid}/activities")]
    public async Task<ActionResult<PagedResult<ActivityDto>>> Activities(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Ok(await crm.ListActivitiesAsync(id, page, pageSize, ct));

    [HttpPost("{id:guid}/stage")]
    public async Task<IActionResult> ChangeStage(Guid id, ChangeStageRequest request, CancellationToken ct)
    {
        await crm.ChangeStageAsync(id, request, ct);
        return NoContent();
    }

    // ---------------- analysis

    [HttpPost("{id:guid}/analyse"), EnableRateLimiting("expensive")]
    public async Task<ActionResult<AnalysisQueuedDto>> Analyse(Guid id, [FromBody] AnalyseRequest? request, CancellationToken ct) =>
        Accepted(await pipeline.QueueAsync(id, request ?? new AnalyseRequest(), ct));

    public sealed record BulkAnalyseRequest(List<Guid> LeadIds, AnalyseRequest Options);

    [HttpPost("analyse/bulk"), EnableRateLimiting("expensive")]
    public async Task<IActionResult> AnalyseBulk(BulkAnalyseRequest request, CancellationToken ct) =>
        Accepted(new { queued = await pipeline.QueueBulkAsync(request.LeadIds, request.Options, ct) });

    [HttpGet("{id:guid}/analysis")]
    public async Task<ActionResult<object>> Analysis(Guid id, CancellationToken ct)
    {
        var l = await leads.GetAsync(id, ct);
        return Ok(new { l.WebsiteAnalysisStatus, l.AIAnalysisStatus, l.LatestWebsiteAnalysis, l.LatestAIAnalysis, l.Opportunities, l.LatestScore });
    }

    [HttpPost("{id:guid}/score")]
    public async Task<ActionResult<ScoreBreakdown>> Score(Guid id, CancellationToken ct) => Ok(await pipeline.ScoreAsync(id, ct));

    // ---------------- CSV

    [HttpPost("import"), RequestSizeLimit(CsvLeadService.MaxBytes + 4096)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<CsvImportResult>> Import(IFormFile file, [FromForm] bool skipDuplicates = true, CancellationToken ct = default)
    {
        if (file.Length == 0 || file.Length > CsvLeadService.MaxBytes) throw new ValidationFailedException("CSV file must be between 1 byte and 2 MB.");
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".csv") throw new ValidationFailedException("Only .csv files are accepted.");
        await using var stream = file.OpenReadStream();
        return Ok(await csv.ImportAsync(stream, file.FileName, skipDuplicates, ct));
    }

    [HttpGet("import/template")]
    public IActionResult ImportTemplate() =>
        File(System.Text.Encoding.UTF8.GetBytes(string.Join(',', CsvLeadService.Columns) + "\nABC Coaching Classes,Coaching institutes,https://abccoaching.example,+91 98765 43210,info@abccoaching.example,12 MG Road,Andheri,Mumbai,Mumbai Suburban,Maharashtra,400053,https://abccoaching.example/contact,,\n"),
            "text/csv", "leadpilot-import-template.csv");

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] LeadQuery query, CancellationToken ct) =>
        File(await csv.ExportAsync(query, ct), "text/csv", $"leadpilot-leads-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
}

[ApiController]
[Route("api/v1/categories")]
public sealed class CategoriesController(LeadService leads) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CategoryDto>>> List([FromQuery] bool includeInactive = false, CancellationToken ct = default) => Ok(await leads.ListCategoriesAsync(includeInactive, ct));

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create(UpsertCategoryRequest request, CancellationToken ct) => Ok(await leads.UpsertCategoryAsync(null, request, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CategoryDto>> Update(Guid id, UpsertCategoryRequest request, CancellationToken ct) => Ok(await leads.UpsertCategoryAsync(id, request, ct));
}
