using System.Diagnostics;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Models;

namespace Platform.Aggregator.Services;

public static class AggregatorTelemetry
{
    /// <summary>Власні spans агрегації (ServiceDefaults підписується на Cinema.*).</summary>
    public static readonly ActivitySource Source = new("Cinema.Aggregator");
}

/// <summary>Без бронювання агрегувати нічого: booking-api недоступний → 503, а не 500.</summary>
public sealed class RequiredServiceUnavailableException(string service, Exception inner)
    : Exception($"{service} недоступний — без нього відповідь сформувати неможливо", inner)
{
    public string Service { get; } = service;
}

/// <summary>
/// Лаб. 06: джерела передаються через <see cref="ServiceSources"/> — за замовчуванням gRPC (паралельні виклики
/// мультиплексуються в одному з'єднанні HTTP/2), або HTTP з Лаб. 05 для порівняння затримок.
/// Картка бронювання: booking-api (обов'язково) → паралельно: актуальні сеанси й фільми з catalog-api
/// та рейтинг і відгуки з reviews-api (Task.WhenAll). Недоступний catalog-api чи reviews-api → часткова відповідь.
/// </summary>
public sealed class BookingCardAggregator(ServiceSources sources, ILogger<BookingCardAggregator> logger)
{
    private readonly IBookingSource bookings = sources.Bookings;
    private readonly ICatalogSource catalog = sources.Catalog;
    private readonly IReviewsSource reviews = sources.Reviews;

    private const int TopReviews = 3;

