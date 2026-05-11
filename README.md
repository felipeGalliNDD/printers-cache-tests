# Printers Cache Tests

Console application for network printer discovery with SQLite persistence and Redis cache coordination.

## Features

- Network printer discovery via SNMP protocol
- SQLite local persistence for discovered printers
- Redis cache coordination for fast access
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
| `REDIS_CONNECTION_STRING` | Redis/Garnet connection | `localhost:6379` |
| `REDIS_PASSWORD` | Redis password (optional) | - |
| `SQLITE_DATABASE_PATH` | SQLite database file path | `printers.db` |

## Usage

```
1 - Create printer
2 - Read printer
3 - Scan and save network printers
```

## Building

```bash
dotnet build
```

## Dependencies

- NDDTech.Devices.PrintControl.PrintersCache (local reference)
- Microsoft.Data.Sqlite
- Lextm.SharpSnmpLib
- ZiggyCreatures.FusionCache