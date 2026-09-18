# WingMan licensing

WingMan's project code is distributed under the **GNU General Public License, version 3** as requested by its owner. The complete, unmodified text was downloaded from https://www.gnu.org/licenses/gpl-3.0.txt on 18 September 2026 and is included in `LICENSE.txt`. The installer displays that text and requires acceptance before proceeding. See the license for redistribution conditions and warranty terms.

Third-party components retain their own licenses:

- .NET runtime / Windows Forms: Microsoft and contributors, MIT license. The self-contained Windows publish contains the .NET runtime. Official repository notices: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT and https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT .
- Gorilla WebSocket v1.5.3: Gorilla WebSocket authors, BSD 2-Clause. Its license is copied into the server bundle as `GORILLA-LICENSE.txt`.
- Go runtime: Go Authors, BSD 3-Clause. Its license is copied into the server bundle as `GO-LICENSE.txt`.
- WiX Toolset 4.0.6 builds the MSI; the generated installer is separate from the WiX toolset's own licensing. WiX is not installed on the pilot's PC.
- Microsoft Flight Simulator / SimConnect SDK: Microsoft license terms. Its binaries are **not included** in the public WingMan MSI/source archive. If a compatible local runtime is missing, WingMan downloads the unmodified MSFS 2024 SDK 1.7.3 native DLL separately from its server. That DLL remains subject to Microsoft's SDK license, available beside the download as `MSFS SDK EULA.pdf`; it is not relicensed under the GPL.

The source archive includes build scripts, the publisher verification public key, UI artwork masters and artwork prompts. Publisher private keys, personal settings, telemetry logs, simulator content and SDK binaries are excluded.
