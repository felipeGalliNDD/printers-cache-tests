using StackExchange.Redis;

namespace PrintersCacheTests;

public static class GarnetHealthCheck
{
    public static async Task<bool> IsGarnetRunningAsync(
        string connectionString = "localhost:6379",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = 3000;
            options.SyncTimeout = 3000;

            await using var redis = await ConnectionMultiplexer.ConnectAsync(options);

            var db = redis.GetDatabase();

            var result = await db.ExecuteAsync("PING");

            return result.ToString()
                .Equals("PONG", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
