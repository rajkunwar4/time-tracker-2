# Planning — Lifecycle, Always-On, and the Internet Window

A living planning doc. **Phases 1 & 2 are now built** (window lifecycle/tray + auto-start);
the internet window and offline login are still planned. We refine *this* instead of
re-deriving it each time.

Status tags: ✅ **Decided** · 🔶 **Proposed (confirm)** · ❓ **Open (needs your / infra input)** · 🛠️ **Built**

**Implemented so far:** 🛠️ §1 window chrome + lifecycle + tray, 🛠️ §2 auto-start at sign-in.
Remaining: §3 internet window, §4 offline login.

---

## 1. Window chrome, resize & app lifecycle — 🛠️ BUILT

Built in [`AppForm`](../src/TimeTrack.App/Forms/AppForm.cs) (standard resizable title bar, centered
content, min-size, DPI-correct), [`TrackerAppContext`](../src/TimeTrack.App/TrackerAppContext.cs)
(tray + login⇆main loop), and the forms. Quit policy landed as **no Exit in the tray** (Open / Sync
now only). Watchdog deferred. (Original state: borderless, fixed-size, logout exited the process.)

- ✅ **Standard Windows title bar** — minimize / maximize / close — and the window is **resizable**.
- ✅ **Logout → return to the Login screen** and keep running (no longer exits the process).
- ✅ **Close button (X) → minimize to a system-tray icon** (does not exit).
- 🔶 **Resizable layout guardrails:** set a sensible **minimum window size**, and keep content in a
  **centered max-width column** so a maximized window doesn't sprawl into whitespace. (The new
  `TableLayoutPanel` layout already reflows.)
- 🔶 **Quit policy:** close goes to tray; **fully quitting requires a password** (workforce tracker
  should stay running). Confirm — or allow free quit from the tray.
- 🔶 **Tray UX:** icon + tooltip showing today's tracked time and Working/On-break state; menu =
  Open, Sync now, status. Confirm contents.
- ⚠️ **Trade-off noted:** a standard title bar means **dropping the borderless rounded chrome** the
  spec PDF specified. Intended.

**Implications for code:** `AppForm` flips to `FormBorderStyle.Sizable`; drag-to-move + custom
border become redundant; `Program.Main` gains a **login ⇆ main loop** instead of a single
`Application.Run`; add a `NotifyIcon` + minimize-to-tray; tracking pauses while at the login screen.

---

## 2. Always-on / auto-start — 🛠️ BUILT

- 🛠️ **Auto-launch per-user at Windows sign-in, non-admin** — [`AutoStart`](../src/TimeTrack.App/Services/AutoStart.cs)
  writes an `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entry on startup, config-toggleable
  via `appsettings.json` → `Startup.RunAtLogon` (ships `true`). Debug builds skip it (and clear any
  stale key) so dev builds don't auto-launch each logon. Runs in the user's interactive session
  (correct for a UI tracker); a boot-time SYSTEM service was rejected as it has no desktop.
- ❓ **Watchdog:** should it **relaunch if the employee force-kills it** mid-day, or is "starts again
  next sign-in" enough? (A watchdog adds a second process / scheduled task.)
- 🔶 **One employee per PC** (single Windows user) assumed.

---

## 3. Connectivity-aware sync + the internet window

This is the area that changed most from the original blueprint and has open infra dependencies.

### Mechanism (confirmed — from a client PC's Proxy settings)
- ✅ **The lever is the Windows system proxy** ("Manual proxy setup → Use a proxy server"), pointing
  at the **master PC at `192.168.137.1:808`** — the **same on every client**. `192.168.137.1` is the
  Windows **ICS / Mobile-Hotspot host IP**, so the master shares its internet and runs a proxy on
  port **808**; clients reach the internet only through it.
- ✅ **"Turn internet on/off" = toggle that proxy On/Off** — what employees do by hand today. The app
  automates exactly that. It's a **local, per-user (`HKCU`), non-admin** change, so there is **no
  chicken-and-egg and no remote control API**: the lever is always available locally; the master just
  has to be sharing internet upstream.
- ✅ **Whole-PC, time-boxed.** Proxy On = the whole PC (incl. browser) has internet — keep the window
  short and auto-close.
- ✅ **Client = static LAN IP**, **no proxy auth**.

### Default approach — abstract the toggle behind an interface
`IInternetWindow` (`OpenAsync()` / `CloseAsync()`) keeps the rest of the app agnostic to the lever.
Concrete implementation:

- **Open:** in `HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings`, set
  `ProxyServer = "192.168.137.1:808"` and `ProxyEnable = 1`, then notify WinINET
  (`InternetSetOption` → `SETTINGS_CHANGED` + `REFRESH`) so it applies without a restart.
- **Close:** set `ProxyEnable = 0` and notify WinINET.
- **App's own sync calls:** point `TimeTrackApiClient`'s `HttpClient` at the same proxy **explicitly**
  (`WebProxy("192.168.137.1:808")`) — don't rely on `HttpClient.DefaultProxy`, which .NET caches at
  first use and won't notice a mid-run toggle.
- **Failsafe:** clear `ProxyEnable` on app **exit** and on a **timer** (`MaxWindowMinutes`), so a
  crash can't leave the whole PC's internet open.
- **Config:** `Proxy.Address = "192.168.137.1"`, `Proxy.Port = 808`, `Proxy.MaxWindowMinutes` in
  `appsettings.json` (defaults, since it's the same everywhere).

### The core constraint — two channels (chicken-and-egg)
To *get* internet the app must *call the controller to open the window* — so the controller must be
reachable **even while general internet is blocked**, i.e. on the **LAN**.

```
CONTROL channel (always reachable, LAN-local):
    app → "open internet for this PC, N min" → proxy/firewall controller
                         ↓ opens the gate
