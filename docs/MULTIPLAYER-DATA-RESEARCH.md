# Multiplayer aircraft data: research and experiments

17 September 2026. Environment: MSFS 2024 retail executable 1.8.16.0; extracted SDK 1.7.3. This report concerns acquisition of target motion, not validation of autopilot formation performance.

## Finding

**18 September user observation:** AI/live traffic is already disabled, with approximately ten or fewer nearby multiplayer aircraft. Several targets remain visually present about 2 NM away and within 150 ft while both EFB and TCAS omit them; occasional map contacts still appear. The location chooser shows dozens of green multiplayer markers before flight. Do not treat congestion as an established explanation or repeatedly recommend disabling AI traffic. These observations support an inconsistency between preflight/visual presence and in-flight traffic exposure, but do not identify the internal cause or establish GSX involvement.

The public [world-map listener](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/JavaScript/Coherent_Listeners/JS_LISTENER_WORLDMAP.htm) and [community listener](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/JavaScript/Coherent_Listeners/JS_LISTENER_COMMUNITY.htm) documentation does not specify a continuous remote-player position query. Access to the actual chooser marker store, its update rate, altitude fields and availability during flight remains unverified. The local Coherent diagnostic endpoint refused connections during this follow-up, so no new live chooser inspection was possible. Do not present a separate menu feed or its in-flight accessibility as proven.

