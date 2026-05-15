using NDDTech.Devices.PrintControl.PrintersCache.Models;

namespace PrintersCacheTests;

public interface ISqlitePrinterStore
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<PrinterData?> GetByIpAddressAsync(string ipAddress, CancellationToken cancellationToken);
    Task UpsertByIpAddressAsync(PrinterData printerData, CancellationToken cancellationToken);
}
