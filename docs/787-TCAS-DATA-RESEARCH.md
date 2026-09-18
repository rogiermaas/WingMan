# Reading traffic from the stock 787

17 September 2026. Loaded WT 787-10; MSFS executable 1.8.16.0. Read-only investigation: no camera, autopilot, traffic-setting or simulator-restart commands.

## Outcome

The 787's actual avionics traffic store is accessible from its loaded MFD JavaScript context. We now have a diagnostic reader for contact IDs, names, GPS positions, altitude, estimated motion, sample time, and TCAS relative positions. No target-side companion is needed. This is an aircraft-specific internal interface, not a stable public SimConnect contract.

It did **not** recover missing players during this test. A minute-long comparison recorded 57 snapshots: the internal contact store, TCAS intruder list and all three raw traffic query routes contained zero contacts. The user independently confirmed that no traffic diamonds were visible at this time. We therefore have no demonstrated case yet where the 787 has a fresh contact that the existing app cannot acquire.

The blank tablet is a related observation, not evidence that opening or repairing the tablet will restore the traffic feed.

## Data path verified in the loaded source

The captured installed `787-mfd.js` contains a TrafficInstrument which obtains traffic using `JS_LISTENER_AIR_TRAFFIC` / `GET_AIR_TRAFFIC`. Its primary instance maintains shared contact data; the MFD's instance is a replica with sync ID `b78x`. The TCAS system derives predictions and relative vectors from those contacts. The installed EFB `atlasapp.js` also calls `GET_AIR_TRAFFIC`.

