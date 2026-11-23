using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Platform.ServiceDefaults.Grpc;

public static class GrpcExtensions
{
    /// <summary>
    /// AddGrpc() з серверним interceptor-ом логування. Детальні помилки (stack trace у відповіді) — лише в Development.
    /// </summary>
    public static TBuilder AddPlatformGrpc<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<GrpcServerLoggingInterceptor>();
            options.EnableDetailedErrors = builder.Environment.IsDevelopment();
        });
        return builder;
    }
}
