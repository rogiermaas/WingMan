# WingMan 1.1.0 validation

Tested locally on Windows, 18 September 2026:

- Windows self-contained x64 build: zero compiler warnings/errors.
- 157 C# checks pass, including existing guidance/PMDG/stock adapter checks and new network identity namespaces, unit conversion, fresh/stale/paused data, 10 Hz motion estimation, teleport rejection, update signature tampering, wrong publishers and archive traversal.
- Six Go relay integration tests: public discovery within 100 NM, observer privacy, selected-follow delivery at 10 Hz, reconnection to idle public discovery, three-pilot chain, circular follow rejection, room isolation, duplicate/stale telemetry rejection, reconnect sequence reset, identity-secret authentication, offline lead rejection, clock skew, signed release publication and concurrent telemetry/download.
- Real .NET WebSocket clients through the Go relay: 20 fresh samples accepted, maximum measured local sample age about 2.1 ms. Local RTT and delivery are not internet latency estimates.
- Real updater process: signed 1.1.0 → 1.1.1 download, original process close, executable replacement, new process readiness acknowledgment and rollback copy verified. Saved pilot settings survived. A deliberately stale "was following" intent supplied at download start was replaced by the real inactive state at close, so stopping during a download does not cause unexpected re-engagement.
- Redesigned normal, expanded settings and compact WinForms screenshots inspected: consistent red/green switches, automatic connection controls, full banner and bottom connection/delay/version bar. Generated ICO has nine frames: 16, 20, 24, 32, 40, 48, 64, 128 and 256 px, embedded in the EXE and used by MSI shortcuts.
- Per-user MSI install/uninstall smoke test passes, with HKCU installation values and Start menu/desktop shortcuts. An initial build-number check incorrectly used Windows Installer's compatibility value; the installer now reads the actual OS build from the registry.
- Linux relay inspected as amd64 ELF with no dynamic interpreter (CGO disabled). It has not been executed on the target AlmaLinux host yet.

Not established by these checks: performance on the actual HTTPS server, live formation tracking with two simulators, every add-on's autopilot selector behaviour, Windows 10 runtime behaviour, or a live MSFS toolbar panel. The toolbar panel is a possible lightweight future interface; it is not included in this MSI. No new autopilot commands were sent to the user's simulator during this validation.
