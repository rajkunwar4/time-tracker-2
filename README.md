# Time Tracking — Revamp

A ground-up redesign of the Equicom employee time-tracking system. This repo is **separate from
the legacy codebase** (`Equicom-TimeTracking`) and is where all new code lives.

## Why a rewrite

The legacy system routed data **child PC → on-prem Server PC (VM on old Windows) → cloud API**. The
Server PC / VM hop was a bottleneck and a constant pain point. The revamp **removes the middleman**
and has each client PC talk **directly** to the cloud API.

The other big change is the connectivity model. Client PCs are **not allowed continuous internet**
(a data-protection policy limits them to short windows). So the app is designed for an
**occasionally-connected, store-and-forward** world: it records time locally at all times and syncs
in short bursts when internet is made available (e.g. at end-of-day logout).

## Architecture at a glance

```
┌─ EMPLOYEE PC (non-admin app, auto-starts at logon) ─────────────┐
│  • login (offline-capable)                                       │
│  • monitor active vs idle time                                   │
│  • write interval records to a local SQLite outbox               │
│  • on "log off & sync": open a timed internet window, flush      │
│        ↓ triggers (no UAC)                                        │
│  • pre-installed SYSTEM scheduled task toggles whole-machine      │
│    internet ON, auto-disables after ~15 min (failsafe)           │
└───────────────────────────────────┬─────────────────────────────┘
                                     │ HTTPS (token auth, idempotent batch)
                                     ▼
                         ┌──────────────────────────┐
                         │ Cloud API (we own it)    │
                         │ • token auth             │
                         │ • idempotent ingest      │
                         │ • dedupe on client ID    │
                         │ • aggregate server-side  │
                         └──────────────────────────┘
```

The **durable SQLite queue + per-record idempotency IDs** are the real delivery guarantee — an
interrupted sync simply retries in the next window, with no double-counting.

## Key design decisions (locked in)

- **Direct client → API**; legacy Server PC / VM removed.
- **Client app only — no Windows Service.** The app runs **non-admin** and **auto-starts** quietly.
- **Whole-machine internet, time-boxed (~15 min)**, toggled by a **pre-installed SYSTEM scheduled
  task** (admin used once at install, never at runtime), with an **auto-disable failsafe**.
- **SQLite** local outbox with **client-generated idempotency IDs** (replaces the legacy
  CSV / JSON-lines outbox).
- **Redesigned API**: token auth + idempotent batch ingest that dedupes on client ID.
- **DPAPI** for token storage; **offline login** retained; **server-time capture** for clock-skew defense.

See **[docs/REDESIGN.md](docs/REDESIGN.md)** for the full blueprint: architecture, the internet-window
mechanism (with concrete scheduled-task definitions), the SQLite schema, the API contract, and the
phased execution plan.

## Status

Early planning / scaffolding. No application code yet — the design blueprint is in `docs/`.

## Relationship to the legacy repo

The old code remains in `Equicom-TimeTracking` for reference (see its `DEVELOPMENT.md` /
`DEPLOYMENT.md`). Nothing here depends on it; this is a clean start.
