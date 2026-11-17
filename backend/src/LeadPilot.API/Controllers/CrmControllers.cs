using LeadPilot.Application.Common;
using LeadPilot.Application.Crm;
using LeadPilot.Application.Notifications;
using LeadPilot.Application.Reports;
using Microsoft.AspNetCore.Mvc;

namespace LeadPilot.API.Controllers;

[ApiController]
[Route("api/v1/crm")]
[Produces("application/json")]
public sealed class CrmController(CrmService crm) : ControllerBase
{
    [HttpGet("pipeline")]
    public async Task<ActionResult<List<PipelineColumn>>> Pipeline([FromQuery] int perStage = 25, CancellationToken ct = default) => Ok(await crm.GetPipelineAsync(Math.Clamp(perStage, 1, 200), ct));

    [HttpPost("activities")]
    public async Task<ActionResult<ActivityDto>> AddActivity(CreateActivityRequest request, CancellationToken ct) => Ok(await crm.AddActivityAsync(request, ct));

    [HttpGet("activities")]
    public async Task<ActionResult<PagedResult<ActivityDto>>> Activities([FromQuery] Guid? leadId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Ok(await crm.ListActivitiesAsync(leadId, page, pageSize, ct));

    [HttpGet("meetings")]
    public async Task<ActionResult<List<MeetingDto>>> Meetings([FromQuery] Guid? leadId, [FromQuery] bool upcoming = false, CancellationToken ct = default) => Ok(await crm.ListMeetingsAsync(leadId, upcoming, ct));

    [HttpPost("meetings")]
    public async Task<ActionResult<object>> CreateMeeting(UpsertMeetingRequest request, CancellationToken ct) => Ok(new { id = await crm.UpsertMeetingAsync(null, request, ct) });

    [HttpPut("meetings/{id:guid}")]
    public async Task<ActionResult<object>> UpdateMeeting(Guid id, UpsertMeetingRequest request, CancellationToken ct) => Ok(new { id = await crm.UpsertMeetingAsync(id, request, ct) });

    [HttpGet("proposals")]
    public async Task<ActionResult<List<ProposalDto>>> Proposals([FromQuery] Guid? leadId, CancellationToken ct) => Ok(await crm.ListProposalsAsync(leadId, ct));

    [HttpPost("proposals")]
    public async Task<ActionResult<object>> CreateProposal(UpsertProposalRequest request, CancellationToken ct) => Ok(new { id = await crm.UpsertProposalAsync(null, request, ct) });

    [HttpPut("proposals/{id:guid}")]
    public async Task<ActionResult<object>> UpdateProposal(Guid id, UpsertProposalRequest request, CancellationToken ct) => Ok(new { id = await crm.UpsertProposalAsync(id, request, ct) });

    [HttpGet("deals")]
    public async Task<ActionResult<List<DealDto>>> Deals([FromQuery] Guid? leadId, CancellationToken ct) => Ok(await crm.ListDealsAsync(leadId, ct));

    [HttpPost("deals")]
    public async Task<ActionResult<object>> CreateDeal(UpsertDealRequest request, CancellationToken ct) => Ok(new { id = await crm.UpsertDealAsync(null, request, ct) });

    [HttpPut("deals/{id:guid}")]
    public async Task<ActionResult<object>> UpdateDeal(Guid id, UpsertDealRequest request, CancellationToken ct) => Ok(new { id = await crm.UpsertDealAsync(id, request, ct) });
}

[ApiController]
[Route("api/v1/followups")]
[Produces("application/json")]
public sealed class FollowUpsController(CrmService crm) : ControllerBase
{
    /// <summary>bucket: today | overdue | upcoming | pending | completed | all</summary>
    [HttpGet]
    public async Task<ActionResult<List<FollowUpDto>>> List([FromQuery] string? bucket, [FromQuery] Guid? leadId, CancellationToken ct) => Ok(await crm.ListFollowUpsAsync(bucket, leadId, ct));

    [HttpPost]
    public async Task<ActionResult<object>> Create(CreateFollowUpRequest request, CancellationToken ct) => Ok(new { id = await crm.CreateFollowUpAsync(request, ct) });

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateFollowUpRequest request, CancellationToken ct)
    {
        await crm.UpdateFollowUpAsync(id, request, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public sealed class NotificationsController(NotificationService notifications, ICurrentUser user) : ControllerBase
{
    [HttpGet("notifications")]
    public async Task<ActionResult<PagedResult<NotificationDto>>> List([FromQuery] bool unreadOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default) =>
        Ok(await notifications.ListAsync(user.RequireUserId(), unreadOnly, page, pageSize, ct));

    [HttpPost("notifications/read")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await notifications.MarkReadAsync(user.RequireUserId(), null, ct);
        return NoContent();
    }

    [HttpPost("notifications/{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(user.RequireUserId(), id, ct);
        return NoContent();
    }

    [HttpPost("devices/register")]
    public async Task<ActionResult<object>> RegisterDevice(RegisterDeviceRequest request, CancellationToken ct) =>
        Ok(new { id = await notifications.RegisterDeviceAsync(user.RequireUserId(), request, ct) });

    [HttpDelete("devices/{id:guid}")]
    public async Task<IActionResult> RemoveDevice(Guid id, CancellationToken ct)
    {
        await notifications.RemoveDeviceAsync(user.RequireUserId(), id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public sealed class DashboardController(DashboardService dashboard, ICurrentUser user) : ControllerBase
{
    [HttpGet("dashboard/summary")]
    public async Task<ActionResult<DashboardSummary>> Summary(CancellationToken ct) => Ok(await dashboard.SummaryAsync(user.RequireUserId(), ct));

    [HttpGet("dashboard/metrics")]
    public async Task<ActionResult<DashboardMetrics>> Metrics([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct) =>
        Ok(await dashboard.MetricsAsync(from?.ToUniversalTime(), to?.ToUniversalTime(), ct));

    [HttpGet("usage")]
    public async Task<ActionResult<UsageSummary>> Usage(CancellationToken ct) => Ok(await dashboard.UsageAsync(ct));
}
