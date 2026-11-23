using System.Globalization;
using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Reviews.Application.Common.Models;
using Reviews.WebApi.Contracts;

namespace Reviews.WebApi.Controllers;

/// <summary>
/// Базовий контролер: доступ до ISender (MediatR) і IMapper. Контролери не містять бізнес-логіки
/// й не знають про MongoDB — лише формують команду/запит і повертають HTTP-статус.
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender? _sender;
    private IMapper? _mapper;

    protected ISender Sender => _sender ??= HttpContext.RequestServices.GetRequiredService<ISender>();
    protected IMapper Mapper => _mapper ??= HttpContext.RequestServices.GetRequiredService<IMapper>();

    /// <summary>Версія документа з If-Match: "3", W/"3" або 3. Якщо заголовка немає — null.</summary>
    protected int? IfMatchVersion()
    {
        var raw = Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var value = raw.Trim();
        if (value.StartsWith("W/", StringComparison.Ordinal)) value = value[2..];
        value = value.Trim('"');
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) ? version : -1;
    }

    protected void SetETag(int version) => Response.Headers.ETag = $"\"{version}\"";

    protected TCommand MapCommand<TCommand>(object request, string id, int? version = null)
        => Mapper.Map<TCommand>(request, opts =>
        {
            opts.Items[RequestMappingProfile.IdKey] = id;
            if (version is { } v) opts.Items[RequestMappingProfile.VersionKey] = v;
        });

    protected void AddPaginationHeaders<T>(PagedResult<T> page)
    {
        Response.Headers["X-Total-Count"] = page.TotalCount.ToString(CultureInfo.InvariantCulture);
        Response.Headers["X-Pagination"] =
            $"page={page.Page};pageSize={page.PageSize};totalPages={page.TotalPages};hasNext={page.HasNext.ToString().ToLowerInvariant()}";
    }
}