Additional approaches requiring no target-side installation remain experimental: inspect the chooser's actual marker source; derive bearings from calibrated screen images and combine them with nameplate range/altitude; or investigate internal rendered-object transforms. Screen tracking requires visible, correctly associated targets and a known camera pose/FOV; it cannot provide dependable off-screen tracking. No optical follower or internal-memory reader is implemented or validated. Existing ordinary traffic acquisition already requires no companion and has produced real target data, but remains intermittent. Switching map programs alone is not an established escape: [Little Navmap's FAQ](https://albar965.github.io/littlenavmap-faq.html) documents native multiplayer limitations. Alternative networks only cover participating pilots and do not reveal arbitrary native MSFS players.

**Latest 787 TCAS investigation:** direct access to the loaded 787's shared contact store and TCAS relative vectors works. During a 57-snapshot comparison, that store, its intruder list and three raw traffic routes were all empty; the user also reported no visible TCAS diamonds. Scoped GET_TCAS_PLANES/GET_FLARM_PLANES calls succeeded in the loaded flight but returned empty arrays, correcting the initialization-time errors below. The [detailed TCAS report](787-TCAS-DATA-RESEARCH.md) documents GPS/relative fields, units, simulation-clock freshness, tools and limitations. No production acquisition fix is claimed.

No verified method found in this investigation reliably exposes positions for **every native MSFS multiplayer aircraft visible in the scene**. There is a useful but intermittent Coherent traffic source. Native SimConnect works for own-aircraft and ordinary simulated objects, but an Object ID does not guarantee readable multiplayer coordinates.

**User requirement: the target pilot must not need a companion app or a separate multiplayer network.** Direct telemetry sharing and JoinFS therefore do not satisfy this project. Continue with local native/Coherent acquisition and investigate camera or UI-derived alternatives only as experiments. Following arbitrary players remains limited by the data MSFS exposes; there is no verified general fix yet.

**Stock 787 follow-up completed after the simulator restart:** Lasterminater's A319 supplied 284 position samples by 16:55:41 UTC, through the existing Coherent bridge, without software on the target pilot's PC. The feed disappeared and returned as separation changed, and also froze temporarily while still supplying responses. This establishes usable acquisition for this nearby player, not reliable coverage of every visible player. See the new [loaded-flight results](#loaded-stock-787-flight-after-restart) below. The earlier initialization-time captures remain excluded from the aircraft comparison.

## Loaded stock 787 flight after restart

The user confirmed an airborne 787, descending from 40,000 ft, on West Europe. Own-aircraft positive controls showed moving coordinates, roughly 462 kt groundspeed, and falling altitude. Initial raw traffic arrays were empty while 23 nameplates were readable. A native 100 NM aircraft/helicopter scan returned no other aircraft; an independent ALL-object scan returned scenery and own characters, with no exceptions. This was a loaded-flight test, unlike the earlier initialization captures.

When Lasterminater approached, the existing app started receiving its A319 as Coherent ID **1805078589**. All three query routes also returned it. An extra bound map did not add coverage: the player was present before that binding and remained after unbinding. The installed EFB Atlas app source was inspected and its `updateTraffic()` calls the same global `GET_AIR_TRAFFIC`, explaining why the tablet is not an independent source in this installation.

| Observation | Result |
| --- | --- |
| First acquisition | 16:49:19 UTC; nearby nameplate approximately 9.20 km |
| First continuous run | 68 samples over 68.36 seconds; maximum receive interval 1.25 seconds |
| First loss | Last sample 16:50:27 UTC; nearby label approximately 11.57 km |
| Reacquisition | 16:52:02 UTC, same Coherent ID; nearby label approximately 8.60 km |
| Second run by 16:55:41 | 216 received samples; includes a period of repeated unchanged positions |
| Last reviewed coordinates | 51.4741642 N, 4.0979023 E; returned altitude approximately 35,920 ft |
| Last 14-second motion estimate | Approximately 380 kt groundspeed, 284 degrees true ground track, nearly level |
| Reported heading | Approximately 280 degrees; kept distinct from position-derived ground track |

Nameplate values are sampled every five seconds in the log, so the distance figures are approximate contemporaneous observations, not exact acquisition thresholds. These data support a distance-dependent availability hypothesis but do not establish a fixed radius. Earlier nearby omissions still prevent a general guarantee.

A native ID **134692870** appeared at 16:49:18 and was removed at 16:50:28, closely matching the first Coherent window. Its separately requested coordinates and STRUCT LATLONALT were all zero, but groundspeed was about 380 kt. This is a plausible corresponding native object; there is no proven ID mapping. The target's label changed from a Fenix model description to Asobo PassiveAircraft, yet the second Coherent run worked with the passive label, so passive-model naming alone does not explain failure.

During this capture the user selected Lasterminater and following engaged at 16:53:05 through Standard SimConnect, configured 2 NM behind. The app logged 34 guidance outputs and selector transmissions. At 16:53:45 it stopped with `Acquiring target flight motion` after target coordinates froze. The captured UI showed the last selectors at Mach 0.78, heading 285, altitude 36,000 ft and V/S +1,900 ft/min, with autothrottle reported unarmed. This is evidence of acquisition and selector control, **not** a completed or validated formation flight. The automatic stop retains the last selector values and does not automatically re-engage when motion returns. No diagnostic script sent camera or autopilot commands.

The app's repeated SimConnect exception 25 in this session belongs to its unconditional PMDG telemetry subscription on the stock aircraft. The independent native probe had no exceptions. That stock-adapter diagnostic noise is a separate issue from missing multiplayer positions.

Evidence:

- `artifacts/acquisition-research/native-matrix-20260917-164636.json` and `native-matrix-20260917-164950.json`.
- `artifacts/acquisition-research/map-ab-20260917-164749.json` (empty loaded flight) and `map-ab-20260917-165041.json` (Lasterminater before/during/after binding, cleanup confirmed).
- `artifacts/acquisition-research/live-track-Lasterminater.json` and `EscortPlane2024-20260917-164739-19752-summary.json`.
- Exported `EscortPlane2024-20260917-164739-19752-traffic.csv` (284 raw samples) and `EscortPlane2024-20260917-164739-19752-labels.csv` (25 label rows at export).
- `artifacts/acquisition-research/stock-787-live-map.png` captures the existing Following tab during the temporary stationary report; despite its filename, it is not a radar screenshot.
- Raw log: `dist/EscortPlane2024/logs/EscortPlane2024-20260917-164739-19752.jsonl`.

The SDK installation/restart cannot be credited as the cause: the previous SDK 1.7.3 tests had already acquired jordixgangster intermittently, and neither aircraft type nor restart was isolated from target/range changes. The actual positive result is acquisition of Lasterminater using the existing app and no target-side companion.

## What was measured locally

### Native SimConnect, independent variable definitions

`tools/simconnect-position-research.py` opens a separate read-only connection. It requests TITLE, CATEGORY, SIM ON GROUND, PLANE LATITUDE, PLANE LONGITUDE, PLANE ALTITUDE, STRUCT LATLONALT and GROUND VELOCITY independently, avoiding a combined definition that could hide a field-specific failure. Three scans run over 25 seconds: ALL objects within 20 NM, AIRCRAFT/HELICOPTER within 100 NM, and own-aircraft positive controls. No write or camera functions are bound by the probe.

The PMDG-era capture returned valid own-aircraft coordinates and ordinary parked aircraft coordinates, with no SimConnect exceptions. One unnamed aircraft ID, **71057420**, had ground velocity about **355 kt**, but latitude, longitude, altitude and STRUCT LATLONALT were all zero. This ID has not been matched to a gamertag. Other blank aircraft IDs also lacked positions. This shows why listing IDs alone does not solve guidance.

Pilot/character objects were also examined because an older workaround suggested using multiplayer pilot children. No moving target position was recovered that way. Nearby character coordinates must not be mistaken for the selected aircraft.

Evidence: `artifacts/acquisition-research/native-matrix-20260917-161623.json`. The later `native-matrix-20260917-162100.json` belongs to **787 initialization**, not a completed flight. Its ID 0 / four-byte empty result is not a discovered aircraft.

### Three Coherent traffic routes

Microsoft's current avionics source registers `JS_LISTENER_AIR_TRAFFIC` and calls its listener's `GET_AIR_TRAFFIC`. The application currently registers `JS_LISTENER_MAPS` and uses the global Coherent call. Both scoped listeners and the global call were therefore compared in a spare cockpit instrument view. [Microsoft Traffic.ts](https://github.com/microsoft/msfs-avionics-mirror/blob/main/src/sdk/instruments/Traffic.ts)

All three initially returned empty arrays. Later all three returned **jordixgangster**, model A333, traffic ID **2445125215**. The existing application also received that contact, from **16:16:13.489 to 16:16:40.390 UTC**. Its position changed from approximately 51.9009084, 4.4755810, 1111 m to 51.8653612, 4.5116744, 1546 m. This is real position acquisition, not a synthesized marker.

The nearby nameplate readings changed from approximately 7.28 km to 9.69 km during that window. The player remained in nameplate data after positions stopped. Another player, Ferb#8423, had a displayed distance around 5.85 km without a corresponding position response. Range may influence availability, but these observations do not establish a universal distance threshold or a distance-only fix. The returned on-ground flag for jordixgangster was inconsistent with its moving, climbing positions.

Evidence: `artifacts/acquisition-research/comparison-summary.json`, individual debugger replies under `artifacts/coherent/`, and `dist/EscortPlane2024/logs/EscortPlane2024-20260917-160800-34620.jsonl`.

### Explicit map initialization

A bounded diagnostic bound its own small Bing map texture, centered it on own position with a 100 NM radius, and cleaned up that map afterward. It did not change an existing map or mount a visible map element. The first attempt used an incorrect LatLong object; that attempt is invalid evidence. A corrected attempt initialized successfully.

The transient player appearance overlapped this investigation, but occurred through all three routes and continued after automatic unbinding. It cannot be attributed to the map binding. A controlled before/bound/after run subsequently completed in the 787 MFD, but **during flight initialization**, so its empty arrays do not settle the hypothesis. Repeat after the flight is ready. Binding implementation was checked against [Microsoft BingComponent.tsx](https://github.com/microsoft/msfs-avionics-mirror/blob/main/src/sdk/components/bing/BingComponent.tsx).

Evidence: `artifacts/acquisition-research/map-ab-20260917-162151.json`; final state explicitly records the diagnostic map as unbound. Two earlier map-ab files are aborted reload-time runs and should not be counted as successful experiments.

## What the primary sources establish

| Route | Evidence and implication |
| --- | --- |
| SimConnect remote coordinates | Asobo's July 29, 2026 response explicitly states that PLANE LATITUDE/LONGITUDE are not multiplayer SimVars and that extending the supported list is tracked. That explains why a valid ID and readable controls or velocity need not provide position. [Asobo response](https://devsupport.flightsimulator.com/t/no-multiplayer-simobject-data-through-simconnect-requestdataonsimobjecttype/17869) |
| Multiplayer replication ranges | The SDK lists Near/Far replication categories for supported variables. These are not a documented hard range limit for all Coherent traffic queries. Do not infer a GET_AIR_TRAFFIC range guarantee from them. [Simulation Variables](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimVars/Simulation_Variables.htm) |
| Coherent availability | An August 19, 2026 report on 1.8.14.0 describes unstable counts and empty arrays. Asobo replied on August 21 that a similar issue is visible in the EFB and was more evident in high-traffic areas with live traffic and multiplayer enabled. A September 15 follow-up asks for a fix; no fix is established by that exchange. This closely matches the user's tablet observation but does not prove the exact local cause. [Coherent report](https://devsupport.flightsimulator.com/t/coherent-get-air-traffic-call-returns-inconsistent-data/18356), [separate multiplayer report](https://devsupport.flightsimulator.com/t/multiplayer-traffic-not-ai-incorrect-values/17557) |
| TCAS / FLARM calls | GET_TCAS_PLANES and GET_FLARM_PLANES appear in the map listener documentation, whose descriptions are incomplete. Earlier calls returned NoSuchMethod, including scoped tests during 787 initialization. **Loaded-flight retest:** all four scoped calls succeeded with empty arrays. These methods are callable in that context, but no additional contact was recovered. [Listener documentation](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/JavaScript/Coherent_Listeners/JS_LISTENER_MAPS.htm), [new experiment](787-TCAS-DATA-RESEARCH.md) |
| WASM variable reads | Moving the same unsupported variable read into WASM is not an established workaround. A developer in the Asobo thread also reports zero from fsVarsAVarGet. No new WASM module was installed for this investigation. [Vars API](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/WASM/Vars_API/fsVarsAVarGet.htm) |
| JavaScript SimVar target selection | The JavaScript SimVar API's dataSource parameter is not an arbitrary SimObject-ID selector. [Working Title clarification](https://devsupport.flightsimulator.com/t/simvar-getsimvarvalue-on-specific-simobject/14566) |
| Camera API | CameraGet reads the camera pose in a chosen reference frame; it is not a passive query taking an arbitrary target Object ID. Tracking-camera approaches depend on camera control and target association, and are not proven here. Earlier project experiments visibly changed the user's camera without establishing target coordinates. No camera changes were made in this research pass. [CameraGet](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Camera/SimConnect_CameraGet.htm), [Asobo clarification](https://devsupport.flightsimulator.com/t/cameraapi-cameraget-behavior-requires-clarification/17697) |
| Historical pilot-child workaround | A 2022 developer described obtaining positions from pilot objects near multiplayer aircraft. The local ALL-object experiment did not reproduce a usable target position. This is not a current compatibility guarantee. [Original report](https://devsupport.flightsimulator.com/t/online-multiplayer-traffic-not-being-returned-in-traffic-requests/3794) |
| SDK upgrade | SDK 1.7.3 is the extracted SDK used for this investigation. Newer SU7 beta SDK references also exist; the earlier 1.7.3 release-notes observation should not be read as a claim that no newer beta exists. No verified traffic fix was identified that justifies another installation or simulator restart. [SDK release notes](https://docs.flightsimulator.com/msfs2024/retail/introduction/sdk-release-notes/), [current SDK developer discussions](https://devsupport.flightsimulator.com/) |

Some developer reports describe successful multiplayer position reads in other contexts, so the conclusion is unreliable coverage, not universal impossibility. [Example of conflicting field experience](https://devsupport.flightsimulator.com/t/camera-api-expose-rotation-data-or-support-rotationreferential-for-multiplayer-simobjects/17715)

## Why visible labels and a radar drawing are insufficient

The existing nameplate reader has recovered 25 loaded labels, including name, model, displayed distance and displayed altitude. The inspected UI attributes have no target latitude/longitude, world bearing or SimObject ID. A label ID belongs to the UI and cannot be used as a native aircraft ID. Texture-atlas coordinates locate a label texture, not an aircraft.

Distance and height alone leave the aircraft's direction undetermined. A radar needs bearing and distance, or world coordinates. Name matching, case sensitivity, removing the 1,000 ft filter and widening the display range cannot restore fields omitted by the source. The app's raw diagnostics query before these display filters.

Screen-image tracking could experimentally estimate bearing from a visible aircraft/label plus a calibrated camera pose. It would depend on view, zoom, occlusion and label placement, and lose the target off-screen. No dependable implementation was found or validated. Memory inspection or network reverse engineering was not attempted, and no maintainable documented route was identified from those approaches.

## Alternatives researched but outside the user's requirement

The following routes were investigated for completeness. They are not the selected development path because they require participating pilots, another network, or do not supply arbitrary native MSFS traffic.

### Direct telemetry from a cooperating lead

Read each pilot's own aircraft, which the local tests demonstrate is available. Send a compact stream from the lead to the follower and feed it into the existing motion estimator and guidance calculation.

Proposed data: session/aircraft identity, sequence number, source sample time, latitude/longitude, geometric altitude with explicit units/datum, ground velocity vector or track/speed, vertical velocity, and pause/loading state. Use local receive time plus source ordering and a clock-offset/delay policy; do not assume two PCs have synchronized clocks. IAS is useful optional telemetry, but equal IAS does not ensure equal groundspeed in different wind/aircraft conditions.

A starting design target is 5–10 updates per second, to be validated with network delay and loss tests. Authenticate paired sessions, reject replayed/out-of-order samples, expire stale tracks and stop commands on missing or paused data. Reset filtering after teleportation or aircraft changes. Do not let extrapolation manufacture indefinite freshness. The sender needs no remote autopilot access.

This requires both pilots' participation and an actual two-PC trial. The first milestone should be stable position comparison while stationary and moving, followed by controlled offsets and bounded autopilot tests. It will not reveal arbitrary Xbox/MSFS players who are not sending telemetry.

### JoinFS or another traffic-injection network

JoinFS currently offers a dedicated MSFS 2024 build and peer-to-peer shared sessions. Both pilots would join that network. The candidate integration is reading its locally injected aircraft, or an explicitly supported export, instead of native MSFS multiplayer objects. **Readability and update quality in our follower still need testing.** This investigation did not install JoinFS or join a network. [JoinFS support](https://www.joinfs.net/en/support/), [current downloads](https://www.joinfs.net/en/download/)

MSFSTrafficService illustrates the distinction: its author supports injected multiplayer/AI traffic but explicitly excludes built-in multiplayer because the necessary SimConnect data is missing. Wrapping the same API in a web service does not unlock it. [Project documentation](https://github.com/laurinius/MSFSTrafficService)

### Public flight-network feeds and recorded traffic

VATSIM's public feed contains VATSIM participants and regenerates every 15 seconds. It is not an Xbox-gamertag lookup. At 250 kt an aircraft travels about 1.04 NM in that interval; such a feed alone is a poor basis for close formation control. Locally injected network traffic is a separate question. [VATSIM data API](https://vatsim.dev/api/data-api/get-network-data/)

Sky Dolly's recording/replay formation features can help test a controller with repeatable aircraft paths, but do not establish access to arbitrary live native multiplayer targets. [Sky Dolly project description](https://github.com/till213/SkyDolly/blob/main/ABOUT.md)

## Remaining validation

The loaded stock 787 comparison is now recorded above. Further validation should measure continuity for additional nearby players and sustained following through turns, packet freezes, range changes and target reacquisition. Repeat explicit map binding only in a spare debugger view and clean up its map afterward.

Keep the current opportunistic sources and nameplate diagnostics. Do not change the production bridge merely because a different listener name sounds more promising: the local comparison has not shown a coverage improvement. Do not substitute a cooperative sender or JoinFS for the requested product. The stock-aircraft autopilot adapter also requires its own live validation, separate from traffic acquisition.

The documented high-traffic correlation suggests a further controlled experiment: compare the same visible player with AI/live traffic reduced while native multiplayer remains enabled. This is a hypothesis, not a confirmed workaround; it requires a loaded flight and coordinated observation. Do not silently change the user's traffic settings. Also distinguish camera attachment from CameraGet: CameraSet can specify a reference Object ID, but obtaining that ID, proving a remote moving position and preserving the user's view remain unresolved requirements. Camera world lockers load scenery around a reference and require camera acquisition; they are not a coordinate-read API. [Camera API documentation](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Camera/Camera_API.htm)