    public async Task<BookingCardDto?> GetAsync(long bookingId, CancellationToken ct)
    {
        using var activity = AggregatorTelemetry.Source.StartActivity("AggregateBookingCard");
        activity?.SetTag("booking.id", bookingId);
        activity?.SetTag("aggregation.transport", sources.Transport);
        var partial = new PartialResults(logger, ct);

        // 1. Бронювання — основа відповіді
        BookingDto? booking;
        try
        {
            booking = await bookings.GetBookingAsync(bookingId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            throw new RequiredServiceUnavailableException(bookings.ServiceName, ex);
        }
        if (booking is null) return null;

        var screeningIds = booking.Tickets.Select(t => t.ScreeningId).Distinct().ToList();

        // 2. Паралельно: актуальні сеанси з Каталогу і локальні копії сеансів у Бронюванні (там є MovieId,
        //    навіть якщо Каталог недоступний) — щоб знати, про які фільми питати Каталог і Відгуки.
        var liveTasks = screeningIds.ToDictionary(id => id, id =>
            partial.SafeAsync(catalog.ServiceName, $"сеанс {id}", () => catalog.GetScreeningAsync(id, ct)));
        var copyTasks = screeningIds.ToDictionary(id => id, id =>
            partial.SafeAsync(bookings.ServiceName, $"копія сеансу {id}", () => bookings.GetScreeningCopyAsync(id, ct)));
        await Task.WhenAll(liveTasks.Values.Cast<Task>().Concat(copyTasks.Values));

        var movieByScreening = new Dictionary<long, int>();
        foreach (var id in screeningIds)
        {
            var live = await liveTasks[id];
            var copy = await copyTasks[id];
            // перевірка узгодженості: сеанс у Каталозі має відповідати тому, що збережено в бронюванні
            if (live is not null && copy is not null && live.MovieId != copy.MovieId)
                partial.Warn($"Сеанс {id}: фільм у Каталозі ({live.MovieTitle}) не збігається з бронюванням ({copy.MovieTitle})");
            if (live?.IsCancelled == true && booking.Status is not ("Cancelled" or "Expired"))
                partial.Warn($"Сеанс {id} скасовано в Каталозі — бронювання {booking.Id} потребує повернення коштів");
            if ((live?.MovieId ?? copy?.MovieId) is { } movieId) movieByScreening[id] = movieId;
        }

        // 3. Паралельно для кожного фільму: опис (catalog-api) + рейтинг і найкращі відгуки (reviews-api)
        var movieIds = movieByScreening.Values.Distinct().ToList();
        var movieTasks = movieIds.ToDictionary(id => id, id =>
            partial.SafeAsync(catalog.ServiceName, $"фільм {id}", () => catalog.GetMovieAsync(id, ct)));
        var ratingTasks = movieIds.ToDictionary(id => id, id =>
            partial.SafeAsync(reviews.ServiceName, $"рейтинг фільму {id}", () => reviews.GetRatingAsync(id, ct)));
        var reviewTasks = movieIds.ToDictionary(id => id, id =>
            partial.SafeAsync(reviews.ServiceName, $"відгуки на фільм {id}", () => reviews.GetTopReviewsAsync(id, TopReviews, ct)));
        await Task.WhenAll(movieTasks.Values.Cast<Task>().Concat(ratingTasks.Values).Concat(reviewTasks.Values));

        // 4. Збирання картки: лише сеанси, що належать цьому бронюванню
        var reviewsFailed = partial.Warnings.Any(w => w.StartsWith(reviews.ServiceName, StringComparison.Ordinal));
        var sessions = new List<SessionCard>();
        foreach (var group in booking.Tickets.Where(t => !t.IsCancelled).GroupBy(t => t.ScreeningId))
        {
            var first = group.First();
            var live = await liveTasks[group.Key];
            MovieInfo? movie = null;
            MovieOpinion? opinion = null;
            if (movieByScreening.TryGetValue(group.Key, out var movieId))
            {
                movie = ToMovieInfo(await movieTasks[movieId]);
                var rating = await ratingTasks[movieId];
                var top = await reviewTasks[movieId];
                opinion = ToOpinion(rating, top, reviewsAvailable: rating is not null || top is not null || !reviewsFailed);
            }

            sessions.Add(new SessionCard(
                group.Key, first.MovieTitle, first.StartsAt, first.HallName,
                group.Select(t => $"ряд {t.SeatRow}, місце {t.SeatNumber}").ToList(),
                group.Sum(t => t.FinalPrice),
                live is null ? null : new ScreeningLiveInfo(live.Format, live.Language, live.BasePrice, live.IsCancelled, live.EndsAt),
                movie, opinion));
        }

        var warnings = partial.Warnings;
        activity?.SetTag("aggregation.sessions", sessions.Count);
        activity?.SetTag("aggregation.warnings", warnings.Count);
        logger.LogInformation("Картку бронювання {BookingId} зібрано: {Sessions} сеанс(и), {Warnings} попереджень",
            booking.Id, sessions.Count, warnings.Count);

        return new BookingCardDto(
            new BookingSummary(booking.Id, booking.Status, booking.CreatedAt, booking.TotalAmount,
                booking.Tickets.Count(t => !t.IsCancelled), booking.ContactEmail, booking.Channel),
            sessions, warnings);
    }

    internal static MovieInfo? ToMovieInfo(CatalogMovieDto? m) => m is null ? null : new MovieInfo(
        m.Id, m.Title, m.OriginalTitle, m.ReleaseYear, m.DurationMinutes, m.AgeRating,
        m.Details?.Director, m.Details?.Description,
        (m.Genres ?? []).OrderByDescending(g => g.IsPrimary).Select(g => g.Name).ToList());

    /// <summary>null — reviews-api не відповів; порожній список — відгуків ще немає.</summary>
    internal static MovieOpinion? ToOpinion(MovieRatingDto? rating, PageDto<ReviewDto>? top, bool reviewsAvailable)
    {
        if (rating is null && top is null && !reviewsAvailable) return null;
        return new MovieOpinion(rating?.Average, rating?.Count ?? 0,
            (top?.Items ?? []).Select(r => new ReviewSnippet(r.Author.DisplayName, r.Rating, r.Text, r.VerifiedViewer, r.CreatedAt)).ToList());
    }
}

/// <summary>Сторінка фільму: чотири незалежні виклики двох сервісів одночасно (Task.WhenAll).</summary>
public sealed class MovieCardAggregator(ServiceSources sources, ILogger<MovieCardAggregator> logger)
{
    private readonly ICatalogSource catalog = sources.Catalog;
    private readonly IReviewsSource reviews = sources.Reviews;

