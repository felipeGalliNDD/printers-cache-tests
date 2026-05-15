using Microsoft.Data.Sqlite;
using NDDTech.Devices.PrintControl.PrintersCache.Models;

namespace PrintersCacheTests;

public class SqlitePrinterStore : ISqlitePrinterStore
{
    private readonly string _connectionString;

    public SqlitePrinterStore(string databasePath)
    {
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Printers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                IpAddress TEXT NULL,
                Name TEXT NULL,
                Serial TEXT NULL,
                Manufacturer TEXT NULL,
                Model TEXT NULL
            )
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PrinterData?> GetByIpAddressAsync(string ipAddress, CancellationToken cancellationToken)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT IpAddress, Name, Serial, Manufacturer, Model
            FROM Printers
            WHERE IpAddress = @IpAddress
            ORDER BY Id DESC
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@IpAddress", ipAddress);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new PrinterData
            {
                IpAddress = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                Serial = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Manufacturer = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Model = reader.IsDBNull(4) ? string.Empty : reader.GetString(4)
            };
        }

        return null;
    }

    public async Task UpsertByIpAddressAsync(PrinterData printerData, CancellationToken cancellationToken)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var ipAddress = string.IsNullOrWhiteSpace(printerData.IpAddress) ? null : printerData.IpAddress;

        if (ipAddress is null)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Printers (IpAddress, Name, Serial, Manufacturer, Model)
                VALUES (NULL, @Name, @Serial, @Manufacturer, @Model)
                """;
            cmd.Parameters.AddWithValue("@Name", printerData.Name);
            cmd.Parameters.AddWithValue("@Serial", printerData.Serial);
            cmd.Parameters.AddWithValue("@Manufacturer", printerData.Manufacturer);
            cmd.Parameters.AddWithValue("@Model", printerData.Model);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            using var updateCmd = conn.CreateCommand();
            updateCmd.CommandText = """
                UPDATE Printers
                SET Name = @Name, Serial = @Serial, Manufacturer = @Manufacturer, Model = @Model
                WHERE IpAddress = @IpAddress
                """;
            updateCmd.Parameters.AddWithValue("@IpAddress", ipAddress);
            updateCmd.Parameters.AddWithValue("@Name", printerData.Name);
            updateCmd.Parameters.AddWithValue("@Serial", printerData.Serial);
            updateCmd.Parameters.AddWithValue("@Manufacturer", printerData.Manufacturer);
            updateCmd.Parameters.AddWithValue("@Model", printerData.Model);

            var rowsAffected = await updateCmd.ExecuteNonQueryAsync(cancellationToken);

            if (rowsAffected == 0)
            {
                using var insertCmd = conn.CreateCommand();
                insertCmd.CommandText = """
                    INSERT INTO Printers (IpAddress, Name, Serial, Manufacturer, Model)
                    VALUES (@IpAddress, @Name, @Serial, @Manufacturer, @Model)
                    """;
                insertCmd.Parameters.AddWithValue("@IpAddress", ipAddress);
                insertCmd.Parameters.AddWithValue("@Name", printerData.Name);
                insertCmd.Parameters.AddWithValue("@Serial", printerData.Serial);
                insertCmd.Parameters.AddWithValue("@Manufacturer", printerData.Manufacturer);
                insertCmd.Parameters.AddWithValue("@Model", printerData.Model);
                await insertCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }
}
