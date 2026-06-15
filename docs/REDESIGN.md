# Time Tracking Revamp — Design Blueprint

This is the architectural blueprint for the rewrite. It captures *why* we're changing the design,
the target architecture, and the concrete mechanisms for the hard parts (the timed internet window,
local durability, and the new API contract), plus a phased execution plan.

> Status: design. No code is written against this yet. Decisions marked **[locked]** are settled;
> items under **Open questions** still need confirmation before the affected phase starts.

---

## 1. Context — why we're rebuilding

**Legacy flow:** `child PC → on-prem Server PC (a VM on an old Windows box) → cloud API`.

Pain points:
- The Server PC / VM hop was a **bottleneck** and operationally fragile (old OS, VM upkeep).
- The legacy "online" login path was never actually implemented server-side (the middleman only had
  `/ping` and `/upload`), so auth silently fell back to per-machine credential files.
- The design implicitly assumed an **always-available path to the API**.

**New reality:** client PCs are **not permitted continuous internet** — a data-protection policy
limits them to **short windows (~15 min)**. The system must therefore be **occasionally-connected**:
record locally all the time, sync in bursts when internet is granted.

**Goals of the revamp:**
1. Remove the middleman — client talks **directly** to the cloud API.
2. Be robust to being **offline for long stretches** (hours/days) and flushing in a brief window.
3. Keep the client **simple to deploy**: a single app, **auto-starting**, **no Windows Service**,
   **non-admin at runtime**.
4. Guarantee **no lost and no double-counted** time data.

---

## 2. Target architecture [locked]

```
┌─ EMPLOYEE PC ───────────────────────────────────────────────────────────┐
│                                                                          │
│  Tracker app  (non-admin, auto-starts at logon, runs quietly)            │
│   • offline-capable login                                                │
│   • activity monitor (active vs idle seconds)                            │
│   • writes interval records → local SQLite outbox                        │
│   • opportunistic + on-demand sync engine                                │
│   • "Log off & sync" → request a timed internet window, flush queue      │
│                    │                                                      │
│                    │ triggers (schtasks /run — no UAC, no admin held)    │
│                    ▼                                                      │
│  Scheduled tasks  (created once at install, run as SYSTEM)               │
│   • EnableInternetWindow → enable adapter + arm auto-disable (+15 min)    │
│   • DisableInternet      → disable adapter                               │
└───────────────────────────────────────────────┬─────────────────────────┘
                                                 │ HTTPS — token auth, idempotent batch
                                                 ▼
                                   ┌──────────────────────────────┐
                                   │ Cloud API (we own it)        │
                                   │ • POST /auth/login → token    │
                                   │ • POST /timeTracking/intervals│
                                   │     - dedupe on client ID     │
                                   │     - aggregate server-side   │
                                   │     - record server-receipt   │
                                   └──────────────────────────────┘
```

**Why app-only (no service):** simpler to ship and reason about. The cost — a sync interrupted by the
user powering off — is absorbed by the durable queue: unsent records just go in the next window. The
queue + idempotency, not the service, is the delivery guarantee.

---

## 3. The timed internet window [locked]

### Requirement
At end of day (or on demand), grant **whole-machine** internet for a bounded period (~15 min), let the
app flush, then turn internet off — and make sure it turns off **even if the app dies**.

### Why a pre-installed SYSTEM scheduled task (not an elevated app)
Toggling a network adapter needs **admin**. Two ways to get that:

| | Run whole app elevated | **Pre-installed SYSTEM scheduled task (chosen)** |
|---|---|---|
| What holds admin | the entire app | one narrow, fixed task |
| UAC at runtime | prompt every launch | none |
| Auto-start quietly | ✗ Windows won't silently auto-launch elevated apps | ✓ app is non-admin, starts normally |
| Works for standard-user employees | ✗ | ✓ |
| Security blast radius | large (keystroke-reading, net-connected app is admin) | small |
| Admin needed | every run | **once, at install** |