    public async Task<MovieCardDto?> GetAsync(int movieId, CancellationToken ct)
    {
        using var activity = AggregatorTelemetry.Source.StartActivity("AggregateMovieCard");
        activity?.SetTag("movie.id", movieId);
        activity?.SetTag("aggregation.transport", sources.Transport);
        var partial = new PartialResults(logger, ct);

        var movieTask = partial.SafeAsync(catalog.ServiceName, $"фільм {movieId}", () => catalog.GetMovieAsync(movieId, ct));
        var screeningsTask = partial.SafeAsync(catalog.ServiceName, $"розклад фільму {movieId}", () => catalog.GetScreeningsOfMovieAsync(movieId, ct));
        var ratingTask = partial.SafeAsync(reviews.ServiceName, $"рейтинг фільму {movieId}", () => reviews.GetRatingAsync(movieId, ct));
        var topTask = partial.SafeAsync(reviews.ServiceName, $"відгуки на фільм {movieId}", () => reviews.GetTopReviewsAsync(movieId, 5, ct));
        await Task.WhenAll(movieTask, screeningsTask, ratingTask, topTask);

        var movie = await movieTask;
        var catalogFailed = partial.Warnings.Any(w => w.StartsWith(catalog.ServiceName, StringComparison.Ordinal));
        if (movie is null && !catalogFailed) return null;                 // Каталог відповів 404 — фільму немає

        var reviewsFailed = partial.Warnings.Any(w => w.StartsWith(reviews.ServiceName, StringComparison.Ordinal));
        var screenings = (await screeningsTask)?.Items
            .Where(s => s.MovieId == movieId && !s.IsCancelled)                 // узгодженість: лише сеанси цього фільму
            .Select(s => new UpcomingScreening(s.Id, s.StartsAt, s.HallName, s.Format, s.Language, s.BasePrice))
            .ToList() ?? [];

        return new MovieCardDto(movieId, BookingCardAggregator.ToMovieInfo(movie), screenings,
            BookingCardAggregator.ToOpinion(await ratingTask, await topTask, !reviewsFailed), partial.Warnings);
    }
}

/// <summary>
/// Лаб. 06: афіша з кількох фільмів. Каталог і Відгуки опитуються паралельно (Task.WhenAll); через gRPC кожен —
/// одним пакетним викликом (GetMoviesByIds, GetRatingsForMovies), через REST — N запитів на кожен сервіс.
/// </summary>
public sealed class PosterBoardAggregator(ServiceSources sources, ILogger<PosterBoardAggregator> logger)
{
    public const int MaxMovies = 20;

    public async Task<PosterBoardDto> GetAsync(IReadOnlyCollection<int> movieIds, CancellationToken ct)
    {
        using var activity = AggregatorTelemetry.Source.StartActivity("AggregatePosterBoard");
        activity?.SetTag("movies.count", movieIds.Count);
        activity?.SetTag("aggregation.transport", sources.Transport);
        var partial = new PartialResults(logger, ct);

        var moviesTask = partial.SafeAsync(sources.Catalog.ServiceName, $"фільми {string.Join(",", movieIds)}",
            async () => (IReadOnlyList<CatalogMovieDto>?)await sources.Catalog.GetMoviesAsync(movieIds, ct));
        var ratingsTask = partial.SafeAsync(sources.Reviews.ServiceName, $"рейтинги фільмів {string.Join(",", movieIds)}",
            async () => (IReadOnlyList<MovieRatingDto>?)await sources.Reviews.GetRatingsAsync(movieIds, ct));
        await Task.WhenAll(moviesTask, ratingsTask);

        var ratings = (await ratingsTask ?? []).ToDictionary(r => r.MovieId);
        var tiles = (await moviesTask ?? [])
            .OrderBy(m => m.Title)
            .Select(m => new MovieTile(m.Id, m.Title, m.ReleaseYear, m.DurationMinutes, m.AgeRating,
                (m.Genres ?? []).OrderByDescending(g => g.IsPrimary).Select(g => g.Name).ToList(),
                ratings.TryGetValue(m.Id, out var r) ? r.Average : null,
                ratings.TryGetValue(m.Id, out var c) ? c.Count : 0))
            .ToList();
        return new PosterBoardDto(tiles, partial.Warnings);
    }
}
