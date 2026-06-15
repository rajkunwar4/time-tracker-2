using System.Globalization;
using Microsoft.Data.Sqlite;
using TimeTrack.Core.Models;

namespace TimeTrack.Core.Storage;

/// <summary>
/// SQLite-backed durable outbox (WAL mode for crash safety). Replaces the legacy
/// CSV / JSON-lines outbox: gives per-record status, retry metadata, and a stable
/// idempotency key so long-buffered batches can be retried without double-counting.
/// </summary>
public sealed class SqliteOutboxRepository : IOutboxRepository
{
    private readonly string _connectionString;

    public SqliteOutboxRepository(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;

            CREATE TABLE IF NOT EXISTS pending_uploads (
                id               TEXT    PRIMARY KEY,
                email            TEXT    NOT NULL,
                window_start_utc TEXT    NOT NULL,
                window_end_utc   TEXT    NOT NULL,
                active_seconds   INTEGER NOT NULL,
                created_utc      TEXT    NOT NULL,
                status           INTEGER NOT NULL DEFAULT 0,
                attempt_count    INTEGER NOT NULL DEFAULT 0,
                last_attempt_utc TEXT    NULL,
                last_error       TEXT    NULL
            );

            CREATE INDEX IF NOT EXISTS ix_pending_status
                ON pending_uploads(status, created_utc);
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task EnqueueAsync(IntervalRecord r, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        // INSERT OR IGNORE makes enqueue idempotent on the GUID primary key.
        cmd.CommandText = """
            INSERT OR IGNORE INTO pending_uploads
                (id, email, window_start_utc, window_end_utc, active_seconds, created_utc, status, attempt_count)
            VALUES ($id, $email, $ws, $we, $secs, $created, $status, 0);
            """;
        cmd.Parameters.AddWithValue("$id", r.Id.ToString());
        cmd.Parameters.AddWithValue("$email", r.Email);
        cmd.Parameters.AddWithValue("$ws", Iso(r.WindowStartUtc));
        cmd.Parameters.AddWithValue("$we", Iso(r.WindowEndUtc));
        cmd.Parameters.AddWithValue("$secs", r.ActiveSeconds);
        cmd.Parameters.AddWithValue("$created", Iso(r.CreatedUtc));
        cmd.Parameters.AddWithValue("$status", (int)OutboxStatus.Pending);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<IntervalRecord>> GetPendingBatchAsync(int max, CancellationToken ct = default)
    {
        var list = new List<IntervalRecord>();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, email, window_start_utc, window_end_utc, active_seconds,
                   created_utc, status, attempt_count, last_attempt_utc, last_error
            FROM pending_uploads
            WHERE status <> $sent
            ORDER BY created_utc
            LIMIT $max;
            """;
        cmd.Parameters.AddWithValue("$sent", (int)OutboxStatus.Sent);
        cmd.Parameters.AddWithValue("$max", max);

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            list.Add(new IntervalRecord
            {
                Id = Guid.Parse(rd.GetString(0)),
                Email = rd.GetString(1),
                WindowStartUtc = ParseUtc(rd.GetString(2)),
                WindowEndUtc = ParseUtc(rd.GetString(3)),
                ActiveSeconds = rd.GetInt32(4),
                CreatedUtc = ParseUtc(rd.GetString(5)),
                Status = (OutboxStatus)rd.GetInt32(6),
                AttemptCount = rd.GetInt32(7),
                LastAttemptUtc = rd.IsDBNull(8) ? null : ParseUtc(rd.GetString(8)),
                LastError = rd.IsDBNull(9) ? null : rd.GetString(9)
            });
        }
        return list;
    }

    public async Task MarkSentAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        foreach (var id in ids)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE pending_uploads SET status = $sent WHERE id = $id;";
            cmd.Parameters.AddWithValue("$sent", (int)OutboxStatus.Sent);
            cmd.Parameters.AddWithValue("$id", id.ToString());
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task MarkFailedAsync(IEnumerable<Guid> ids, string error, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        foreach (var id in ids)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE pending_uploads
                SET status = $pending,
                    attempt_count = attempt_count + 1,
                    last_attempt_utc = $now,
                    last_error = $err
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$pending", (int)OutboxStatus.Pending);
            cmd.Parameters.AddWithValue("$now", Iso(DateTime.UtcNow));
            cmd.Parameters.AddWithValue("$err", error);
            cmd.Parameters.AddWithValue("$id", id.ToString());
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<int> CountPendingAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pending_uploads WHERE status <> $sent;";
        cmd.Parameters.AddWithValue("$sent", (int)OutboxStatus.Sent);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static string Iso(DateTime dt) => dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTime ParseUtc(string s) =>
        DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}
