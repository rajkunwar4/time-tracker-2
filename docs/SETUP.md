# Development Setup

How to set up a fresh Windows machine to build, run, and test the TimeTrack client and
its backend. Verified on Windows 11.

---

## 1. Prerequisites

| Tool | Version | Why | Install |
|------|---------|-----|---------|
| **.NET SDK** | **9.0.x** | All projects target `net9.0` / `net9.0-windows` | `winget install Microsoft.DotNet.SDK.9` |
| **WinForms / Windows Desktop** | bundled | `TimeTrack.App` is a WinForms `WinExe` | Comes with the .NET SDK on Windows — no separate install |
| **Visual Studio 2022** *(optional)* | 17.12+ | IDE with debugger; needs .NET 9 support | `winget install Microsoft.VisualStudio.2022.Community --override "--add Microsoft.VisualStudio.Workload.ManagedDesktop --includeRecommended"` |
| **Git** | any recent | Source control | `winget install Git.Git` |
| **Node.js** | 18+ (tested on 24 LTS) | Runs the cloud API backend | `winget install OpenJS.NodeJS.LTS` |

> **PATH note:** after installing the SDK / Node via winget, a freshly opened terminal
> may not see `dotnet` or `node` until you start a **new** terminal (or sign out/in). If a
> script can't find them, the binaries live at `C:\Program Files\dotnet\` and
> `C:\Program Files\nodejs\`.

There is **no `global.json`**, so any .NET 9 SDK works — the version is not pinned.

---

## 2. Get the code

The client and the backend are **two separate repos** that sit side-by-side:

```
Desktop/
├─ time-tracking-revamp/      # this repo — the WinForms client
└─ equicom-legacy/backend/    # the cloud API (Node + Express + MongoDB)
```

---

## 3. Build & test the client

From the `time-tracking-revamp` root:

```powershell
dotnet build TimeTrack.sln          # restores NuGet packages + compiles all 4 projects
dotnet test  TimeTrack.sln          # runs the xUnit test suite
```

NuGet packages restore automatically on first build:
`Microsoft.Data.Sqlite`, `System.Security.Cryptography.ProtectedData`, and the xUnit stack.

A clean machine should see **0 warnings, 0 errors** and **all tests passing**.

---

## 4. Cloud API (backend)

The client talks to `http://127.0.0.1:4000/api/v1` (configurable — see
[appsettings](#6-client-configuration)). To run the backend:

```powershell
cd ..\equicom-legacy\backend
npm install            # first time only — builds native modules (bcrypt) for your Node
node index.js          # or: npm run dev   (nodemon auto-reload)
```

Key facts about the backend:

- Listens on **port 4000**, mounts everything under **`/api/v1`**.
- Connects to a **cloud MongoDB Atlas** cluster via `MONGODB_URL_DEVELOPMENT` in its
  `.env` — **no local MongoDB required**, but it **needs internet** at startup.
- Signs JWTs with `JWT_SECRET` from `.env` (default dev secret: `secret_equicom`),
  7-day expiry.
- **Health check:** `GET http://127.0.0.1:4000/` → `{"success":true,"message":"Your server is up and running...."}`

> If `npm install` was skipped and `node_modules` was copied from another machine, native
> modules like `bcrypt` may fail to load under a different Node version. Re-run
> `npm install` (or `npm rebuild bcrypt`) on the new machine.

---

## 5. Run the whole thing

1. **Start the backend** (section 4) and confirm the health check.
2. **Launch the client:**
   ```powershell
   dotnet run --project src/TimeTrack.App
   ```
   …or open `TimeTrack.sln` in Visual Studio, set **TimeTrack.App** as the startup
   project, and press **F5**.
3. **Sign in.** A known dev/test account is `ansh@gmail.com` / `1234` (role `EMPLOYEE`).
   Tracking starts automatically after login.

### Headless end-to-end check

To verify the **full client→API path without the GUI** (login → SQLite outbox → flush →
idempotency), run the smoke test against a live backend:

```powershell
dotnet run --project tools/TimeTrack.SmokeTest
# optional args: [baseUrl] [email] [password]
# defaults:      http://127.0.0.1:4000/api/v1  ansh@gmail.com  1234
```

Expected output: login OK with an employee id, 2 intervals queued, flush #1 accepts both
(pending → 0), flush #2 accepts 0 (nothing pending) — proving idempotent delivery.

---

## 6. Client configuration

`src/TimeTrack.App/appsettings.json` (copied next to the exe on build):

```json
{
  "Api":      { "BaseUrl": "http://127.0.0.1:4000/api/v1", "TimeoutSeconds": 15 },
  "Tracking": { "IdleThresholdSeconds": 300, "WindowSeconds": 60 }
}
```

| Setting | Meaning |
|---------|---------|
| `Api.BaseUrl` | Cloud API root, **including** the `/api/v1` prefix. |
| `Api.TimeoutSeconds` | Per-request HTTP timeout. |
| `Tracking.IdleThresholdSeconds` | No keyboard/mouse input for this long ⇒ time counts as **idle**, not work. |
| `Tracking.WindowSeconds` | How many seconds accumulate before an interval is closed and queued. |

Missing or invalid file ⇒ the app falls back to these same defaults (`AppSettings.Load`).

### Runtime data (auto-created next to the exe, under `Data/`)

- `Data/timetrack.db` — the SQLite outbox (+ `-wal` / `-shm` files in WAL mode).
- `Data/token.bin` — the API JWT, encrypted at rest with **Windows DPAPI** (current-user
  scope; only the logged-in Windows user can decrypt it).

---

## 7. Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| `'dotnet'`/`'node'` not recognized | New terminal needed after install; or call the full path under `C:\Program Files\`. |
| `EADDRINUSE :::4000` when starting backend | A backend instance is already listening on 4000. Reuse it, or find the PID with `Get-NetTCPConnection -LocalPort 4000` and stop it. |
| Backend exits with "Issue in connecting to database" | No internet, or the Atlas URI/credentials in `.env` are wrong/expired. |
| Login fails with "Could not reach the server" | Backend isn't running, or `Api.BaseUrl` points at the wrong host/port. |
| `bcrypt` load error on backend | `node_modules` from another machine — run `npm install` / `npm rebuild bcrypt`. |
