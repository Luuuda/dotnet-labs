using System.Text.Json;
using CatalogService.Bll.Dtos;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace CatalogService.Api.Infrastructure;

/// <summary>Спільні помічники контролерів: явний виклик FluentValidation і заголовки пагінації.</summary>
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// Явна валідація FluentValidation (автоматичний FluentValidation.AspNetCore застарів).
    /// Помилки потрапляють у ModelState і повертаються як ValidationProblemDetails (400) з traceId.
    /// </summary>
    protected async Task<bool> IsValidAsync<T>(IValidator<T> validator, T input, CancellationToken ct)
    {
        var result = await validator.ValidateAsync(input, ct);
        foreach (var error in result.Errors)
            ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
        return result.IsValid;
    }

    /// <summary>Метадані пагінації дублюються в заголовках (X-Total-Count, X-Pagination) — для кешування й проксі.</summary>
    protected void AddPaginationHeaders<T>(PagedResult<T> page)
    {
        Response.Headers["X-Total-Count"] = page.TotalCount.ToString();
        Response.Headers["X-Pagination"] = JsonSerializer.Serialize(new
        {
            page.Page, page.PageSize, page.TotalCount, page.TotalPages, page.HasNext, page.HasPrevious
        });
    }
}
