# TimeTrack — Documentation

This folder is the documentation hub for the **Time Tracking — Revamp** project: a
.NET 9 WinForms desktop client that records employee work time locally and syncs it to
the Equicom cloud API in short, occasionally-connected bursts.

## Start here

| Doc | What it covers |
|-----|----------------|
| **[SETUP.md](SETUP.md)** | Set up a dev machine from scratch: SDKs, IDE, backend, build/run/test. |
| **[HOW_IT_WORKS.md](HOW_IT_WORKS.md)** | The current core logic — how tracking, the durable outbox, idempotent sync, auth, and the backend ingest actually work in the code today. |
| **[PLANNING.md](PLANNING.md)** | Living plan for not-yet-built work: window lifecycle/tray, auto-start, offline login, and the programmatic internet-window (proxy/firewall allow) mechanism — decisions, open questions, and build sequence. |
| **[REDESIGN.md](REDESIGN.md)** | The original design blueprint / vision: full architecture, the time-boxed internet-window mechanism, the phased execution plan. (Some parts are still planned — see HOW_IT_WORKS for what's built.) |

## The one-paragraph version

An employee runs a small, non-admin WinForms app that **auto-tracks active vs. idle
time** second-by-second and batches it into fixed-length intervals. Each interval gets a
**client-generated GUID** and is written to a **durable local SQLite outbox**. Because
client PCs only get internet in short windows, the app **stores-and-forwards**: it keeps
recording offline and **flushes the queue to the cloud API on logout**. The server ingest
is **idempotent** (deduped on the interval GUID), so a retried batch after a long offline
stretch can never double-count time.

## Repository layout

```
time-tracking-revamp/
├─ src/
│  ├─ TimeTrack.Core/      # platform-agnostic logic: tracking, outbox, sync, API client
│  └─ TimeTrack.App/       # WinForms UI (login + home screen), Windows-specific services
├─ tests/
│  └─ TimeTrack.Core.Tests/  # xUnit unit tests for the Core library
├─ tools/
│  └─ TimeTrack.SmokeTest/   # headless end-to-end check against a running backend
└─ docs/                   # you are here
```

The **cloud API** lives in a separate repo (`equicom-legacy/backend`, Node + Express +
MongoDB) — see [SETUP.md](SETUP.md#cloud-api-backend) for how to run it.
