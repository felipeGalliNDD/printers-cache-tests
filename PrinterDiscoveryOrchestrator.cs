using System.Globalization;
using System.Net;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using NDDTech.Devices.PrintControl.PrintersCache.Features;
using NDDTech.Devices.PrintControl.PrintersCache.Models;

namespace PrintersCacheTests;

public class PrinterDiscoveryOrchestrator : IPrinterDiscoveryOrchestrator
{
    private readonly ISqlitePrinterStore _sqliteStore;
    private readonly IPrinterDistributedCache _cache;
    private const string Community = "public";
    private const int TimeoutMs = 2000;

    private static readonly string[] OidName = { "1.3.6.1.2.1.1.5.0" };
    private static readonly string[] OidSerial = { "1.3.6.1.2.1.43.5.1.1.17.1" };
    private static readonly string[] OidModel = { "1.3.6.1.2.1.43.5.1.1.16.1" };
    private static readonly string[] OidManufacturer = { ".1.3.6.1.2.1.1.1.0" };

    public PrinterDiscoveryOrchestrator(ISqlitePrinterStore sqliteStore, IPrinterDistributedCache cache)
    {
        _sqliteStore = sqliteStore;
        _cache = cache;
    }

    public async Task<PrinterData?> DiscoverAndStoreAsync(string ipAddress, CancellationToken cancellationToken)
    {
        var sqliteResult = await _sqliteStore.GetByIpAddressAsync(ipAddress, cancellationToken);

        if (sqliteResult is not null)
        {
            var redisResult = await _cache.ReadPrinterData(ipAddress, cancellationToken);
            if (redisResult is null)
            {
                await _cache.WritePrinterData(sqliteResult, cancellationToken);
            }
            return redisResult ?? sqliteResult;
        }

        var snmpResult = await QuerySnmpAsync(ipAddress, cancellationToken);
        if (snmpResult is not null)
        {
            var printerData = new PrinterData
            {
                Name = string.IsNullOrWhiteSpace(snmpResult.Name) ? snmpResult.Model ?? string.Empty : snmpResult.Name,
                Serial = snmpResult.Serial ?? string.Empty,
                IpAddress = ipAddress,
                Manufacturer = snmpResult.Manufacturer ?? string.Empty,
                Model = snmpResult.Model ?? string.Empty
            };

            await _sqliteStore.UpsertByIpAddressAsync(printerData, cancellationToken);
            await _cache.WritePrinterData(printerData, cancellationToken);

            return printerData;
        }

        return null;
    }

    public async Task<PrinterData?> GetByIpAddressAsync(string ipAddress, CancellationToken cancellationToken)
    {
        var sqliteResult = await _sqliteStore.GetByIpAddressAsync(ipAddress, cancellationToken);

        if (sqliteResult is not null)
        {
            var redisResult = await _cache.ReadPrinterData(ipAddress, cancellationToken);
            if (redisResult is null)
            {
                await _cache.WritePrinterData(sqliteResult, cancellationToken);
                return sqliteResult;
            }
            return redisResult;
        }

        return null;
    }

    private async Task<PrinterInfo?> QuerySnmpAsync(string ip, CancellationToken cancellationToken)
    {
        try
        {
            var endpoint = new IPEndPoint(IPAddress.Parse(ip), 161);

            var nameTask = GetStringAsync(endpoint, OidName, cancellationToken);
            var serialTask = GetStringAsync(endpoint, OidSerial, cancellationToken);
            var modelTask = GetStringAsync(endpoint, OidModel, cancellationToken);
            var manufacturerTask = GetStringAsync(endpoint, OidManufacturer, cancellationToken);

            await Task.WhenAll(nameTask, serialTask, modelTask, manufacturerTask);

            var name = nameTask.Result;
            var serial = serialTask.Result;
            var model = modelTask.Result;
            var manufacturer = manufacturerTask.Result;

            if (!string.IsNullOrWhiteSpace(model) && !string.IsNullOrWhiteSpace(manufacturer))
            {
                string manufacturerString = GetManufacturerFromString(manufacturer);
                string modelString = GetModelFromString(model, manufacturerString);

                return new PrinterInfo
                {
                    Name = name,
                    Serial = serial,
                    Model = modelString,
                    Manufacturer = manufacturerString
                };
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }

        return null;
    }

    private async Task<string?> GetStringAsync(IPEndPoint endpoint, string[] oid, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            var result = await Messenger.GetAsync(
                VersionCode.V2,
                endpoint,
                new OctetString(Community),
                new List<Variable> { new Variable(new ObjectIdentifier(oid[0])) },
                linked.Token);

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

    private static string GetModelFromString(string rawModel, string manufact)
    {
        if (string.IsNullOrWhiteSpace(rawModel))
            return string.Empty;

        if (rawModel.Length <= 2 && rawModel.All(char.IsDigit))
            return string.Empty;

        var manufactLower = manufact.ToLower(CultureInfo.InvariantCulture);
        int beginPosition = FindModelFirstPosition(rawModel, manufactLower);

        var posComma = rawModel.IndexOf(',');
        var posDotComma = rawModel.IndexOf(';');

        if (posComma > -1 && posDotComma > -1)
        {
            var lastValidPos = posComma < posDotComma ? posComma : posDotComma;
            return rawModel[beginPosition..lastValidPos].Trim();
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
                var result = finalPosValida switch
                {
                    -1 => rawModel[beginPosition..],
                    _ => rawModel[beginPosition..finalPosValida]
                };
                return result.Replace(";", string.Empty);
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
            return string.Empty;

        if (rawManufact.Length <= 2 && rawManufact.All(char.IsDigit))
            return string.Empty;

        var positionBreak = rawManufact.IndexOfAny([' ', '-']);

        if (positionBreak == -1)
            return string.Empty;

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

    private record PrinterInfo
    {
        public string? Name { get; init; }
        public string? Serial { get; init; }
        public string? Model { get; init; }
        public string? Manufacturer { get; init; }
    }
}