Because employee account type **varies / is unknown**, we design for the strict case (standard users);
the scheduled-task approach is the only one that works on **every** machine regardless of account type.

### Mechanism
- **Install-time (admin, once per PC):** register two tasks, runnable by SYSTEM, triggerable by normal users.
- **Runtime (non-admin app):** `schtasks /run /tn "EnableInternetWindow"`.

**`EnableInternetWindow`** (SYSTEM) does two things atomically:
1. Enable connectivity, e.g.:
   - Wired: `netsh interface set interface name="Ethernet" admin=enabled`
   - Wi-Fi:  `netsh interface set interface name="Wi-Fi" admin=enabled` (+ `netsh wlan connect name="<SSID>"` if needed)
2. **Arm the failsafe** — schedule a one-shot disable ~15 min out so internet can't linger if the app crashes:
   `schtasks /create /tn "DisableInternet_OneShot" /tr "<disable command>" /sc once /st <now+15m> /ru SYSTEM /rl HIGHEST /f`

**`DisableInternet`** (SYSTEM): `netsh interface set interface name="Ethernet" admin=disabled`
(and Wi-Fi equivalent). The app calls this early once the flush is confirmed; the one-shot is the backstop.

### App-side logout flow
1. User clicks **Log off & sync**.
2. App triggers `EnableInternetWindow`.
3. App waits for connectivity (`NetworkChange.NetworkAvailabilityChanged` + short poll), shows a
   progress screen ("Sending your time — please keep the PC on").
4. App flushes the SQLite queue (batched, with retries/backoff).
5. On success → trigger `DisableInternet` early; else let the +15-min one-shot handle it.
6. Anything still unsent stays queued for the next window. **No data loss.**

> Trade-off logged: whole-machine internet means the employee *can* browse during the window. Accepted
> for now. Per-app firewall isolation (default-deny outbound + allow only our exe) remains a future
> option if the data-protection team requires it.

---

## 4. Local storage — SQLite outbox [locked]

Replaces the legacy CSV / JSON-lines outbox (which had no idempotency key, all-or-nothing batching,
and no per-record retry state — too fragile for multi-day offline buffering).

### `pending_uploads` table

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT (GUID) PK | **Client-generated idempotency key.** Stable across retries. |
| `user_id` / `email` | TEXT | Who the interval belongs to. |
| `window_start_utc` | TEXT (ISO-8601) | Interval start. |
| `window_end_utc` | TEXT (ISO-8601) | Interval end. |
| `active_seconds` | INTEGER | Active seconds in the window. |
| `created_utc` | TEXT | When the client recorded it. |
| `status` | TEXT | `pending` \| `sending` \| `sent`. |
| `attempt_count` | INTEGER | For backoff / poison detection. |
| `last_attempt_utc` | TEXT | Last send attempt. |
| `last_error` | TEXT | Diagnostics for stuck rows. |

Benefits: ACID + crash-safe (WAL mode), precise batching, per-record retry/backoff, partial-success
handling, server-side dedupe via the stable `id`, and real observability ("what's pending and why").

Implementation note: `Microsoft.Data.Sqlite` (works on modern .NET; pick the client framework in Phase 0).

Activity-event logging (the legacy `activity_logs.csv`) can stay as a local rolling file **if** it's
purely diagnostic; if it ever feeds the API, fold it into SQLite too.

---

## 5. Cloud API contract (we own it) [locked direction, details TBD]

### `POST /auth/login`
- Request: `{ "email": "...", "password": "..." }` (or device credential — see Open questions).
- Response: `{ "token": "...", "expiresAt": "...", "serverTimeUtc": "..." }`
- `serverTimeUtc` lets the client measure **clock skew** while it's online (relevant: this is time
  tracking, so a manipulated client clock is a threat). Re-auth each window is fine — connections are rare.

