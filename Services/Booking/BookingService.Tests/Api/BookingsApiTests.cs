using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookingService.Bll.Dtos;
using BookingService.Tests.Integration;

namespace BookingService.Tests.Api;

/// <summary>
/// API-тести через WebApplicationFactory: статуси, формат ProblemDetails, транзакційні сценарії —
/// той самий список, що в розділі «Перед здачею перевірте себе».
/// </summary>
public sealed class BookingsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const long ImaxScreening = 7;   // Дюна, IMAX 14×20 (seed Лаб. 01)
    private readonly HttpClient _client = factory.CreateClient();

    [DbFact]
    public async Task Unknown_id_returns_404_problem_details_not_500()
    {
        var response = await _client.GetAsync("/api/bookings/987654321");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [DbFact]
    public async Task Invalid_dto_returns_400_before_service_is_called()
    {
        var response = await _client.PostAsJsonAsync("/api/customers", new { email = "not-an-email", fullName = "X" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(problem.GetProperty("errors").EnumerateObject(),
                        e => e.Name.Equals("Email", StringComparison.OrdinalIgnoreCase));
    }

    [DbFact]
    public async Task Customer_named_OBrien_is_created_with_201_and_location()
    {
        var response = await _client.PostAsJsonAsync("/api/customers", new
        {
            email = $"o.brien.{Guid.NewGuid():N}@example.com",
            fullName = "Seán O'Brien"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var created = await response.Content.ReadFromJsonAsync<CustomerDto>();
        var search = await _client.GetFromJsonAsync<JsonElement>("/api/customers?search=O'Brien&pageSize=100");
        Assert.Contains(search.GetProperty("items").EnumerateArray(),
                        c => c.GetProperty("id").GetGuid() == created!.Id);

        await _client.DeleteAsync(response.Headers.Location);
    }

    [DbFact]
    public async Task Confirming_twice_returns_409_problem_details()
    {
        var bookingId = await CreateBookingAsync();

        var first = await _client.PostAsync($"/api/bookings/{bookingId}/confirm", null);
        var second = await _client.PostAsync($"/api/bookings/{bookingId}/confirm", null);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync($"/api/bookings/{bookingId}/cancel", null)).StatusCode);
    }

    [DbFact]
    public async Task Paying_unconfirmed_booking_returns_409_and_payment_is_rolled_back()
    {
        var bookingId = await CreateBookingAsync();
        var booking = await _client.GetFromJsonAsync<BookingDto>($"/api/bookings/{bookingId}");

        var pay = await _client.PostAsJsonAsync($"/api/bookings/{bookingId}/pay",
            new { amount = booking!.TotalAmount, provider = "Card" });

        Assert.Equal(HttpStatusCode.Conflict, pay.StatusCode);
        var payments = await _client.GetFromJsonAsync<List<PaymentDto>>($"/api/bookings/{bookingId}/payments");
        Assert.Empty(payments!);   // вставка оплати відкотилась разом із невдалою зміною статусу

        await _client.PostAsync($"/api/bookings/{bookingId}/cancel", null);
    }

    [DbFact]
    public async Task Full_flow_create_confirm_pay_idempotent_issue()
    {
        var bookingId = await CreateBookingAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync($"/api/bookings/{bookingId}/confirm", null)).StatusCode);

        var booking = await _client.GetFromJsonAsync<BookingDto>($"/api/bookings/{bookingId}");
        var key = $"TX-TEST-{Guid.NewGuid():N}";
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/bookings/{bookingId}/pay")
        {
            Content = JsonContent.Create(new { amount = booking!.TotalAmount, provider = "Card" })
        };
        request.Headers.Add("Idempotency-Key", key);
        var paid = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, paid.StatusCode);

        var replay = new HttpRequestMessage(HttpMethod.Post, $"/api/bookings/{bookingId}/pay")
        {
            Content = JsonContent.Create(new { amount = booking.TotalAmount, provider = "Card" })
        };
        replay.Headers.Add("Idempotency-Key", key);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(replay)).StatusCode);   // друга оплата не створена

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync($"/api/bookings/{bookingId}/issue", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsync($"/api/bookings/{bookingId}/cancel", null)).StatusCode);
        Assert.Single((await _client.GetFromJsonAsync<List<PaymentDto>>($"/api/bookings/{bookingId}/payments"))!);
    }

    [DbFact]
    public async Task Swagger_document_is_available()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Створює бронювання на перше вільне місце сеансу IMAX (щоб тести можна було повторювати).</summary>
    private async Task<long> CreateBookingAsync()
    {
        var map = await _client.GetFromJsonAsync<SeatMapDto>($"/api/screenings/{ImaxScreening}/seats");
        var occupied = map!.Occupied.Select(s => (s.Row, s.Seat)).ToHashSet();
        var free = Enumerable.Range(1, map.RowsCount)
            .SelectMany(r => Enumerable.Range(1, map.SeatsPerRow).Select(s => (Row: r, Seat: s)))
            .First(seat => !occupied.Contains(seat));

        var response = await _client.PostAsJsonAsync("/api/bookings", new
        {
            customerId = "6f1c2a10-0000-4000-8000-000000000004",
            screeningId = ImaxScreening,
            seats = new[] { new { row = free.Row, seat = free.Seat } },
            channel = "Web"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BookingDto>())!.Id;
    }
}
