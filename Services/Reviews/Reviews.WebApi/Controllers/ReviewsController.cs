using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Reviews.Application.Common.Models;
using Reviews.Application.Reviews.Commands;
using Reviews.Application.Reviews.Queries;
using Reviews.WebApi.Contracts;

namespace Reviews.WebApi.Controllers;

/// <summary>Відгуки глядачів (колекція reviews) з вкладеними коментарями.</summary>
[Route("api/reviews")]
public sealed class ReviewsController(ILogger<ReviewsController> logger) : ApiControllerBase
{
    /// <summary>
    /// Список відгуків: фільтри (фільм, автор, оцінка, перевірений глядач), повнотекстовий пошук search,
    /// сортування sortBy = newest | oldest | rating | rating_asc | comments, пагінація skip/limit.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ReviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ReviewDto>>> GetList([FromQuery] GetReviewsListQuery query, CancellationToken ct)
    {
        var page = await Sender.Send(query, ct);
        AddPaginationHeaders(page);
        return Ok(page);
    }

    /// <summary>Курсорна стрічка: наступна сторінка — after = nextCursor з попередньої відповіді (без skip).</summary>
    [HttpGet("feed")]
    [ProducesResponseType<CursorPage<ReviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CursorPage<ReviewDto>>> GetFeed([FromQuery] GetReviewsFeedQuery query, CancellationToken ct)
        => Ok(await Sender.Send(query, ct));

    /// <summary>Відгук з коментарями. Версія документа — у заголовку ETag (для If-Match при оновленні).</summary>
    [HttpGet("{id}")]
    [ProducesResponseType<ReviewDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReviewDetailsDto>> GetById(string id, CancellationToken ct)
    {
        var review = await Sender.Send(new GetReviewByIdQuery(id), ct);
        SetETag(review.Version);
        return Ok(review);
    }

    /// <summary>Створити відгук → 201 + Location з рядковим ObjectId. Повторний відгук того самого користувача — 400 (валідатор) / 409 (унікальний індекс).</summary>
    [HttpPost]
    [ProducesResponseType<ReviewDetailsDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReviewDetailsDto>> Create(CreateReviewCommand command, CancellationToken ct)
    {
        var review = await Sender.Send(command, ct);
        SetETag(review.Version);
        Activity.Current?.SetTag("review.id", review.Id);
        Activity.Current?.SetTag("movie.id", review.MovieId);
        logger.LogInformation("API: відгук {ReviewId} на фільм {MovieId} опубліковано, оцінка {Rating}", review.Id, review.MovieId, review.Rating);
        return CreatedAtAction(nameof(GetById), new { id = review.Id }, review);
    }

    /// <summary>Редагувати відгук. Потрібна версія: заголовок If-Match (ETag з GET) або поле version. Застаріла версія — 409.</summary>
    [HttpPut("{id}")]
    [ProducesResponseType<ReviewDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReviewDetailsDto>> Update(string id, UpdateReviewRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken ct)
    {
        var review = await Sender.Send(MapCommand<UpdateReviewCommand>(request, id, IfMatchVersion()), ct);
        SetETag(review.Version);
        Activity.Current?.SetTag("review.id", review.Id);
        logger.LogInformation("API: відгук {ReviewId} оновлено до версії {Version}", review.Id, review.Version);
        return Ok(review);
    }

    /// <summary>Видалити відгук (з If-Match — лише якщо версія збігається).</summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(string id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken ct)
    {
        await Sender.Send(new DeleteReviewCommand(id, IfMatchVersion()), ct);
        Activity.Current?.SetTag("review.id", id);
        logger.LogInformation("API: відгук {ReviewId} видалено", id);
        return NoContent();
    }

    /// <summary>Додати коментар (вкладений документ у масиві comments).</summary>
    [HttpPost("{id}/comments")]
    [ProducesResponseType<CommentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CommentDto>> AddComment(string id, AddCommentRequest request, CancellationToken ct)
    {
        var comment = await Sender.Send(MapCommand<AddCommentCommand>(request, id), ct);
        return CreatedAtAction(nameof(GetById), new { id }, comment);
    }

    /// <summary>Видалити коментар.</summary>
    [HttpDelete("{id}/comments/{commentId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveComment(string id, string commentId, CancellationToken ct)
    {
        await Sender.Send(new RemoveCommentCommand(id, commentId), ct);
        return NoContent();
    }
}