DATA channel (gated = the actual internet):
    app → cloud API: login / sync intervals
app → "close now" (explicit, right after sync)
controller auto-closes after N min regardless (failsafe)
```

### Recommendations (fairly confident)
- 🔶 **Failsafe (client-side, given the default impl):** clear the proxy **on app exit** and on a
  **timer**, so a crash can't leave the whole PC's internet open. Optional master-side time-boxed
  allow as defense-in-depth.
- 🔶 **Open windows only for *sync*, not for login** — by adding **offline login** (next section),
  the morning sign-in needs no internet at all; windows open only when there's queued data to flush.
- ⚠️ **Security caveat of the client-side default:** if turning internet on is just a client proxy
  toggle, a savvy employee could set the same proxy to browse freely. If that matters, the master
  must enforce per-IP / time-boxed allow (and ideally an audit log) — revisit if the policy is strict.

### Still to confirm (not blocking)
1. **The master PC is sharing internet upstream** whenever the app opens a window (assumed always-on;
   if the master itself is sometimes off, the app's "open" simply yields no connectivity and the
   outbox stays queued — safe).
2. **"Automatically detect settings" (WPAD) is also On** in the client settings. We only write the
   manual-proxy keys (which take precedence); verify WPAD doesn't interfere on the real machines.

*(Resolved: the lever is the local Windows manual-proxy toggle at `192.168.137.1:808`, non-admin —
no remote control API, no LAN control channel, no chicken-and-egg. Client = static LAN IP; no auth.)*

### ⚠️ Design-tension note
The revamp's headline goal was "remove the middleman" (old Server-PC/VM relay). A LAN-local
controller reintroduces a small on-prem component — but a **very thin one**: it only flips a firewall
rule and **never relays time data** (that still goes client → cloud directly). We're knowingly
accepting that.

---

## 4. Supporting decisions (proposed defaults — confirm)

- 🔶 **Offline login.** First login online; afterward sign-in works with internet off by validating
  against a **DPAPI-protected verifier** (salted hash, never the raw password). Required because
  Wi-Fi/internet is normally off at the start of the day. Needs **token-refresh-when-online** since
  the server JWT expires in 7 days and offline stretches may outlast it.
- 🔶 **Sync timing.** Auto-sync when a window is open + a manual **"Sync now"** (tray) + flush at
  logout.
- 🔶 **Offline logout.** Allow logout immediately; data stays in the durable outbox and syncs in the
  next window (no friction, no double-count — the existing GUID idempotency guarantees this).
- 🔶 **Connectivity detection.** After opening a window, confirm the **cloud API health endpoint** is
  reachable before syncing (don't trust "Wi-Fi up" alone).

---

## 5. Build sequence

1. 🛠️ **Lifecycle + chrome** — title bar, resize/min-size, logout→login loop, tray + minimize-to-tray. **Done.**
2. 🛠️ **Auto-start** — `HKCU\…\Run` entry on startup (Debug-gated). **Done.**
3. **Offline login** — cached verifier + token refresh. Security-sensitive; isolate and test hard. ← next
4. **Internet window** — implement the **`IInternetWindow`** abstraction + default proxy-toggle impl
   + config (`Proxy.MasterIp`/`Port`/`MaxWindowMinutes`), wired into the sync flow: open → confirm
   cloud reachable → sync → close (with the exit/timer failsafe). The gateway is treated as working.
   Only loose end: confirm LAN-reachability while offline.