### `POST /timeTracking/intervals` (authenticated)
- Request: a **batch** —
  ```json
  [
    { "id": "<guid>", "userId": "...", "windowStartUtc": "...", "windowEndUtc": "...",
      "activeSeconds": 240, "clientSentUtc": "..." }
  ]
  ```
- Server: **dedupes on `id`** (idempotent upsert), aggregates server-side, records server-receipt time.
- Response: the list of **accepted `id`s**, so the client marks exactly those as `sent` →
  enables partial success without double-counting.

### Security
- TLS, token auth, payload validation, rate limiting (public endpoint now hit by many PCs directly).
- Idempotency is mandatory: long offline buffering + retries make duplicate submissions a *when*, not an *if*.

---

## 6. Auth & security on the client [locked]

- **Offline login** for the UI: keep cached credential verification (BCrypt) so users can log in with
  no internet.
- **API token** obtained during a connected window; stored with **Windows DPAPI** (per-user encryption).
- **Clock-skew defense:** capture `serverTimeUtc` on connect; flag/correct large drift.

---

## 7. Connectivity & sync engine [locked]

- Network awareness via `NetworkChange.NetworkAvailabilityChanged` + lightweight reachability check.
- **Opportunistic flush:** attempt a sync at app start, on any network-available event, and on the
  logout window — not only at logout.
- **Exponential backoff** with a cap; **poison handling** for rows that keep failing (don't block the queue).
- **Batched** uploads with partial-success handling driven by the API's accepted-id response.

---

## 8. Phased execution plan

| Phase | Goal | Notes |
|---|---|---|
| **0. Foundations** | Pick client framework (.NET 8 vs Framework 4.8), solution layout, CI. | Resolve Open questions first. |
| **1. API redesign** | Token auth + idempotent batch ingest (dedupe on id, return accepted ids, server time). | We own the API → do first; unblocks the client. |
| **2. SQLite outbox** | `pending_uploads` schema + repository; migrate any legacy `outbox.log`. | |
| **3. Sync engine** | Connectivity awareness, batching, backoff, partial-success. | |
| **4. Client auth** | API token acquisition in-window, DPAPI storage, offline BCrypt login. | |
| **5. Internet window** | Two scheduled tasks + installer step; logout flow + progress UI; auto-disable failsafe. | |
| **6. Hardening** | Clock-skew capture/correction; retry caps / poison handling. | |
| **7. Autostart + packaging** | Logon autostart (Startup/Run key); installer that also registers the tasks. | |
| **8. Decommission legacy** | Remove Server PC/VM; drop `ServerIP` config; staged rollout, verify on one PC first. | |

---

## 9. Open questions (resolve before the relevant phase)

- **Client framework:** .NET 8 (modern, recommended) vs staying on .NET Framework 4.8 for parity with
  legacy. Affects Phase 0/2 library choices.
- **Identity model at the API:** per-**user** login token, per-**device** key, or both? Affects `/auth`.
- **Adapter naming:** real interface names per PC ("Ethernet" / "Wi-Fi" / vendor names) for the toggle
  commands; the install script should detect rather than hardcode.
- **Installer technology:** MSI / WiX / Inno Setup / script — must run as admin once to register tasks
  and autostart.
- **Activity-event log:** keep as local diagnostic file, or also ingest into the API?

---

## 10. Carry-over facts from the legacy system (for reference)

- Interval identity was only `(email, windowStartUtc, windowEndUtc)` — **no id** (the gap we're fixing).
- Legacy cloud endpoint: `https://backend.software.equicom.co/api/v1/timeTracking/add`, payload
  `{ employeeEmail, seconds }` (aggregated by the middleman). New design sends raw intervals with ids
  and aggregates server-side.
- Legacy logout already attempted a 2-second final sync + a shutdown snapshot — good instinct, but too
  short and with no connectivity handling. The revamp generalizes this into the window + durable queue.
