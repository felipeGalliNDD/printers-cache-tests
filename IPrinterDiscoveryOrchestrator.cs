using NDDTech.Devices.PrintControl.PrintersCache.Models;

namespace PrintersCacheTests;

public interface IPrinterDiscoveryOrchestrator
{
    Task<PrinterData?> DiscoverAndStoreAsync(string ipAddress, CancellationToken cancellationToken);
    Task<PrinterData?> GetByIpAddressAsync(string ipAddress, CancellationToken cancellationToken);
}