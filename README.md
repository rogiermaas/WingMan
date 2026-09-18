# WingMan

**Fly together. Lead, follow, or both.**

WingMan is a Windows 10/11 x64 formation-flying companion for Microsoft Flight Simulator 2024, with a lightweight Linux relay. Pilots running WingMan share their simulated aircraft telemetry, so formation following does not depend on MSFS exposing other multiplayer aircraft through its traffic APIs.

[Download the Windows installer](https://wingman.rogiermaas.nl/WingMan.msi) · [Releases](https://github.com/rogiermaas/WingMan/releases) · [Pilot guide](docs/WINGMAN-USAGE.md) · [Website](https://wingman.rogiermaas.nl/)

## What it does

- Share your aircraft, follow another pilot, or build a formation/5 NM air train.
- Adjust distance behind, lateral spacing and height in 0.1 NM steps while following.
- Update selected speed, heading and altitude through standard SimConnect or the PMDG 737 SDK; optional automatic V/S matching.
- Circle a grounded WingMan lead with a positive height offset, then resume formation guidance after takeoff.
- Discover WingMan aircraft in pale green and optional local MSFS contacts in light grey. Data status distinguishes live positions from labels without coordinates.
- Filter position jumps and frozen samples; optionally reacquire after telemetry loss.
- Keep a compact window on top, and check for updates every five minutes.

Telemetry runs at up to 10 Hz, guidance at up to 5 Hz, and lists refresh once per second without rebuilding existing rows. The default relay is hosted at `wingman.rogiermaas.nl`; an Apache HTTPS proxy allows the relay to use an existing web port.

## Install and fly

Install the MSI, load a flight, enter your pilot name and click **OK**. Simulator and relay connections are automatic. Enable **Followable** to let others select you. Select a **Live** aircraft, choose offsets and outputs, then enable **Follow user**. Your aircraft must be airborne at least 500 ft above ground. You retain control of AP master, autothrottle and aircraft modes; see the pilot guide for automatic V/S prerequisites.

The application locates a compatible Microsoft SimConnect runtime or downloads the unmodified SDK 1.7.3 DLL from the WingMan website and verifies its pinned hash. The DLL is not in this repository or MSI and retains Microsoft's SDK license.

This is experimental simulator software, not software for real aircraft. Aircraft-specific autopilot compatibility and performance vary. There is no automatic takeoff, landing or terrain avoidance. A positive height offset is relative to the lead, not a guarantee of terrain clearance. Native MSFS discovery remains limited by the data the simulator exposes. MSFS 2020 compatibility has not been established.

## Build and test

Required: Windows, .NET 10 SDK, Go 1.23 or newer, Python 3, and WiX 4.0.6. MSFS and its SDK are not required for the offline tests or CI build.

```powershell
dotnet tool install wix --version 4.0.6 --tool-path artifacts/toolchain/wix
& ./artifacts/toolchain/wix/wix.exe extension add WixToolset.UI.wixext/4.0.6
./build-wingman.ps1 -SkipPublish

$env:WINGMAN_TEST_PROFILE = 'local-self-test'
$testRun = Start-Process -FilePath ./dist/WingMan/WingMan.exe -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
if ($testRun.ExitCode -ne 0) { throw 'WingMan self-tests failed' }
```

Outputs: `dist/WingMan/WingMan.exe`, `dist/WingMan.msi`, and `dist/WingMan-server-almalinux8-x64.tar.gz`. The self-contained executable needs no separate .NET installation. `-SkipPublish` does not require the production update-signing key. Forks must configure their own service and update trust key before distributing automatic updates.

[GitHub Actions](https://github.com/rogiermaas/WingMan/actions) builds the Windows application/installer and Linux relay, runs the offline client and relay tests, and retains build artifacts. Production website releases are separate from CI artifacts. Version 1.1.6 passed 249 client checks. See [release notes](docs/WINGMAN-1.1.6.md).

## Project and data

Client: C# WinForms in `src/EscortPlane2024` (the original internal namespace remains). Relay: Go in `relay`. Installer: WiX in `installer`. Operational examples: `deploy`. [Deployment guide](docs/WINGMAN-DEPLOYMENT.md).

Settings and client identity live in `HKCU\Software\WingMan`; diagnostics and update/runtime files use local storage. The relay receives simulated positions for nearby discovery even when Followable is off; those observer positions are not forwarded to other pilots. The relay keeps telemetry in memory, while ordinary hosting access logs are separate. The application does not automatically upload its local diagnostic logs. See the pilot guide before sharing telemetry or diagnostic files.

## Code signing policy

Current EXE/MSI releases are **not Windows Authenticode-signed**. The automatic updater verifies a project-signed manifest and archive hash; that is separate from Windows trust. Free open-source signing through SignPath Foundation is being investigated and has not been approved. See [code-signing status and setup](docs/CODE-SIGNING.md).

## License

GNU GPL version 3; see [LICENSE.txt](LICENSE.txt). Third-party software retains its own licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Microsoft SimConnect is a separate proprietary runtime. WingMan is an independent project, not an official Microsoft, Asobo or PMDG product.
