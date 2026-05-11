## ADDED Requirements

### Requirement: Lookup Printers by IpAddress in SQLite
The system SHALL use `IpAddress` as the lookup key when checking SQLite for a printer record.

#### Scenario: SQLite contains printer for IpAddress
- **WHEN** a lookup is performed for an `IpAddress` that exists in SQLite
- **THEN** the system retrieves the stored printer record from SQLite

#### Scenario: SQLite does not contain printer for IpAddress
- **WHEN** a lookup is performed for an `IpAddress` that does not exist in SQLite
- **THEN** the system treats the printer as missing from SQLite and continues the discovery flow

### Requirement: Hydrate Redis from SQLite
When SQLite contains a printer record for an `IpAddress` and Redis does not, the system SHALL save that printer record into Redis before continuing.

#### Scenario: Redis miss after SQLite hit
- **WHEN** SQLite contains a printer record for an `IpAddress` and Redis does not
- **THEN** the system copies the SQLite record into Redis using the same `IpAddress`

#### Scenario: Redis hit after SQLite hit
- **WHEN** SQLite contains a printer record for an `IpAddress` and Redis also contains that printer
- **THEN** the system uses the Redis record without querying SNMP

### Requirement: Discover Missing Printers via SNMP
When SQLite does not contain a printer record for an `IpAddress`, the system SHALL query SNMP for that `IpAddress` and save any discovered printer data to both SQLite and Redis.

#### Scenario: SNMP finds printer after SQLite miss
- **WHEN** SQLite does not contain a printer record for an `IpAddress` and SNMP returns printer data
- **THEN** the system saves the discovered printer record to SQLite and Redis

#### Scenario: SNMP does not find printer after SQLite miss
- **WHEN** SQLite does not contain a printer record for an `IpAddress` and SNMP does not return printer data
- **THEN** the system does not persist a printer record

### Requirement: Persist Printer Data in Both Stores
The system SHALL keep Redis and SQLite synchronized for discovered printers saved through the discovery flow.

#### Scenario: Save discovered printer to both stores
- **WHEN** the discovery flow finds a printer via SNMP
- **THEN** the system stores the printer data in both SQLite and Redis using `IpAddress`

#### Scenario: Update existing printer in both stores
- **WHEN** the discovery flow finds updated printer data for an existing `IpAddress`
- **THEN** the system overwrites the prior values in both SQLite and Redis
