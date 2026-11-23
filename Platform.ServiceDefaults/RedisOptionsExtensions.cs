using System.Net;

using StackExchange.Redis;

namespace Microsoft.Extensions.Hosting;

public static class RedisOptionsExtensions
{
    /// <summary>
    /// Лаб. 06: Aspire передає рядок підключення «localhost:порт». На Windows клієнт спершу пробує IPv6 (::1), куди Docker
    /// порт не публікує, і кожне нове з'єднання з Redis чекає ~2 с. Підставляємо 127.0.0.1 замість localhost.
    /// </summary>
    public static ConfigurationOptions UseIPv4Loopback(this ConfigurationOptions options)
    {
        for (var i = 0; i < options.EndPoints.Count; i++)
            if (options.EndPoints[i] is DnsEndPoint { Host: "localhost" } dns)
                options.EndPoints[i] = new IPEndPoint(IPAddress.Loopback, dns.Port);
        return options;
    }
}
