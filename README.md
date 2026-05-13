# Printers Cache Tests

Console application for network printer discovery with SQLite persistence and Redis/Garnet cache coordination.

## Features

- Network printer discovery via SNMP protocol
- SQLite local persistence for discovered printers
- Redis/Garnet cache coordination for fast access
- Orchestrator-based flow: SQLite → Redis → SNMP

## Runtime Flow

For each IP address:

```
1. Check SQLite by IpAddress
   ├─ Found → Check Redis
   │          ├─ Redis hit → use cache, skip SNMP
   │          └─ Redis miss → hydrate from SQLite, skip SNMP
   └─ Not found → SNMP query
                  ├─ Found → save to SQLite + Redis
                  └─ Not found → no save
```

## Configuration

| Environment Variable | Description | Default |
|---------------------|-------------|---------|
| `--redis` | Redis/Garnet connection | `localhost:6379` |
| `--redis-password` | Redis password (optional) | - |
| `--sqlite` | SQLite database file path | `printers.db` |
| `--garnet-autostart` | Start bundled Garnet if needed | `true` |
| `--garnet-exe` | Garnet executable path | `tools/garnet/win-x64/garnet-server.exe` |
| `--garnet-workdir` | Garnet working directory | app folder |

## Usage

```
1 - Create printer
2 - Read printer
3 - Scan and save network printers
```

### Examples

```bash
dotnet run
dotnet run -- --sqlite printers.db
dotnet run -- --redis localhost:6379 --garnet-autostart true
dotnet run -- --garnet-autostart false
```

### Bundled Garnet

Place `garnet-server.exe` under `tools/garnet/win-x64/` before publish. The app will copy it to output and start it automatically when `--garnet-autostart true` and local Redis port is closed.

## Building

```bash
dotnet build
```

## Dependencies

- NDDTech.Devices.PrintControl.PrintersCache (local reference)
- Microsoft.Data.Sqlite
- Lextm.SharpSnmpLib
- ZiggyCreatures.FusionCache
