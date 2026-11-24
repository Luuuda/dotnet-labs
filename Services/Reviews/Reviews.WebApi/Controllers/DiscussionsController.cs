using Microsoft.AspNetCore.Mvc;
using Reviews.Application.Common.Models;
using Reviews.Application.Discussions.Commands;
using Reviews.Application.Discussions.Queries;
using Reviews.WebApi.Contracts;
using Reviews.WebApi.Metrics;

namespace Reviews.WebApi.Controllers;

/// <summary>Теми обговорень фільмів (колекція discussions).</summary>
[Route("api/discussions")]
public sealed class DiscussionsController(ReviewsMetrics metrics) : ApiControllerBase
{
    /// <summary>Теми з фільтрами (фільм, статус open/closed, тег), найактивніші зверху.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<DiscussionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<DiscussionDto>>> GetList([FromQuery] GetDiscussionsListQuery query, CancellationToken ct)
    {
        var page = await Sender.Send(query, ct);
        AddPaginationHeaders(page);
        return Ok(page);
    }

    [HttpGet("{id}")]
    [ProducesResponseType<DiscussionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DiscussionDto>> GetById(string id, CancellationToken ct)
    {
        var discussion = await Sender.Send(new GetDiscussionByIdQuery(id), ct);
        SetETag(discussion.Version);
        return Ok(discussion);
    }

    [HttpPost]
    [ProducesResponseType<DiscussionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DiscussionDto>> Create(CreateDiscussionCommand command, CancellationToken ct)
    {
        var discussion = await metrics.MeasureAsync("discussion_create", () => Sender.Send(command, ct));
        metrics.DiscussionCreated();
        SetETag(discussion.Version);
        return CreatedAtAction(nameof(GetById), new { id = discussion.Id }, discussion);
    }

    /// <summary>Закрити тему. Повторне закриття — 400 (порушення доменного правила), застаріла версія — 409.</summary>
    [HttpPost("{id}/close")]
    [ProducesResponseType<DiscussionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DiscussionDto>> Close(string id, CloseDiscussionRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken ct)
    {
        var discussion = await metrics.MeasureAsync("discussion_close",
            () => Sender.Send(MapCommand<CloseDiscussionCommand>(request, id, IfMatchVersion()), ct));
        metrics.DiscussionClosed();
        SetETag(discussion.Version);
        return Ok(discussion);
    }

    [HttpPost("{id}/reopen")]
    [ProducesResponseType<DiscussionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DiscussionDto>> Reopen(string id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken ct)
    {
        var discussion = await metrics.MeasureAsync("discussion_reopen",
            () => Sender.Send(new ReopenDiscussionCommand(id, IfMatchVersion()), ct));
        SetETag(discussion.Version);
        return Ok(discussion);
    }
}
