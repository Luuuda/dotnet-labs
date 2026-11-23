using CatalogService.Api.Grpc;
using CatalogService.Bll.Dtos;
using CatalogService.Bll.Services;
using CatalogService.Domain.Exceptions;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CatalogService.Tests.Unit;

/// <summary>Лаб. 06: gRPC-адаптер над BLL — коректні статуси RpcException і перетворення DTO → повідомлення.</summary>
public sealed class GrpcServiceTests
{
    private readonly Mock<IMovieService> _movies = new();
    private readonly Mock<IScreeningService> _screenings = new();
    private CatalogGrpcService Service => new(_movies.Object, _screenings.Object, NullLogger<CatalogGrpcService>.Instance);

    [Fact]
    public async Task Missing_movie_returns_NotFound_not_Internal()
    {
        _movies.Setup(m => m.GetByIdAsync(404, It.IsAny<CancellationToken>())).ThrowsAsync(new EntityNotFoundException("Фільм 404 не знайдено"));

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            Service.GetMovie(new CatalogService.Grpc.GetMovieRequest { Id = 404 }, new TestServerCallContext()));

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Non_positive_id_returns_InvalidArgument_without_calling_BLL()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            Service.GetMovie(new CatalogService.Grpc.GetMovieRequest { Id = 0 }, new TestServerCallContext()));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        _movies.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Screening_is_mapped_with_exact_money_and_time()
    {
        _screenings.Setup(s => s.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new ScreeningDto
        {
            Id = 1, MovieId = 3, MovieTitle = "Дюна: Частина друга", HallName = "IMAX", Format = "IMAX",
            StartsAt = new DateTime(2026, 12, 5, 16, 0, 0), EndsAt = new DateTime(2026, 12, 5, 19, 1, 0), BasePrice = 249.99m
        });

        var reply = await Service.GetScreening(new CatalogService.Grpc.GetScreeningRequest { Id = 1 }, new TestServerCallContext());

        Assert.Equal(249, reply.BasePrice.Units);
        Assert.Equal(990_000_000, reply.BasePrice.Nanos);
        Assert.Equal(new DateTime(2026, 12, 5, 16, 0, 0), reply.StartsAt.ToDateTime());
    }

    /// <summary>
    /// «Змініть номер поля лише на сервері»: клієнт зі старим номером не бачить значення, хоча ім'я поля збігається —
    /// у бінарному форматі protobuf передаються номери, а не імена.
    /// </summary>
    [Fact]
    public void Changing_field_number_on_one_side_breaks_reading()
    {
        var server = new CatalogService.Grpc.MovieReply { Id = 3, Title = "Дюна: Частина друга", ReleaseYear = 2024 };
        var bytes = server.ToByteArray();

        var sameContract = CatalogService.Grpc.MovieReply.Parser.ParseFrom(bytes);
        var renumbered = CatalogService.Tests.ProtoRenumbered.MovieReply.Parser.ParseFrom(bytes);

        Assert.Equal("Дюна: Частина друга", sameContract.Title);
        Assert.Equal("", renumbered.Title);                 // назву втрачено
        Assert.Equal(2024, renumbered.ReleaseYear);         // поля з незмінними номерами читаються як і раніше
    }

    private sealed class TestServerCallContext : ServerCallContext
    {
        protected override string MethodCore => "/cinema.catalog.v1.CatalogGrpc/Test";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "ipv4:127.0.0.1:5000";
        protected override DateTime DeadlineCore => DateTime.UtcNow.AddMinutes(1);
        protected override Metadata RequestHeadersCore { get; } = [];
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore { get; } = [];
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new(null, new Dictionary<string, List<AuthProperty>>());
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotSupportedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
