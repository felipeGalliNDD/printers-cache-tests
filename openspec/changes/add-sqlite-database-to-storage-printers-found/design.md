## Context

The console workflow currently writes printer data into Redis/Garnet through `IPrinterDistributedCache`. The new flow keeps Redis as the cache layer, adds SQLite as a durable local store, and uses `IpAddress` as the shared lookup key. SNMP should only run when a printer is missing from SQLite.

## Goals / Non-Goals

**Goals:**
- Keep Redis as the cache while adding SQLite as the durable source of truth for discovered printers.
- Use `IpAddress` to coordinate lookup, hydration, and persistence across SQLite and Redis.
- Hydrate Redis from SQLite before falling back to SNMP.

**Non-Goals:**
- Replacing or removing the existing Redis/Garnet backend.
- Introducing remote synchronization, replication, or multi-node coordination for SQLite data.
- Redesigning SNMP discovery logic or printer parsing behavior.

## Decisions

1. Keep Redis in the primary read/write path and add SQLite as a companion store.
   - Rationale: preserves current cache behavior while giving the app a durable local lookup source.
   - Alternative considered: replace Redis with SQLite. Rejected because Redis must remain the active cache.

2. Use `IpAddress` as the lookup key in both Redis and SQLite.
   - Rationale: the flow needs a single stable identifier across cache hydration, storage, and lookup.
   - Alternative considered: keep matching by a different field. Rejected because the requirement explicitly keys on `IpAddress`.

3. On SQLite hit, check Redis before SNMP.
   - Rationale: avoids unnecessary network discovery and allows Redis cache hydration from SQLite.
   - Alternative considered: always query SNMP first. Rejected because the user wants SQLite to gate SNMP usage.

4. On SQLite miss, query SNMP and save the result to both SQLite and Redis.
   - Rationale: makes SNMP the fallback discovery source and keeps both stores synchronized.
   - Alternative considered: save only to Redis. Rejected because SQLite is intended to persist the discovered printer.

5. Ensure database and table initialization occurs automatically during startup or first write.
   - Rationale: removes manual setup and keeps local developer workflow simple.
   - Alternative considered: external migration step. Rejected because it adds avoidable operational overhead for a lightweight store.

## Risks / Trade-offs

- SQLite file locking under high write contention may limit throughput compared to Redis.
  - Mitigation: keep transactions short and document expected single-process usage.
- Redis and SQLite can diverge if writes fail to one store but not the other.
  - Mitigation: treat each dual-write step as retryable and surface errors clearly.
- Existing deployments may assume Redis-only behavior.
  - Mitigation: preserve Redis as the cache layer and add SQLite as an additional dependency only.

## Migration Plan

1. Add SQLite package dependency and implementation classes.
2. Add configuration keys for SQLite database path.
3. Wire the scan/read flow to check SQLite first, then Redis, then SNMP.
4. Validate by scanning and reading printer records with SQLite and Redis enabled.

Rollback strategy:
- Disable SQLite usage and return to Redis-only behavior; no API surface change is required.

## Open Questions

- Should Redis cache hydration happen on every SQLite hit, or only when Redis is missing the record?
