using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NDDTech.Devices.PrintControl.PrintersCache.Extensions;
using NDDTech.Devices.PrintControl.PrintersCache.Features;
using NDDTech.Devices.PrintControl.PrintersCache.Models;
using PrintersCacheTests;

var services = new ServiceCollection();

var baseConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
    ?? "localhost:6379";
var redisPassword = Environment.GetEnvironmentVariable("REDIS_PASSWORD");
var garnetConnection = string.IsNullOrWhiteSpace(redisPassword)
    ? baseConnection
    : $"{baseConnection},password={redisPassword}";

var sqlitePath = Environment.GetEnvironmentVariable("SQLITE_DATABASE_PATH")
    ?? "printers.db";

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Garnet"] = garnetConnection,
        ["Sqlite:DatabasePath"] = sqlitePath
    })
    .Build();

services.AddDistributedGarnetCache(configuration);

services.AddSingleton<ISqlitePrinterStore>(sp =>
{
    var dbPath = configuration["Sqlite:DatabasePath"] ?? "printers.db";
    return new SqlitePrinterStore(dbPath);
});

services.AddSingleton<IPrinterDiscoveryOrchestrator, PrinterDiscoveryOrchestrator>();

using var sp = services.BuildServiceProvider();

var cache = sp.GetRequiredService<IPrinterDistributedCache>();
var orchestrator = sp.GetRequiredService<IPrinterDiscoveryOrchestrator>();
var sqliteStore = sp.GetRequiredService<ISqlitePrinterStore>();

await sqliteStore.InitializeAsync(CancellationToken.None);

Console.WriteLine("Choose option:");
Console.WriteLine("1 - Create printer");
Console.WriteLine("2 - Read printer");
Console.WriteLine("3 - Scan and save network printers");
Console.Write("Option: ");

var option = Console.ReadLine();

try
{
    if (option == "1")
    {
        Console.Write("Printer name: ");
        var name = Console.ReadLine();

        Console.Write("Serial number: ");
        var serial = Console.ReadLine();

        Console.Write("IpAddress: ");
        var ipAddress = Console.ReadLine();

        var printerData = new PrinterData
        {
            Name = name ?? string.Empty,
            Serial = serial ?? string.Empty,
            IpAddress = ipAddress ?? string.Empty
        };

        await cache.WritePrinterData(printerData, CancellationToken.None);
        await sqliteStore.UpsertByIpAddressAsync(printerData, CancellationToken.None);

        Console.WriteLine("Printer data written successfully.");
    }
    else if (option == "2")
    {
        Console.Write("IpAddress: ");
        var ipAddress = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            Console.WriteLine("IpAddress is required.");
            return;
        }

        var printerData = await orchestrator.GetByIpAddressAsync(ipAddress, CancellationToken.None);

        if (printerData is null)
        {
            Console.WriteLine("Printer not found.");
            return;
        }

        Console.WriteLine("Printer found:");
        Console.WriteLine($"Name: {printerData.Name}");
        Console.WriteLine($"Serial: {printerData.Serial}");
        Console.WriteLine($"IpAddress: {printerData.IpAddress}");
        Console.WriteLine($"Manufacturer: {printerData.Manufacturer}");
        Console.WriteLine($"Model: {printerData.Model}");
    }
    else if (option == "3")
    {
        Console.WriteLine("Scanning network printers...");
        await NetworkPrinterScanner.ScanAndSaveWithOrchestratorAsync(orchestrator, CancellationToken.None);
    }
    else
    {
        Console.WriteLine("Invalid option.");
    }
}
catch (Exception ex)
{
    Console.WriteLine("Printer cache operation failed.");
    Console.WriteLine($"Connection: {baseConnection}");
    Console.WriteLine($"SQLite: {sqlitePath}");
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine("If Redis/Garnet requires auth, set REDIS_PASSWORD env var and run again.");
}