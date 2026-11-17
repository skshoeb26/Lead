using LeadPilot.Application.Common;
using LeadPilot.Application.Discovery;
using LeadPilot.Application.Pitches;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LeadPilot.API.Controllers;

[ApiController]
[Route("api/v1/discovery")]
[Produces("application/json")]
public sealed class DiscoveryController(DiscoveryService discovery) : ControllerBase
{
    [HttpGet("providers")]
    public ActionResult<IReadOnlyList<ProviderStatusDto>> Providers() => Ok(discovery.ProviderStatus());

    /// <summary>Estimates request count and cost before creating a job.</summary>
    [HttpPost("estimate")]
    public async Task<ActionResult<DiscoveryEstimate>> Estimate(CreateDiscoveryJobRequest request, CancellationToken ct) => Ok(await discovery.EstimateAsync(request, ct));

    [HttpPost("jobs"), EnableRateLimiting("expensive")]
    public async Task<ActionResult<DiscoveryJobDto>> Create(CreateDiscoveryJobRequest request, CancellationToken ct)
    {
        var job = await discovery.CreateJobAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = job.Id }, job);
    }

    [HttpGet("jobs")]
    public async Task<ActionResult<PagedResult<DiscoveryJobDto>>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await discovery.ListAsync(page, pageSize, ct));

    [HttpGet("jobs/{id:guid}")]
    public async Task<ActionResult<DiscoveryJobDto>> Get(Guid id, CancellationToken ct) => Ok(await discovery.GetAsync(id, ct));

    [HttpGet("jobs/{id:guid}/results")]
    public async Task<ActionResult<PagedResult<DiscoveryResultDto>>> Results(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Ok(await discovery.ResultsAsync(id, page, pageSize, ct));

    [HttpPost("jobs/{id:guid}/pause")]
    public async Task<ActionResult<DiscoveryJobDto>> Pause(Guid id, CancellationToken ct) => Ok(await discovery.PauseAsync(id, ct));

    [HttpPost("jobs/{id:guid}/resume")]
    public async Task<ActionResult<DiscoveryJobDto>> Resume(Guid id, CancellationToken ct) => Ok(await discovery.ResumeAsync(id, ct));

    [HttpPost("jobs/{id:guid}/cancel")]
    public async Task<ActionResult<DiscoveryJobDto>> Cancel(Guid id, CancellationToken ct) => Ok(await discovery.CancelAsync(id, ct));
}

[ApiController]
[Route("api/v1/pitches")]
[Produces("application/json")]
public sealed class PitchesController(PitchService pitches) : ControllerBase
{
    [HttpPost("generate"), EnableRateLimiting("expensive")]
    public async Task<ActionResult<PitchDto>> Generate(GeneratePitchRequest request, CancellationToken ct) => Ok(await pitches.GenerateAsync(request, ct));

    [HttpGet]
    public async Task<ActionResult<PagedResult<PitchDto>>> List([FromQuery] Guid? leadId, [FromQuery] LeadPilot.Domain.PitchType? type, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await pitches.ListAsync(leadId, type, page, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PitchDto>> Get(Guid id, CancellationToken ct) => Ok(await pitches.GetAsync(id, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PitchDto>> Update(Guid id, UpdatePitchRequest request, CancellationToken ct) => Ok(await pitches.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await pitches.ArchiveAsync(id, ct);
        return NoContent();
    }
}
