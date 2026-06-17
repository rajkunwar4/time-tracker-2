# How It Works — Core Logic

This document explains the **application as it exists in code today**: every moving part
of the tracking → outbox → sync → ingest pipeline, and the auth that protects it. For the
broader vision (the time-boxed internet-window scheduled task, the phased plan), see
[REDESIGN.md](REDESIGN.md); where the current code differs from that vision it's called out
under [What's not built yet](#whats-not-built-yet).

---

## 1. The problem this solves

Employee PCs are **only allowed internet in short windows** (a data-protection policy), so
the app cannot assume a live connection. It is built for an **occasionally-connected,
store-and-forward** world:

- Record work time **locally, always**, even with no network.
- **Flush** the backlog to the cloud in short bursts (today: on logout).
- Guarantee that a batch retried after a long offline stretch is **never double-counted**.

The delivery guarantee comes from two things working together: a **durable SQLite queue**
and a **client-generated idempotency key (GUID)** on every interval.

---

## 2. The big picture

```
        EMPLOYEE PC (non-admin WinForms app)                    CLOUD (we own it)
┌───────────────────────────────────────────────┐
│  FrmLogin ──login──► TimeTrackApiClient ───────┼──HTTPS──►  POST /api/v1/auth/login
│     │                                          │            (bcrypt verify → JWT, 7d)
│     │ JWT stored via DPAPI (token.bin)         │
│     ▼                                          │
│  FrmMain  (1-second timer)                     │
│     │  every tick: active? idle? on break?     │
│     ▼                                          │
│  WindowedIntervalCollector                     │
│     │  fills a 60s window → IntervalRecord     │
│     ▼                                          │
│  SqliteOutboxRepository  (durable, WAL)        │
│     │  pending_uploads table, GUID PK          │
│     ▼   on logout                              │
│  OutboxSyncService.FlushAsync(token) ──────────┼──HTTPS──►  POST /api/v1/timeTracking/intervals
│         (batched, Bearer token)                │            (auth middleware → JWT → employeeId)
└───────────────────────────────────────────────┘                     │
                                                          idempotent upsert into
                                                          ProcessedInterval (ledger, GUID _id)
                                                          then aggregate new seconds into
                                                          DailyTrackedWork (per employee/day)
```

Two projects make up the client:

- **`TimeTrack.Core`** — platform-agnostic logic and the API client. No WinForms
  dependency, fully unit-tested.
- **`TimeTrack.App`** — the WinForms UI plus Windows-only services (idle detection via
  Win32, DPAPI token storage).

---

## 3. Startup & composition

`src/TimeTrack.App/Program.cs` is the composition root:

1. **Single-instance guard** — a named `Mutex` (`TimeTrack_SingleInstance_Mutex`) ensures
   only one tracker runs per session; a second launch shows a message and exits.
2. Creates a `Data/` folder next to the exe and loads `appsettings.json`
   (`AppSettings.Load` — falls back to defaults if missing/invalid).
3. Wires up the services:
   - `SqliteOutboxRepository(Data/timetrack.db)` and calls `InitializeAsync()` once
     (creates the table + index if needed).
   - `TimeTrackApiClient(BaseUrl, TimeoutSeconds)`.
   - `DpapiTokenStore(Data/token.bin)`.
4. Shows **`FrmLogin`** as a modal dialog. Only on `DialogResult.OK` does it proceed.
5. Runs **`FrmMain`** — the home screen — which begins tracking immediately.

So the lifecycle is strictly **login first, then track**.

---

## 4. Authentication

### Client side
- `FrmLogin.TrySignInAsync` (`src/TimeTrack.App/Forms/FrmLogin.cs`) calls
  `TimeTrackApiClient.LoginAsync(email, password)`.
- `LoginAsync` (`src/TimeTrack.Core/Api/TimeTrackApiClient.cs`) does
  `POST {BaseUrl}/auth/login` with `{ email, password }` and parses
  `{ success, token, user:{ _id, email, role } }`.
- On success the JWT + identity is saved via **DPAPI** to `Data/token.bin`
  (`DpapiTokenStore.Save`), encrypted to the current Windows user. The email is handed to
  `FrmMain`.

### Server side
- `POST /api/v1/auth/login` → `login` controller
  (`equicom-legacy/backend/controllers/auth/auth.controller.js`): looks up the user, checks
  `active`, `bcrypt.compare`s the password, then `jwt.sign({ email, id, role }, JWT_SECRET,
  { expiresIn: "7d" })`. Returns the token and the user object.
- The protected ingest route uses the **`auth` middleware**
  (`equicom-legacy/backend/middlewares/auth.js`): it reads the token from the
  `Authorization: Bearer …` header (or body/cookie), `jwt.verify`s it, and sets `req.user`.
  **`employeeId` is taken from the JWT, never from the request body** — a client can only
  ever write its own time.

### Token storage (DPAPI)
`DpapiTokenStore` (`src/TimeTrack.App/Services/DpapiTokenStore.cs`) serializes the
`StoredToken` to JSON and encrypts it with `ProtectedData.Protect(...,
DataProtectionScope.CurrentUser)`. Corrupt/unreadable files are treated as "no token".

---

## 5. Tracking: turning seconds into intervals

All of this lives in `FrmMain` (`src/TimeTrack.App/Forms/FrmMain.cs`) and
`WindowedIntervalCollector` (`src/TimeTrack.Core/Tracking/WindowedIntervalCollector.cs`).

### The per-second tick (`FrmMain.OnTick`)
A `System.Windows.Forms.Timer` fires once per second. Each tick decides the user's state:

```
idle = Win32Idle.GetIdleTime() >= IdleThresholdSeconds (default 300s)

if    on break → breakSeconds++   , active = false
elif  idle     → idleSeconds++    , active = false
else           → workSeconds++    , active = true
```

- **Idle detection** is real and system-wide: `Win32Idle.GetIdleTime`
  (`src/TimeTrack.App/Services/Win32Idle.cs`) calls the Win32 `GetLastInputInfo` API and
  returns time since the last keyboard/mouse input. Idle seconds are simply **never counted
  as active**, so they can't inflate work time.
- **Break** is a manual toggle (the "Take a break" button). While on break the work timer
  is paused and time accrues to the break bucket.

### Windowing (`WindowedIntervalCollector`)
The collector accumulates `active` seconds into a **fixed-length window**
(`Tracking.WindowSeconds`, default **60s**):

- `Tick(active)` is called every second; it increments elapsed (and active, if active).
- When `elapsed` reaches the window size, it **closes the window** and returns a completed
  `IntervalRecord { Email, WindowStartUtc, WindowEndUtc, ActiveSeconds }`, then resets.
- `Flush()` force-closes a **partial** window (used at logout) so the last < 60s aren't
  lost.

Back in `OnTick`, a returned record is enqueued **only if `ActiveSeconds > 0`** — fully
idle windows produce nothing to send.

### `IntervalRecord` — the unit of work
`src/TimeTrack.Core/Models/IntervalRecord.cs`. The important field is:

- **`Id` — a client-generated `Guid`** (default `Guid.NewGuid()`). This is the stable
  **idempotency key** the whole delivery guarantee hinges on.

Plus `Email`, `WindowStartUtc`, `WindowEndUtc`, `ActiveSeconds`, `CreatedUtc`, and outbox
bookkeeping (`Status`, `AttemptCount`, `LastAttemptUtc`, `LastError`).

---

## 6. The durable outbox (SQLite)

`SqliteOutboxRepository` (`src/TimeTrack.Core/Storage/SqliteOutboxRepository.cs`) is the
local store-and-forward queue. It runs in **WAL mode** for crash safety.

**Schema** (`pending_uploads`):

| Column | Notes |
|--------|-------|
| `id` (TEXT, **PRIMARY KEY**) | the interval GUID — makes enqueue idempotent |
| `email` | who recorded it |
| `window_start_utc`, `window_end_utc` | ISO-8601 round-trip UTC |
| `active_seconds` | the payload |
| `created_utc` | enqueue order |
| `status` (INT) | `0 Pending`, `1 Sending`, `2 Sent` (see `OutboxStatus`) |
| `attempt_count`, `last_attempt_utc`, `last_error` | retry metadata |

Index `ix_pending_status (status, created_utc)` keeps the "pending, oldest first" query fast.

**Operations:**
- `EnqueueAsync` → `INSERT OR IGNORE` on the GUID PK. Enqueue is therefore **idempotent**:
  re-adding the same interval is a no-op.
- `GetPendingBatchAsync(max)` → oldest-first rows `WHERE status <> Sent`, limited to `max`.
- `MarkSentAsync(ids)` → set `status = Sent` (transactional).
- `MarkFailedAsync(ids, error)` → keep `status = Pending`, **bump `attempt_count`**, record
  `last_attempt_utc` / `last_error` so the item retries next time.
- `CountPendingAsync` → everything not yet `Sent` (drives the UI "N queued" note).

Because state lives in a real database file, **an app crash or power loss loses nothing** —
on next launch the same pending rows are still there to send.

---

## 7. Sync: draining the outbox to the API

`OutboxSyncService.FlushAsync(token)` (`src/TimeTrack.Core/Sync/OutboxSyncService.cs`)
drains the queue in batches (default **200** per request):

```
loop:
  batch = outbox.GetPendingBatch(200)
  if batch empty → stop
  result = api.PostIntervals(token, batch)
  if !result.Success:                      # transport / auth / server error
      outbox.MarkFailed(batch ids)         # leave queued, bump attempts
      stop and report failure              # everything safe locally, retries next window
  else:
      outbox.MarkSent(result.AcceptedIds)
      notAccepted = batch − accepted       # e.g. validation-rejected by server
      outbox.MarkFailed(notAccepted)       # so they aren't re-fetched forever
  if batch < 200 or nothing accepted → stop
```

Key properties:
- **Fail-safe:** the first transport/auth failure stops the loop and leaves data queued.
  Nothing is ever deleted on failure.
- **Forward progress:** only ids the server explicitly accepted are marked `Sent`.
- **No infinite loop:** server-rejected rows are marked failed (not re-fetched endlessly),
  and the loop ends when a page is short or no progress is made.

### The wire call — `TimeTrackApiClient.PostIntervalsAsync`
`POST {BaseUrl}/timeTracking/intervals` with `Authorization: Bearer <token>` and a JSON
array of:

```json
{ "id": "<guid>", "windowStartUtc": "...", "windowEndUtc": "...",
  "activeSeconds": 12, "clientSentUtc": "..." }
```

It parses `data.acceptedIds` back out and returns them as the accepted set.

---

## 8. Server-side ingest (idempotent)

`addTrackedIntervals`
(`equicom-legacy/backend/controllers/timeTracking/trackedIntervals.controller.js`),
protected by the `auth` middleware:

1. `employeeId = req.user.id` (from the JWT — **not** the body).
2. Accepts an array (or `{ intervals: [...] }`); **validates** each item (non-empty id,
   finite `activeSeconds ≥ 0`, parseable start/end). Invalid items go to `rejected`.
3. For each valid interval, **upsert into the `ProcessedInterval` ledger** keyed by the
   interval GUID (`_id`), using `$setOnInsert`:
   - Every persisted id is added to `acceptedIds` (so the client marks it sent either way).
   - **`upsertedCount === 1`** means *this* request created the row → only **those** seconds
     are counted. A retried/duplicate GUID updates nothing and contributes **zero** seconds.
4. Newly-inserted seconds are aggregated into **`DailyTrackedWork`** per `(employeeId, day)`
   via `$inc` on the tracked-seconds totals, with `$min`/`$max` keeping the day's
   start/end correct even for **out-of-order buffered** data.
5. Responds with `{ acceptedIds, accepted, inserted, duplicates, rejected, serverTimeUtc }`.

> **Why it can't double-count:** the ledger write (idempotency check) happens **before** the
> daily increment, and only first-time inserts increment. The worst case of a mid-request
> crash is *dropping* some seconds (safe), never counting them twice.

This is the server half of the same GUID-based idempotency the client generates in §5.

---

## 9. Logout = flush (`FrmMain.LogoutAndSyncAsync`)

Pressing **Log out**:

1. Stops the tick timer and guards against re-entry.
2. `_collector.Flush()` closes the **partial** window and enqueues it (if it has active
   seconds), so the final minutes aren't lost.
3. Loads the JWT from `DpapiTokenStore`. If none, prompts to sign in again.
4. Calls `OutboxSyncService.FlushAsync(token)` and reports the outcome:
   - success → "Synced N interval(s). M still queued."
   - failure → "Couldn't sync … Your data is safe locally and will retry next time."
5. Closes the app.

So a normal day is: **log in → track all day offline → log out → one burst sync**.

---

## 10. The home screen UI

`FrmMain` is a custom-painted, frameless window (≈460×432). It shows:

- A **status pill** ("Working" / "On break · m:ss").
- A large **work timer** (`HH:MM:SS`) — work seconds only; idle/break excluded.
- A **day timeline** bar split into Work / Break / Idle proportions.
- Three **stat cards**: Logged-in time, Breaks, Idle.
- **Take a break / Resume** and **Log out** buttons.
- An **offline note** that live-updates with the queued-interval count.

The UI is authored entirely in code (no WinForms designer) — hence the project's
`NoWarn;WFO1000`. Custom controls (`RoundedButton`, `RoundedPanel`, `Draw`, `Theme`) live
under `src/TimeTrack.App/Ui/`.

---

## 11. Data & security summary

| Concern | How it's handled |
|---------|------------------|
| **Durability** | SQLite WAL outbox; survives crash/power loss. |
| **Idempotency** | Client GUID per interval; `INSERT OR IGNORE` locally + ledger upsert server-side. |
| **No double-count** | Only `upsertedCount === 1` increments `DailyTrackedWork`. |
| **Auth** | JWT (7-day), `employeeId` derived from token server-side. |
| **Token at rest** | DPAPI (current-user) encryption of `token.bin`. |
| **Idle integrity** | Win32 `GetLastInputInfo`; idle seconds never counted as work. |
| **Clock skew** | Server stamps `serverTimeUtc`; days bucketed by UTC start-of-day. |

---

## 12. Built recently / still to build

**Recently built** (see [PLANNING.md](PLANNING.md) for the full plan):
- **Window lifecycle & tray** — standard resizable window; login⇆main loop; close→tray
  ([`TrackerAppContext`](../src/TimeTrack.App/TrackerAppContext.cs)).
- **Auto-start at sign-in** — per-user `HKCU\…\Run`, non-admin
  ([`AutoStart`](../src/TimeTrack.App/Services/AutoStart.cs)).
- **Offline login** — on a successful online login, [`FrmLogin`](../src/TimeTrack.App/Forms/FrmLogin.cs)
  caches a DPAPI-protected **PBKDF2 verifier** ([`PasswordVerifier`](../src/TimeTrack.Core/Security/PasswordVerifier.cs));
  if the server is later *unreachable*, the same credentials authenticate offline and the user
  tracks locally. A reachable server rejecting the password is still a hard failure.
- **Internet window** — sync opens a time-boxed window before flushing and closes it after
  ([`IInternetWindow`](../src/TimeTrack.Core/Sync/IInternetWindow.cs)). Release toggles the Windows
  system proxy to the master gateway (`192.168.137.1:808`) with a WinINET refresh + exit/timer
  failsafe ([`SystemProxyInternetWindow`](../src/TimeTrack.App/Services/SystemProxyInternetWindow.cs));
  Debug stays direct (NoOp).

**Still to build:**
- **Installer / packaging** story.
- **Retry backoff / poison-message handling.** `attempt_count` is recorded but not yet used
  to delay or quarantine repeatedly-failing intervals.

---

## 13. Where to look in the code

| Concern | File |
|---------|------|
| Composition / startup | `src/TimeTrack.App/Program.cs` |
| Login UI | `src/TimeTrack.App/Forms/FrmLogin.cs` |
| Home screen + tick loop | `src/TimeTrack.App/Forms/FrmMain.cs` |
| Idle detection | `src/TimeTrack.App/Services/Win32Idle.cs` |
| Token storage (DPAPI) | `src/TimeTrack.App/Services/DpapiTokenStore.cs` |
| Windowing | `src/TimeTrack.Core/Tracking/WindowedIntervalCollector.cs` |
| Interval model | `src/TimeTrack.Core/Models/IntervalRecord.cs` |
| Outbox (SQLite) | `src/TimeTrack.Core/Storage/SqliteOutboxRepository.cs` |
| Sync engine | `src/TimeTrack.Core/Sync/OutboxSyncService.cs` |
| API client + DTOs | `src/TimeTrack.Core/Api/TimeTrackApiClient.cs`, `ApiContracts.cs` |
| Config | `src/TimeTrack.Core/Configuration/AppSettings.cs` |
| Backend login | `equicom-legacy/backend/controllers/auth/auth.controller.js` |
| Backend auth middleware | `equicom-legacy/backend/middlewares/auth.js` |
| Backend ingest | `equicom-legacy/backend/controllers/timeTracking/trackedIntervals.controller.js` |
| Backend routes | `equicom-legacy/backend/routes/timeTracking.route.js` |
