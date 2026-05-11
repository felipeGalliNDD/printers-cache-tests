## Why

The current printer discovery flow needs a durable local source of truth while still keeping Redis as the fast cache. Adding SQLite now enables cache hydration and persistence by `IpAddress` without removing the existing Redis path.

## What Changes

- Add SQLite persistence alongside the existing Redis cache for printer records discovered by the network scanner.
- Use `IpAddress` as the lookup key in both SQLite and Redis.
- Hydrate Redis from SQLite when a printer exists in SQLite but is missing from the cache.
- Query SNMP only when the printer is missing from SQLite, then save the discovered data to both SQLite and Redis.
- Add configuration support for the SQLite database file path.

## Capabilities

### New Capabilities
- `sqlite-printer-storage`: Store and retrieve discovered printers through SQLite using `IpAddress` as the lookup key while coordinating with Redis cache hydration.

### Modified Capabilities
- `printer-cache`: Redis remains the cache layer, but reads and writes now coordinate with SQLite and SNMP fallback behavior.

## Impact

- Affected code: console app bootstrap (`Program.cs`), scanner lookup/save flow (`NetworkPrinterScanner.cs`), Redis cache implementation, and new SQLite storage classes.
- Dependencies: add a SQLite provider package for .NET data access.
- Configuration: introduce SQLite database path settings for runtime selection.
- Operations: keeps Redis in the flow and adds SQLite as the durable lookup source keyed by `IpAddress`.
