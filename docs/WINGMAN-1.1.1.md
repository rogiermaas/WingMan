# WingMan 1.1.1

- Labels and inputs now share aligned rows. Followable and Follow user use the same label column.
- A small OK button commits the pilot name; Enter also works. Leaving the field no longer commits it.
- Switches paint their background explicitly and use a rounded keyboard focus outline instead of a rectangular box.
- Missing or incompatible SimConnect installations trigger an automatic download of the unchanged native DLL from SDK 1.7.3. The app verifies the exact 84,480-byte payload and SHA-256 before writing it.
- Installation uses `MSFS2024_SDK` when set, otherwise the system drive's `MSFS 2024 SDK/SimConnect SDK/lib` directory. Protected locations or existing incompatible files fall back to the same SDK layout under the user's WingMan application-data directory. Existing SDK DLLs are preserved. The resulting path is stored in HKCU.
- No administrator prompt is needed. A failed download retries after five minutes. Closing the app cancels a pending runtime download.
- Automatic application updates still contain only `WingMan.exe`; the runtime is a separate prerequisite download. The existing publisher signing key and update protocol are unchanged.

Validation: Release build has zero warnings/errors. All 159 offline checks pass. An integration test downloaded the runtime over production HTTPS, verified its hash, preserved a pre-existing incompatible file, installed to the fallback SDK layout and loaded its native exports. Normal, settings and compact layouts were rendered for inspection. These tests did not issue commands to a live aircraft.

Runtime mirror: `https://wingman.rogiermaas.nl/runtime/msfs2024-1.7.3/SimConnect.dll`.

SHA-256: `B10DE7ADF4C62E5F66C89DD6D01B64091BBBCEC83411CFE191D6B85FBEE61D15`.

The runtime is Microsoft's unmodified component under the SDK EULA, hosted alongside it. Mirroring proceeds under the project owner's reported confirmation from Asobo on 18 September 2026; this note does not assert independent written confirmation by WingMan's developer.
