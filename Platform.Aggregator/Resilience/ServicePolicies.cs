using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Timeout;

namespace Platform.Aggregator.Resilience;

/// <summary>
/// Лаб. 07: параметри resilience для кожного сервісу, від якого залежить Aggregator. Обґрунтування — README, розділ 8.2.
/// <para>
/// Критичність визначає політику:
/// <list type="bullet">
/// <item><b>catalog-api — критичний</b>: без фільму й сеансу картку не зібрати → більше повторів (4) і найчутливіший
/// circuit breaker (30 % збоїв уже розмикають ланцюг);</item>
/// <item><b>booking-api — обов'язковий</b> для картки бронювання (без нього 503) → 3 повтори, breaker 50 %;</item>
/// <item><b>reviews-api — некритичний</b>: без рейтингу Aggregator і так поверне часткову відповідь → один повтор,
/// короткі таймаути (швидко здатися) і толерантний breaker (70 %).</item>
/// </list>
/// </para>
/// Обмеження, які перевіряє валідація HttpStandardResilienceOptions під час старту:
/// SamplingDuration ≥ 2 × AttemptTimeout; TotalRequestTimeout ≥ AttemptTimeout. Ми тримаємо ще суворіше правило:
/// загальний таймаут більший за суму всіх спроб і затримок між ними, інакше повтори не встигли б виконатися.
/// </summary>
public sealed record ServicePolicy(
    string Service,
    int MaxRetryAttempts,
    TimeSpan RetryDelay,
    TimeSpan AttemptTimeout,
    TimeSpan TotalRequestTimeout,
    double FailureRatio,
    int MinimumThroughput,
    TimeSpan SamplingDuration,
    TimeSpan BreakDuration,
    int GrpcMaxAttempts,
    TimeSpan GrpcInitialBackoff,
    TimeSpan GrpcMaxBackoff)
{
    /// <summary>Критичний: більше повторів, чутливіший breaker. Найгірший випадок: 5 × 2 с + (0,2+0,4+0,8+1,6) с ≈ 13 с &lt; 15 с.</summary>
    public static readonly ServicePolicy Catalog = new(
        Service: "catalog-api",
        MaxRetryAttempts: 4, RetryDelay: TimeSpan.FromMilliseconds(200),
        AttemptTimeout: TimeSpan.FromSeconds(2), TotalRequestTimeout: TimeSpan.FromSeconds(15),
        FailureRatio: 0.3, MinimumThroughput: 4,
        SamplingDuration: TimeSpan.FromSeconds(20), BreakDuration: TimeSpan.FromSeconds(10),
        GrpcMaxAttempts: 4, GrpcInitialBackoff: TimeSpan.FromMilliseconds(200), GrpcMaxBackoff: TimeSpan.FromSeconds(1));

    /// <summary>Обов'язковий для картки бронювання. Найгірший випадок: 4 × 2 с + (0,2+0,4+0,8) с ≈ 9,4 с &lt; 10 с.</summary>
    public static readonly ServicePolicy Booking = new(
        Service: "booking-api",
        MaxRetryAttempts: 3, RetryDelay: TimeSpan.FromMilliseconds(200),
        AttemptTimeout: TimeSpan.FromSeconds(2), TotalRequestTimeout: TimeSpan.FromSeconds(10),
        FailureRatio: 0.5, MinimumThroughput: 6,
        SamplingDuration: TimeSpan.FromSeconds(20), BreakDuration: TimeSpan.FromSeconds(15),
        GrpcMaxAttempts: 3, GrpcInitialBackoff: TimeSpan.FromMilliseconds(200), GrpcMaxBackoff: TimeSpan.FromSeconds(1));

    /// <summary>Некритичний: швидко здатися. Найгірший випадок: 2 × 1 с + 0,1 с ≈ 2,1 с &lt; 3 с.</summary>
    public static readonly ServicePolicy Reviews = new(
        Service: "reviews-api",
        MaxRetryAttempts: 1, RetryDelay: TimeSpan.FromMilliseconds(100),
        AttemptTimeout: TimeSpan.FromSeconds(1), TotalRequestTimeout: TimeSpan.FromSeconds(3),
        FailureRatio: 0.7, MinimumThroughput: 8,
        SamplingDuration: TimeSpan.FromSeconds(10), BreakDuration: TimeSpan.FromSeconds(15),
        GrpcMaxAttempts: 2, GrpcInitialBackoff: TimeSpan.FromMilliseconds(100), GrpcMaxBackoff: TimeSpan.FromMilliseconds(500));

    /// <summary>
    /// Налаштування Standard Resilience Handler одного клієнта (HTTP або gRPC) + логування переходів circuit breaker.
    /// Викликається через <see cref="ResiliencePolicyExtensions.ApplyPolicy"/> після <c>RemoveAllResilienceHandlers()</c>.
    /// </summary>
    public void Apply(HttpStandardResilienceOptions o, ILogger logger, string transport)
    {
        // --- Retry: експоненційна затримка з jitter, лише ідемпотентні методи ---
        o.Retry.MaxRetryAttempts = MaxRetryAttempts;
        o.Retry.Delay = RetryDelay;
        o.Retry.BackoffType = DelayBackoffType.Exponential;
        o.Retry.UseJitter = true;                       // клієнти не повторюють запити синхронно (thundering herd)
        if (transport == "grpc")
        {
            // gRPC: кожен тип збою повторює рівно ОДИН рівень, тож повтори не множаться (2 × 2):
            //  • статуси Unavailable / ResourceExhausted (у трейлерах з HTTP 200, а також «з'єднання відхилено») —
            //    RetryPolicy каналу (GrpcServiceConfig нижче); HTTP-обробник їх не бачить;
            //  • таймаут спроби (TimeoutRejectedException від Polly) gRPC-клієнт отримує як Internal і сам не
            //    повторює — тому лише його повторює HTTP-рівень.
            // Усі RPC, які викликає Aggregator, — читання (ідемпотентні), тож повтор безпечний.
            o.Retry.ShouldHandle = args => ValueTask.FromResult(args.Outcome.Exception is TimeoutRejectedException);
        }
        else
        {
            // POST/PUT/PATCH/DELETE/CONNECT не повторюються: повтор міг би, наприклад, створити бронювання двічі.
            // Aggregator через HTTP лише читає (GET), а це правило — запобіжник для майбутніх операцій запису.
            o.Retry.DisableForUnsafeHttpMethods();
        }
        o.Retry.OnRetry = args =>
        {
            logger.LogWarning("Resilience {Service} ({Transport}): повтор {Attempt} через {DelayMs} мс — {Reason}",
                Service, transport, args.AttemptNumber + 1, (long)args.RetryDelay.TotalMilliseconds, Describe(args.Outcome));
            return ValueTask.CompletedTask;
        };

        // --- Таймаути: спроба і загальний (разом з повторами) ---
        o.AttemptTimeout.Timeout = AttemptTimeout;
        o.TotalRequestTimeout.Timeout = TotalRequestTimeout;

        // --- Circuit breaker: на навчальному стенді MinimumThroughput зменшено (за замовчуванням 100) ---
        o.CircuitBreaker.FailureRatio = FailureRatio;
        o.CircuitBreaker.MinimumThroughput = MinimumThroughput;
        o.CircuitBreaker.SamplingDuration = SamplingDuration;     // ≥ 2 × AttemptTimeout (інакше помилка валідації)
        o.CircuitBreaker.BreakDuration = BreakDuration;
        o.CircuitBreaker.OnOpened = args =>
        {
            logger.LogError("Circuit breaker {Service} ({Transport}) OPENED на {BreakSeconds} с: виклики відхиляються без звернення до сервісу — {Reason}",
                Service, transport, args.BreakDuration.TotalSeconds, Describe(args.Outcome));
            return ValueTask.CompletedTask;
        };
        o.CircuitBreaker.OnHalfOpened = _ =>
        {
            logger.LogWarning("Circuit breaker {Service} ({Transport}) HALF-OPEN: пропускаю пробний запит", Service, transport);
            return ValueTask.CompletedTask;
        };
        o.CircuitBreaker.OnClosed = args =>
        {
            logger.LogInformation("Circuit breaker {Service} ({Transport}) CLOSED: сервіс відповідає ({Reason})",
                Service, transport, Describe(args.Outcome));
            return ValueTask.CompletedTask;
        };
    }

    /// <summary>
    /// Retry-політика gRPC-каналу за gRPC-статусами: Unavailable (сервіс зупинено / перезапускається) і
    /// ResourceExhausted (перевантаження). Повторюються лише ідемпотентні читання — інших RPC у контрактах немає.
    /// Загальний час обмежує дедлайн виклику (3 с, GrpcCall.Options).
    /// </summary>
    public ServiceConfig GrpcServiceConfig() => new()
    {
        MethodConfigs =
        {
            new MethodConfig
            {
                Names = { MethodName.Default },
                RetryPolicy = new RetryPolicy
                {
                    MaxAttempts = GrpcMaxAttempts,
                    InitialBackoff = GrpcInitialBackoff,
                    MaxBackoff = GrpcMaxBackoff,
                    BackoffMultiplier = 2,
                    RetryableStatusCodes = { StatusCode.Unavailable, StatusCode.ResourceExhausted }
                }
            }
        }
    };

    private static string Describe(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is { } ex ? $"{ex.GetType().Name}: {ex.Message}"
        : outcome.Result is { } response ? $"HTTP {(int)response.StatusCode}"
        : "—";
}

/// <summary>Підключення <see cref="ServicePolicy"/> до Standard Resilience Handler конкретного клієнта.</summary>
public static class ResiliencePolicyExtensions
{
    /// <summary>Категорія логів подій resilience (повтори, переходи circuit breaker) у Structured logs.</summary>
    public const string LogCategory = "Platform.Aggregator.Resilience";

    /// <summary>
    /// Налаштовує іменовані опції пайплайна саме цього клієнта (PipelineName = «&lt;клієнт&gt;-standard»), тож у метриках
    /// resilience.polly.* тег pipeline.name відрізняє Каталог, Бронювання й Відгуки, HTTP і gRPC.
    /// </summary>
    public static IHttpStandardResiliencePipelineBuilder ApplyPolicy(
        this IHttpStandardResiliencePipelineBuilder builder, ServicePolicy policy, string transport)
    {
        builder.Services.AddOptions<HttpStandardResilienceOptions>(builder.PipelineName)
            .Configure<ILoggerFactory>((options, loggers) =>
                policy.Apply(options, loggers.CreateLogger(LogCategory), transport));
        return builder;
    }
}
