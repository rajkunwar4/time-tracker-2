using TimeTrack.Core.Api;
using TimeTrack.Core.Models;
using TimeTrack.Core.Storage;
using TimeTrack.Core.Sync;

// Live end-to-end check of the CLIENT code path against a running backend:
//   login -> JWT -> enqueue intervals in a real SQLite outbox -> flush to the API.
//
// Usage: dotnet run --project tools/TimeTrack.SmokeTest [baseUrl] [email] [password]

string baseUrl = args.Length > 0 ? args[0] : "http://127.0.0.1:4000/api/v1";
string email = args.Length > 1 ? args[1] : "ansh@gmail.com";
string password = args.Length > 2 ? args[2] : "1234";

Console.WriteLine($"Base URL : {baseUrl}");
Console.WriteLine($"Account  : {email}");

var api = new TimeTrackApiClient(baseUrl);

Console.WriteLine("\n[1] Login…");
var login = await api.LoginAsync(email, password);
if (!login.Success)
{
    Console.WriteLine($"    LOGIN FAILED: {login.Error}");
    return 1;
}
Console.WriteLine($"    OK  employeeId={login.EmployeeId}  role={login.Role}  token.len={login.Token.Length}");

var dbPath = Path.Combine(Path.GetTempPath(), $"tt_smoke_{Guid.NewGuid():N}.db");
var outbox = new SqliteOutboxRepository(dbPath);
await outbox.InitializeAsync();

var r1 = new IntervalRecord { Email = email, WindowStartUtc = DateTime.UtcNow.AddMinutes(-2), WindowEndUtc = DateTime.UtcNow.AddMinutes(-1), ActiveSeconds = 12 };
var r2 = new IntervalRecord { Email = email, WindowStartUtc = DateTime.UtcNow.AddMinutes(-1), WindowEndUtc = DateTime.UtcNow, ActiveSeconds = 8 };
await outbox.EnqueueAsync(r1);
await outbox.EnqueueAsync(r2);
Console.WriteLine($"\n[2] Queued 2 intervals (12s + 8s). pending={await outbox.CountPendingAsync()}");

var sync = new OutboxSyncService(outbox, api);

Console.WriteLine("\n[3] Flush #1…");
var res1 = await sync.FlushAsync(login.Token);
Console.WriteLine($"    success={res1.Success}  accepted={res1.AcceptedCount}  error={res1.Error}");
Console.WriteLine($"    pending after flush = {await outbox.CountPendingAsync()}  (expect 0)");

Console.WriteLine("\n[4] Flush #2 (nothing pending)…");
var res2 = await sync.FlushAsync(login.Token);
Console.WriteLine($"    success={res2.Success}  accepted={res2.AcceptedCount}  (expect 0)");

foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
    try { if (File.Exists(p)) File.Delete(p); } catch { }

Console.WriteLine("\nDONE");
return 0;
