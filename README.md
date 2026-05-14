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

### Garnet Tool

When `--garnet-autostart true` and local Redis port is closed, the app first checks Redis `PING`. If Garnet is not already running, it checks for global `garnet-server`. If missing, it runs `dotnet tool install -g garnet-server`, then starts `garnet-server` detached. It keeps running after app exits.

## Building

```bash
dotnet build
```

## Dependencies

- NDDTech.Devices.PrintControl.PrintersCache (local reference)
- Microsoft.Data.Sqlite
- Lextm.SharpSnmpLib
- ZiggyCreatures.FusionCache
