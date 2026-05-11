## 1. Configuration and Dependencies

- [ ] 1.1 Add a SQLite .NET package dependency required to open connections and execute commands.
- [ ] 1.2 Introduce configuration keys for the SQLite database path.
- [ ] 1.3 Keep Redis/Garnet configuration intact so both stores remain available.

## 2. SQLite Storage Implementation

- [ ] 2.1 Create a SQLite-backed printer repository keyed by `IpAddress`.
- [ ] 2.2 Implement automatic database/table initialization when SQLite is first used.
- [ ] 2.3 Implement upsert behavior keyed by `IpAddress` to update existing records.
- [ ] 2.4 Implement read-by-`IpAddress` behavior returning null when no matching record exists.

## 3. Application Wiring and Flow

- [ ] 3.1 Update lookup flow to check SQLite first by `IpAddress`.
- [ ] 3.2 Hydrate Redis from SQLite when a printer exists in SQLite but is missing from cache.
- [ ] 3.3 Fall back to SNMP only when SQLite does not contain the printer.
- [ ] 3.4 Save SNMP-discovered printers to both SQLite and Redis.

## 4. Verification

- [ ] 4.1 Verify a SQLite hit hydrates Redis when the cache is missing the same `IpAddress`.
- [ ] 4.2 Verify a SQLite miss triggers SNMP lookup and persists the discovered printer to both stores.
- [ ] 4.3 Verify duplicate saves for the same `IpAddress` overwrite prior values in both stores.
- [ ] 4.4 Verify missing printer data does not create records in either store.
