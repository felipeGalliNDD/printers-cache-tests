using System.Globalization;
using System.Net;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using NDDTech.Devices.PrintControl.PrintersCache.Features;
using NDDTech.Devices.PrintControl.PrintersCache.Models;
using PrintersCacheTests;

public static class NetworkPrinterScanner
{
    private static readonly string[] OidName = { "1.3.6.1.2.1.1.5.0" };
    private static readonly string[] OidSerial = { "1.3.6.1.2.1.43.5.1.1.17.1" };
    private static readonly string[] OidModel = { "1.3.6.1.2.1.43.5.1.1.16.1" };
    private static readonly string[] OidManufacturer = { ".1.3.6.1.2.1.1.1.0" };

    public static async Task ScanAndSaveAsync(IPrinterDistributedCache cache, CancellationToken cancellationToken)
    {
        const string subnet = "172.25.16";
        const int start = 1;
        const int end = 100;
        const int timeout = 2000;
        const string community = "public";
        const int maxParallel = 20;

        Console.WriteLine($"Scanning {subnet}.{start}-{end}...");
        Console.WriteLine($"Timeout: {timeout}ms, Community: {community}");

        var semaphore = new SemaphoreSlim(maxParallel);
        var tasks = new List<Task>();
        var found = 0;
        var saved = 0;

        for (int i = start; i <= end; i++)
        {
            var ip = $"{subnet}.{i}";
            await semaphore.WaitAsync(cancellationToken);
            var task = Task.Run(async () =>
            {
                try
                {
                    var result = await QueryPrinterAsync(ip, community, timeout, cancellationToken);
                    if (result is not null)
                    {
                        Interlocked.Increment(ref found);
                        Console.WriteLine($"Found: {ip} - {result.Model} by {result.Manufacturer}");

                        var printerData = new PrinterData
                        {
                            Name = string.IsNullOrWhiteSpace(result.Name) ? $"{result.Model}" : result.Name,
                            Serial = result.Serial ?? string.Empty,
                            IpAddress = ip,
                            Manufacturer = result.Manufacturer ?? string.Empty,
                            Model = result.Model ?? string.Empty
                        };

                        await cache.WritePrinterData(printerData, cancellationToken);
                        Interlocked.Increment(ref saved);
                        Console.WriteLine($"Saved: {ip}");
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine($"Scan cancelled: {ip}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error scanning {ip}: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken);
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);

        Console.WriteLine($"Scan complete. Found: {found}, Saved: {saved}");
    }

    public static async Task ScanAndSaveWithOrchestratorAsync(IPrinterDiscoveryOrchestrator orchestrator, CancellationToken cancellationToken)
    {
        const string subnet = "172.25.16";
        const int start = 1;
        const int end = 100;
        const int maxParallel = 20;

        Console.WriteLine($"Scanning {subnet}.{start}-{end} with orchestrator...");

        var semaphore = new SemaphoreSlim(maxParallel);
        var tasks = new List<Task>();
        var found = 0;
        var saved = 0;

        for (int i = start; i <= end; i++)
        {
            var ip = $"{subnet}.{i}";
            await semaphore.WaitAsync(cancellationToken);
            var task = Task.Run(async () =>
            {
                try
                {
                    var printerData = await orchestrator.DiscoverAndStoreAsync(ip, cancellationToken);
                    if (printerData is not null)
                    {
                        Interlocked.Increment(ref found);
                        Console.WriteLine($"Found: {ip} - {printerData.Model} by {printerData.Manufacturer}");
                        Interlocked.Increment(ref saved);
                        Console.WriteLine($"Saved: {ip}");
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine($"Scan cancelled: {ip}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error scanning {ip}: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken);
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);

        Console.WriteLine($"Scan complete. Found: {found}, Saved: {saved}");
    }

    private static async Task<PrinterInfo?> QueryPrinterAsync(string ip, string community, int timeoutMs, CancellationToken cancellationToken)
    {
        try
        {
            var endpoint = new IPEndPoint(IPAddress.Parse(ip), 161);
            var name = await GetStringAsync(endpoint, community, OidName, timeoutMs, cancellationToken);
            var serial = await GetStringAsync(endpoint, community, OidSerial, timeoutMs, cancellationToken);
            var model = await GetStringAsync(endpoint, community, OidModel, timeoutMs, cancellationToken);
            var manufacturer = await GetStringAsync(endpoint, community, OidManufacturer, timeoutMs, cancellationToken);

            if (!string.IsNullOrWhiteSpace(model) && !string.IsNullOrWhiteSpace(manufacturer))
            {
                string manufacturerString = GetManufacturerFromString(manufacturer);

                return new PrinterInfo
                {
                    Name = name,
                    Serial = serial,
                    Model = GetModelFromString(model, manufacturerString),
                    Manufacturer = manufacturerString
                };
            }
        }
        catch (OperationCanceledException ex)
        {
            Console.WriteLine($"Query cancelled for {ip}: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error querying {ip}: {ex.Message}");
        }

        return null;
    }

    private static string GetModelFromString(string rawModel, string manufact)
    {
        if (string.IsNullOrWhiteSpace(rawModel))
        {
            return string.Empty;
        }

        if (rawModel.Length <= 2 && rawModel.All(char.IsDigit))
        {
            return string.Empty;
        }

        var manufactLower = manufact.ToLower(CultureInfo.InvariantCulture);
        var beginPosition = FindModelFirstPosition(rawModel, manufactLower);

        var posComma = rawModel.IndexOf(',');
        var posDotComma = rawModel.IndexOf(';');

        if (posComma > -1 && posDotComma > -1)
        {
            var lastValidPos = posComma < posDotComma ? posComma : posDotComma;

            rawModel = rawModel[beginPosition..lastValidPos];

            return rawModel.Trim();
        }

        return HandleModelRemainingCases(rawModel, beginPosition);
    }

    private static string HandleModelRemainingCases(string rawModel, int beginPosition)
    {
        for (var j = beginPosition; j < rawModel.Length; j++)
        {
            if (char.IsDigit(rawModel[j]))
            {
                var finalPosValida = rawModel.IndexOf(' ', j);

                rawModel = finalPosValida switch
                {
                    -1 => rawModel[beginPosition..],
                    _ => rawModel[beginPosition..finalPosValida]
                };

                return rawModel.Replace(";", string.Empty);
            }
        }

        return rawModel[beginPosition..];
    }

    private static int FindModelFirstPosition(string rawModel, string manufactLower)
    {
        int beginPosition = rawModel.ToLower(CultureInfo.InvariantCulture).IndexOf(manufactLower, StringComparison.InvariantCulture);

        if (beginPosition > -1)
        {
            beginPosition += manufactLower.Length;
            if (rawModel[beginPosition] == ' ')
            {
                beginPosition++;
            }
        }
        else
        {
            beginPosition = 0;
        }

        return beginPosition;
    }

    private static string GetManufacturerFromString(string rawManufact)
    {
        

        if (string.IsNullOrWhiteSpace(rawManufact))
        {
            return string.Empty;
        }

        if (rawManufact.Length <= 2 && rawManufact.All(char.IsDigit))
        {
            return string.Empty;
        }

        var positionBreak = rawManufact.IndexOfAny([' ', '-']);

        if (positionBreak == -1)
        {
            return "";
        }

        var retManufacturer = rawManufact[..positionBreak];
        var rawManufactLower = rawManufact.ToLower(CultureInfo.InvariantCulture);

        return retManufacturer.ToLower(CultureInfo.InvariantCulture) switch
        {
            "network" when rawManufactLower.Contains("ricoh") => "Ricoh",
            "sp" or "gelsprinter" or "type" or "mfp" => "Ricoh",
            "131;crw-00552;" or "impressora" => "Xerox",
            "nucleus" => "Lexmark",
            string s when s.Contains("ip") || s.Contains("bw") => "Konica",
            "hp" => "HP",
            "c" when rawManufactLower.Contains("mf335") => "Develop",
            "color" when rawManufactLower.Contains("mf30-1") => "Develop",
            "generic" when rawManufactLower.Contains("30c-9") => "Develop",
            string s when s.Contains("oce") => retManufacturer.Replace(",", ""),
            string s when s.Contains("pantum") => "Pantum",
            string s when s.Contains("fuji") => "Xerox",
            string s when s.Contains("hewlett-packard") => "HP",
            string s when s.Contains("epson") => "Epson",
            _ => retManufacturer
        };
    }

   
  private static string GetHpManufacturerFromPattern(string rawManufact, string pattern)
 {
     var startIndex = rawManufact.ToLower(CultureInfo.InvariantCulture).Trim().IndexOf(pattern, StringComparison.InvariantCulture) + pattern.Length;
     var finalIndex = rawManufact.IndexOf(';', startIndex);

     if (startIndex < 0 || finalIndex < 0)
         throw new ArgumentException($"Pattern {pattern} not found or incorrectly formatted in {rawManufact}.");

     var manufacturer = rawManufact[startIndex..finalIndex].TrimEnd().ToLower(CultureInfo.InvariantCulture);
     Console.WriteLine($"HP Raw Manufact: {rawManufact} - Indexed Manufact: {manufacturer}");

     if (manufacturer == "hewlett-packard" || manufacturer == "hewlett packard")
     {
         return "HP";
     }

     return manufacturer.ToUpper(CultureInfo.InvariantCulture);
 }

    private static async Task<string?> GetStringAsync(IPEndPoint endpoint, string community, string[] oid, int timeoutMs, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(timeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            var result = await Messenger.GetAsync(VersionCode.V2, endpoint, new OctetString(community), new List<Variable> { new Variable(new ObjectIdentifier(oid[0])) }, linked.Token);
            return result.Count > 0 ? result[0].Data.ToString() : null;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private record PrinterInfo
    {
        public string? Name { get; init; }
        public string? Serial { get; init; }
        public string? Model { get; init; }
        public string? Manufacturer { get; init; }
    }
}