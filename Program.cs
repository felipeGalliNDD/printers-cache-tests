using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NDDTech.Devices.PrintControl.PrintersCache.Extensions;
using NDDTech.Devices.PrintControl.PrintersCache.Features;
using NDDTech.Devices.PrintControl.PrintersCache.Models;
using PrintersCacheTests;

var arguments = ParseArguments(args);
var redisConnection = arguments.GetValueOrDefault("redis") ?? "localhost:6379";
var sqlitePath = arguments.GetValueOrDefault("sqlite") ?? "printers.db";
var garnetAutoStart = ParseBoolean(arguments.GetValueOrDefault("garnet-autostart"), defaultValue: true);
var garnetWorkDir = ResolvePath(arguments.GetValueOrDefault("garnet-workdir") ?? AppContext.BaseDirectory);
var redisPassword = arguments.GetValueOrDefault("redis-password");

var redisEndpoint = ParseRedisEndpoint(redisConnection);
Process? garnetProcess = null;

if (garnetAutoStart && redisEndpoint.IsLocal && !IsPortOpen(redisEndpoint.Host, redisEndpoint.Port))
{
    if (!await GarnetHealthCheck.IsGarnetRunningAsync(redisConnection, CancellationToken.None))
    {
        var garnetExe = await EnsureGarnetToolInstalledAsync(CancellationToken.None);

        if (!File.Exists(garnetExe))
        {
            throw new FileNotFoundException($"Garnet tool executable not found after install: {garnetExe}");
        }

        garnetProcess = StartGarnet(garnetExe, garnetWorkDir, redisEndpoint.Port);
        await WaitForGarnetAsync(garnetProcess, redisConnection, TimeSpan.FromSeconds(30), CancellationToken.None);
    }
}

var garnetConnection = string.IsNullOrWhiteSpace(redisPassword)
    ? redisConnection
    : $"{redisConnection},password={redisPassword}";

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Garnet"] = garnetConnection,
        ["Sqlite:DatabasePath"] = sqlitePath
    })
    .Build();

var services = new ServiceCollection();
services.AddDistributedGarnetCache(configuration);

services.AddSingleton<ISqlitePrinterStore>(_ => new SqlitePrinterStore(sqlitePath));
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
    Console.WriteLine($"Connection: {redisConnection}");
    Console.WriteLine($"SQLite: {sqlitePath}");
    Console.WriteLine($"Garnet auto-start: {garnetAutoStart}");
    Console.WriteLine($"Error: {ex.Message}");
}

static Dictionary<string, string?> ParseArguments(string[] args)
{
    var parsed = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    for (var index = 0; index < args.Length; index++)
    {
        var arg = args[index];
        if (!arg.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        var key = arg[2..];
        string? value = null;

        if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = args[++index];
        }

        parsed[key] = value ?? "true";
    }

    return parsed;
}

static bool ParseBoolean(string? value, bool defaultValue)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return defaultValue;
    }

    return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
}

static string ResolvePath(string path)
{
    return Path.IsPathRooted(path)
        ? path
        : Path.GetFullPath(path, AppContext.BaseDirectory);
}

static async Task<string> EnsureGarnetToolInstalledAsync(CancellationToken cancellationToken)
{
    if (await IsGarnetToolInstalledAsync(cancellationToken))
    {
        var existingExe = ResolveGarnetToolExe();
        if (File.Exists(existingExe))
        {
            return existingExe;
        }
    }

    var install = new ProcessStartInfo
    {
        FileName = "dotnet",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };

    install.ArgumentList.Add("tool");
    install.ArgumentList.Add("install");
    install.ArgumentList.Add("-g");
    install.ArgumentList.Add("garnet-server");

    var output = await RunProcessAsync(install, cancellationToken);
    var toolExe = ResolveGarnetToolExe();

    if (!File.Exists(toolExe))
    {
        throw new InvalidOperationException($"Garnet install finished but executable not found: {toolExe}{Environment.NewLine}{output}");
    }

    return toolExe;
}

static async Task<bool> IsGarnetToolInstalledAsync(CancellationToken cancellationToken)
{
    var list = new ProcessStartInfo
    {
        FileName = "dotnet",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };

    list.ArgumentList.Add("tool");
    list.ArgumentList.Add("list");
    list.ArgumentList.Add("-g");

    var output = await RunProcessAsync(list, cancellationToken);
    return output.Contains("garnet-server", StringComparison.OrdinalIgnoreCase);
}

static string ResolveGarnetToolExe()
{
    if (OperatingSystem.IsWindows())
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", "garnet-server.exe");
    }

    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", "garnet-server");
}

static (string Host, int Port, bool IsLocal) ParseRedisEndpoint(string connection)
{
    var parts = connection.Split(':', 2, StringSplitOptions.TrimEntries);
    var host = parts[0];
    var port = parts.Length > 1 && int.TryParse(parts[1], out var parsedPort) ? parsedPort : 6379;
    var machineName = Environment.MachineName;
    var isLocal = string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, machineName, StringComparison.OrdinalIgnoreCase);

    return (host, port, isLocal);
}

static bool IsPortOpen(string host, int port)
{
    try
    {
        using var client = new TcpClient();
        var connectTask = client.ConnectAsync(host, port);
        return connectTask.Wait(TimeSpan.FromMilliseconds(500)) && client.Connected;
    }
    catch
    {
        return false;
    }
}

static Process StartGarnet(string garnetExe, string garnetWorkDir, int port)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = garnetExe,
        WorkingDirectory = garnetWorkDir,
        UseShellExecute = true,
        CreateNoWindow = true
    };

    startInfo.ArgumentList.Add("--port");
    startInfo.ArgumentList.Add(port.ToString());

    return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start Garnet process.");
}

static async Task<string> RunProcessAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
{
    using var process = new Process { StartInfo = startInfo };
    var output = new StringBuilder();

    process.OutputDataReceived += (_, e) =>
    {
        if (!string.IsNullOrWhiteSpace(e.Data))
        {
            output.AppendLine(e.Data);
        }
    };

    process.ErrorDataReceived += (_, e) =>
    {
        if (!string.IsNullOrWhiteSpace(e.Data))
        {
            output.AppendLine(e.Data);
        }
    };

    if (!process.Start())
    {
        throw new InvalidOperationException($"Failed to start process: {startInfo.FileName}");
    }

    process.BeginOutputReadLine();
    process.BeginErrorReadLine();

    await process.WaitForExitAsync(cancellationToken);

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"Process failed: {startInfo.FileName}{Environment.NewLine}{output}");
    }

    return output.ToString();
}

static async Task WaitForGarnetAsync(Process process, string connectionString, TimeSpan timeout, CancellationToken cancellationToken)
{
    var deadline = DateTimeOffset.UtcNow + timeout;

    while (DateTimeOffset.UtcNow < deadline)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (await GarnetHealthCheck.IsGarnetRunningAsync(connectionString, cancellationToken))
        {
            return;
        }

        await Task.Delay(250, cancellationToken);
    }

    throw new TimeoutException($"Garnet did not start within {timeout.TotalSeconds} seconds.");
}
