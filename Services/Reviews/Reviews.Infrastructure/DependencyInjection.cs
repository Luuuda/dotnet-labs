using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reviews.Application.Common.Interfaces;
using Reviews.Domain.Interfaces;
using Reviews.Infrastructure.Health;
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

        services.AddScoped<IDataSeeder, ReviewSeeder>();
        services.AddScoped<IDataSeeder, DiscussionSeeder>();
        services.AddHostedService<IndexCreationService>();       // спершу індекси (унікальність до вставки даних)
        services.AddHostedService<DataSeedingService>();         // потім сідінг

        services.AddHealthChecks().AddCheck<MongoHealthCheck>("mongodb", tags: ["db", "mongodb"]);
        return services;
    }
}
