using AutoMapper;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using Reviews.Application;
using Reviews.Application.Common.Behaviors;
using Reviews.Application.Common.Exceptions;
using Reviews.Application.Common.Mapping;
using Reviews.Application.Common.Models;
using Reviews.Application.Reviews.Commands;
using Reviews.Application.Reviews.Queries;
using Reviews.Domain.Entities;
using Reviews.Domain.Exceptions;
using Reviews.Domain.Interfaces;
using Reviews.Domain.ValueObjects;
using Reviews.WebApi.Contracts;
using ValidationException = Reviews.Application.Common.Exceptions.ValidationException;

namespace Reviews.Tests.Unit;

public sealed class ApplicationTests
{
    internal static IMapper CreateMapper() =>
        new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<MappingProfile>();
            cfg.AddProfile<RequestMappingProfile>();
        }, NullLoggerFactory.Instance).CreateMapper();

    private static CreateReviewCommand ValidCreate(int rating = 5) =>
        new(3, "Дюна: Частина друга", "u-77", "Олена", rating, "Чудовий фільм");

    [Fact]
    public void AutoMapper_configuration_is_valid() =>
        new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<MappingProfile>();
            cfg.AddProfile<RequestMappingProfile>();
        }, NullLoggerFactory.Instance).AssertConfigurationIsValid();

    [Fact]
    public void Review_maps_objectid_value_objects_and_nested_comments()
    {
        var review = new Review(3, "Дюна", new Author("u-1", "Олена"), new Rating(5), "Текст",
            new ReviewAspects(new Dictionary<string, int> { ["story"] = 5 }));
        review.AddComment(new Author("u-2", "Андрій"), "Згоден");

        var dto = CreateMapper().Map<ReviewDetailsDto>(review);

        Assert.Equal(review.Id.ToString(), dto.Id);
        Assert.Equal(24, dto.Id.Length);
        Assert.Equal(5, dto.Rating);
        Assert.Equal("Олена", dto.Author.DisplayName);
        Assert.Equal(5, dto.Aspects!["story"]);
        Assert.Equal("Андрій", Assert.Single(dto.Comments).Author.DisplayName);
        Assert.Equal(1, dto.CommentsCount);
    }

    [Fact]
    public async Task Create_validator_rejects_rating_10_with_error_per_field()
    {
        var validator = new CreateReviewCommandValidator(Mock.Of<IReviewRepository>());
        var result = await validator.ValidateAsync(ValidCreate(rating: 10) with { MovieId = 0, Text = "" });

        var fields = result.Errors.Select(e => e.PropertyName).ToHashSet();
        Assert.Contains("Rating", fields);
        Assert.Contains("MovieId", fields);
        Assert.Contains("Text", fields);
    }

    [Fact]
    public async Task Create_validator_has_async_rule_against_duplicate_review()
    {
        var repository = new Mock<IReviewRepository>();
        repository.Setup(r => r.ExistsAsync(3, "u-77", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await new CreateReviewCommandValidator(repository.Object).ValidateAsync(ValidCreate());

        var error = Assert.Single(result.Errors);
        Assert.Equal("UserId", error.PropertyName);
        Assert.Contains("уже залишив відгук", error.ErrorMessage);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("")]
    public void Invalid_objectid_is_rejected(string id)
        => Assert.False(new GetReviewByIdQueryValidator().Validate(new GetReviewByIdQuery(id)).IsValid);

    [Fact]
    public void Unknown_sort_field_is_rejected()
        => Assert.False(new GetReviewsListQueryValidator().Validate(new GetReviewsListQuery(SortBy: "password")).IsValid);

    [Fact]
    public async Task ValidationBehavior_does_not_call_handler_when_request_is_invalid()
    {
        var behavior = new ValidationBehavior<CreateReviewCommand, ReviewDetailsDto>(
            [new CreateReviewCommandValidator(Mock.Of<IReviewRepository>())]);
        var handlerCalled = false;

        var ex = await Assert.ThrowsAsync<ValidationException>(() => behavior.Handle(ValidCreate(rating: 10),
            _ => { handlerCalled = true; return Task.FromResult(new ReviewDetailsDto()); }, CancellationToken.None));

        Assert.False(handlerCalled);
        Assert.True(ex.Errors.ContainsKey("Rating"));
    }

    [Fact]
    public async Task Pipeline_through_mediator_rejects_rating_10_before_handler_and_logs_it()
    {
        var repository = new Mock<IReviewRepository>();
        var logs = new ListLoggerProvider();
        var services = new ServiceCollection()
            .AddLogging(b => b.AddProvider(logs))
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton(repository.Object);
        services.AddApplication(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();

        await Assert.ThrowsAsync<ValidationException>(() =>
            provider.GetRequiredService<ISender>().Send(ValidCreate(rating: 10)));

        repository.Verify(r => r.AddAsync(It.IsAny<Review>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains(logs.Messages, m => m.Contains("MediatR → CreateReviewCommand"));
        Assert.Contains(logs.Messages, m => m.Contains("ДО виклику обробника"));
        Assert.DoesNotContain(logs.Messages, m => m.StartsWith("Обробник CreateReviewCommand виконано", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_with_stale_version_is_conflict()
    {
        var review = new Review(3, "Дюна", new Author("u-1", "Олена"), 5, "Текст");
        review.BumpVersion(); review.BumpVersion();                       // у базі вже версія 2
        var repository = new Mock<IReviewRepository>();
        repository.Setup(r => r.GetByIdAsync(review.Id, It.IsAny<CancellationToken>())).ReturnsAsync(review);

        var handler = new UpdateReviewCommandHandler(repository.Object, CreateMapper(), NullLogger<UpdateReviewCommandHandler>.Instance);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new UpdateReviewCommand(review.Id.ToString(), 4, "Новий текст", null, ExpectedVersion: 1), CancellationToken.None));
        repository.Verify(r => r.UpdateAsync(It.IsAny<Review>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_of_missing_review_is_not_found()
    {
        var handler = new UpdateReviewCommandHandler(Mock.Of<IReviewRepository>(), CreateMapper(), NullLogger<UpdateReviewCommandHandler>.Instance);
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new UpdateReviewCommand(ObjectId.GenerateNewId().ToString(), 4, "Текст", null, 0), CancellationToken.None));
    }

    [Fact]
    public async Task Create_handler_saves_through_repository_and_returns_string_id()
    {
        var repository = new Mock<IReviewRepository>();
        var handler = new CreateReviewCommandHandler(repository.Object, CreateMapper(), NullLogger<CreateReviewCommandHandler>.Instance);

        var dto = await handler.Handle(ValidCreate() with { Aspects = new Dictionary<string, int> { ["visuals"] = 5 } }, CancellationToken.None);

        Assert.True(ObjectId.TryParse(dto.Id, out _));
        Assert.Equal(5, dto.Aspects!["visuals"]);
        repository.Verify(r => r.AddAsync(It.Is<Review>(x => x.Id.ToString() == dto.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Request_is_mapped_to_command_with_route_id_and_if_match_version()
    {
        var command = CreateMapper().Map<UpdateReviewCommand>(new UpdateReviewRequest(4, "Текст", null, Version: 7), o =>
        {
            o.Items[RequestMappingProfile.IdKey] = "6650f0c2a1b2c3d4e5f60718";
            o.Items[RequestMappingProfile.VersionKey] = 3;
        });
        Assert.Equal("6650f0c2a1b2c3d4e5f60718", command.Id);
        Assert.Equal(3, command.ExpectedVersion);                     // If-Match має пріоритет над полем version
    }
}

/// <summary>Простий провайдер логів для перевірки, які записи зробили behaviors.</summary>
public sealed class ListLoggerProvider : ILoggerProvider
{
    public List<string> Messages { get; } = [];
    public ILogger CreateLogger(string categoryName) => new ListLogger(Messages);
    public void Dispose() { }

    private sealed class ListLogger(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (messages) messages.Add(formatter(state, exception));
        }
    }
}