Working Title independently confirms that its TCAS implementation uses this call and derives motion from successive position samples. [Developer explanation](https://devsupport.flightsimulator.com/t/js-npcplane-parameter-name-always-empty-msfs2020-2024/13002). Microsoft's public implementations provide useful corroboration: [Traffic.ts](https://github.com/microsoft/msfs-avionics-mirror/blob/main/src/sdk/instruments/Traffic.ts), [TCAS.ts](https://github.com/microsoft/msfs-avionics-mirror/blob/main/src/sdk/traffic/TCAS.ts). The field and unit details below were also checked against the locally captured, loaded 787 source.

```text
MSFS GET_AIR_TRAFFIC
  +-- Escort Plane's current bridge
  +-- EFB Atlas traffic layer
  +-- 787 primary TrafficInstrument -> shared contacts -> MFD replica -> TCAS predictions

Visible multiplayer nameplates use a different path; names alone do not establish a GPS fix.
```

Access in this installation:

```js
const instrument = document.querySelector('wtb78x-mfd').fsInstrument;
const shared = instrument.trafficInstrument.sharedGlobalData;
const contacts = Array.from(shared.trafficContactData.values());
const intruders = instrument.tcas.getIntruders();
```

The global shared object was also found as `window['__msfssdk-trafficInstrumentSync-b78x']`. Its `updateId` continued increasing while the contact map was empty: an instrument heartbeat is not proof of target updates.

| Read location | Values | Interpretation |
| --- | --- | --- |
| Shared contact | `uid`, `name`, `lat`, `lon`, `altitude`, `heading` | Last reported contact, latitude/longitude in degrees, altitude in **feet**. Raw GET_AIR_TRAFFIC altitude instead uses meters. |
| Shared contact motion | `groundSpeed`, `groundTrack`, `verticalSpeed` | Estimated knots, true degrees and feet/minute; may be invalid before enough samples. |
| Shared contact timestamp | `lastContactTime` | Simulation clock milliseconds; compare with TCAS `simTime`, not wall-clock `Date.now()`. |
| TCAS intruder | `position`, `altitude`, `relativePositionVec` | Predicted position, not a new underlying observation. Relative vector is east/north/up in meters. Intruder altitude is feet. |
| TCAS intruder velocity | `relativeVelocityVec` | East/north/up meters/second. |
| TCAS validity | `isPredictionValid` and original contact time | Both matter; an advancing prediction must not renew measurement freshness. |

For a valid relative vector `[east, north, up]`, horizontal range is `hypot(east,north)/1852` NM, true bearing is `atan2(east,north)` normalized to 0–360 degrees, and relative height is `up/0.3048` feet. The reader additionally reports bearing relative to own **ground track**, explicitly distinct from nose-relative bearing. These are derived display quantities; they do not add an independent position source.

In this flight the simulation clock was approximately 82 seconds behind wall time. Copying its contact timestamp into the app's existing wall-clock freshness checks would incorrectly mark contacts stale. Conversely, substituting the current receive time on each cache read would make old contacts appear fresh. Production integration needs explicit clock conversion, duplicate handling, sim pause/reset handling and contact expiry.

## Live experiments

| Experiment | Measured result |
| --- | --- |
| Direct 787 store and TCAS access | Successful; replica ready, increasing update ID, valid moving own GPS, zero contacts and intruders. Actual avionics mode was TA/RA. |
| Parallel source comparison, approximately 18:13:43–18:14:43 UTC | 57 snapshots, with zero contacts in the 787 store and intruder list; global, AIR_TRAFFIC and MAPS GET_AIR_TRAFFIC calls returned empty arrays. |
| GET_TCAS_PLANES and GET_FLARM_PLANES | Each called **once per scoped listener** at the start of that comparison. All four succeeded with empty arrays. Their saved results are repeated in the snapshot log; these are not 57 independent alternative-call tests. |
| Native TCAS/FLARM SimVars | 195 independent readings across 13 fields over 15 seconds, no SimConnect exceptions. Intruder bearing/distance/height/vertical speed and network ID returned maximum-double placeholders, not usable data. FLARM unavailable. Own position remained valid. |
| Generic versus actual TCAS mode | Native TCAS MODE returned 1, while the actual 787 JavaScript system reported TA/RA. Generic mode cannot be used to conclude that the 787 TCAS is switched off. |
| Updated reader verification | Offline cardinal-direction, unit, simulation-age, missing-instrument and invalid-prediction checks passed. A final live snapshot at about 18:18:45 UTC again read the ready store successfully, still with zero contacts. |

The successful alternative calls correct the earlier initialization-time `NoSuchMethod` results. The [MAPS listener documentation](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/JavaScript/Coherent_Listeners/JS_LISTENER_MAPS.htm) lists these methods but provides insufficient schema detail to claim they enumerate all multiplayer aircraft. The native composite `TCAS INTRUDER DATA` structure was researched but not decoded in this experiment; no positive result is claimed for it. [Native variable documentation](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimVars/Aircraft_SimVars/Aircraft_RadioNavigation_Variables.htm).

## Why the missing tablet traffic matters

Asobo acknowledged an August 2026 report of unstable or empty GET_AIR_TRAFFIC results and linked the symptom to reports about the EFB. Its testing observed more instability in busy areas with live traffic and multiplayer together. A September 15 follow-up still requests a fix; this exchange does not establish a released fix. This closely matches our symptoms but does not prove the exact cause in this flight. [Asobo discussion](https://devsupport.flightsimulator.com/t/coherent-get-air-traffic-call-returns-inconsistent-data/18356).

Separately, Asobo confirmed in July that remote PLANE LATITUDE/LONGITUDE are not replicated multiplayer SimVars, with expansion tracked but no ETA. This explains why a native ID can exist without coordinates. [Asobo explanation](https://devsupport.flightsimulator.com/t/no-multiplayer-simobject-data-through-simconnect-requestdataonsimobjecttype/17869).

These are two different limitations: incomplete native-variable coverage, and intermittent availability through the otherwise useful traffic feed. Neither is repaired by changing the app's list filters or searching gamertags with different capitalization.

## Reproduce and next validation

Tools:

- `tools/coherent-787-tcas-read.js`: read-only snapshot with explicit units, simulation-clock age, prediction validity, relative range/bearing/height.
- `tools/coherent-787-tcas-compare.py`: bounded comparison recorder; default 40 seconds, maximum 120.
- `tools/simconnect-tcas-read.py`: independent native relative-variable probe.
- `tools/verify-tcas-reader.cjs`: offline diagnostic-reader checks (`node tools/verify-tcas-reader.cjs`).

Use `python tools/research-acquisition.py pages` to identify a loaded, spare 787 MFD view; view 21 was used here. Do not attach to the app's active bridge/nameplate views. Then run `python tools/coherent-787-tcas-compare.py 21 60`. This records observations locally and does not command the aircraft. View numbers may change when the flight changes.

Evidence:

- `artifacts/acquisition-research/tcas-comparison-20260917-181342.jsonl`
- `artifacts/acquisition-research/tcas-native-20260917-180811.json`
- `artifacts/coherent/*-coherent-787-tcas-read.json`
- Loaded source captures: `artifacts/acquisition-research/787-mfd.js`, `atlasapp.js`, `simvar-current.js`.

The next decisive comparison is a period with visible TCAS diamonds: match their IDs and sample ages against raw traffic and the app's list. Fresh shared contacts missing from the app would justify an aircraft-specific supplemental reader. Predicted or retained contacts would justify display continuity only, not inventing fresh guidance inputs. If the shared store is also empty, this route cannot recover positions that the simulator has withheld.

No production acquisition fallback was enabled and no executable rebuild was needed for this research. Reliable following of every visible native multiplayer aircraft remains unresolved.
