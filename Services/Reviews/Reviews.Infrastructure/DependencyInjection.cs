using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reviews.Application.Common.Interfaces;
using Reviews.Domain.Interfaces;
using Reviews.Infrastructure.Persistence;
using Reviews.Infrastructure.Persistence.Queries;
using Reviews.Infrastructure.Persistence.Repositories;
using Reviews.Infrastructure.Seeding;

namespace Reviews.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        MongoMappings.Register();

        services.AddOptions<MongoDbOptions>()
            .Bind(config.GetSection(MongoDbOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<MongoDbContext>();

        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IDiscussionRepository, DiscussionRepository>();
        services.AddScoped<IReviewQueries, ReviewQueries>();
        services.AddScoped<IDiscussionQueries, DiscussionQueries>();
        services.AddScoped<IStoreReadiness, MongoStoreReadiness>();
        services.AddScoped<IIntegrationEventStore, IntegrationEventStore>();   // Лаб. 08: оброблені події (processedEvents)      // Лаб. 07: готовність індексів для health check

        services.AddScoped<IDataSeeder, ReviewSeeder>();
        services.AddScoped<IDataSeeder, DiscussionSeeder>();
        services.AddHostedService<IndexCreationService>();       // спершу індекси (унікальність до вставки даних)
        services.AddHostedService<DataSeedingService>();         // потім сідінг

        // Лаб. 07: health checks MongoDB (AddMongoDb: ping; індекси) реєструє Reviews.WebApi/Program.cs з тегом ready
        return services;
    }
}
