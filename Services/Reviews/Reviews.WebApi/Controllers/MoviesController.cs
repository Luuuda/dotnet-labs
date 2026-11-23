using Microsoft.AspNetCore.Mvc;
using Reviews.Application.Common.Models;
using Reviews.Application.Movies.Queries;
using Reviews.Application.Reviews.Commands;
using Reviews.WebApi.Contracts;

namespace Reviews.WebApi.Controllers;

/// <summary>Дані про фільми, обчислені з відгуків.</summary>
[Route("api/movies")]
public sealed class MoviesController : ApiControllerBase
{
    /// <summary>Рейтинг фільму: середня оцінка, кількість і розподіл (aggregation pipeline).</summary>
    [HttpGet("{movieId:int}/rating")]
    [ProducesResponseType<MovieRatingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MovieRatingDto>> GetRating(int movieId, CancellationToken ct)
        => Ok(await Sender.Send(new GetMovieRatingQuery(movieId), ct));

    /// <summary>
    /// Фільм перейменовано в Каталозі (у реальній системі — подія MovieRenamed з брокера):
    /// оновити денормалізовану назву в усіх відгуках (updateMany). Повертає кількість змінених документів.
    /// </summary>
    [HttpPut("{movieId:int}/title")]
    [ProducesResponseType<long>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<long>> Rename(int movieId, RenameMovieRequest request, CancellationToken ct)
        => Ok(await Sender.Send(new RenameMovieCommand(movieId, request.MovieTitle), ct));
}
