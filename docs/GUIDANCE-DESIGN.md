# Ahead recovery scenario: AccusedCascade6

Current live focus is TrymTube, following AccusedCascade6 going offline. The named Coherent feed supports horizontal and vertical motion estimation during flight. This document retains the original ahead-recovery design; an experimental guidance controller, independent selector outputs and PMDG V/S mode handling are now implemented. Live MCP control still awaits validation. The traffic API's ground flag is unreliable and cannot select a guidance mode. See FOLLOWER-USAGE.md for the implemented behavior and limits.

User-provided live test setup, 17 September 2026: own aircraft ahead; AccusedCascade6 is a Fenix A321 rendered as an Asobo PassiveAircraft A321 CEO, approximately 108–110 km behind and to the right, slowly gaining, on roughly the same track. The user is in external view and can see the target. These details identify the test case; they are not telemetry measured by this application.

## Longitudinal recovery

Use target ground track to define forward and right vectors. Project the vector from our aircraft to the desired formation position onto them. Along-track error is positive when the desired position is ahead of us, negative when we are ahead of it. Never turn toward the desired point solely because the along-track error is negative.

For negative along-track error, enter AHEAD_HOLD. Follow the target's sustained track with bounded cross-track correction; reduce desired ground speed below the target's estimated ground speed, within this aircraft's practical envelope. At a 59 NM separation this is a prolonged rendezvous, not close formation. There is no universally safe fixed IAS reduction, particularly at high altitude; target GS must be converted to appropriate IAS/Mach using own air data and wind, with aircraft-specific limits.

Do not wait until the aircraft passes and the requested gap has opened before starting to accelerate. Estimate closure rate, speed-response lag and relative stopping distance. If relative speed is c (m/s) and usable relative acceleration a (m/s²), idealized remaining relative travel while matching speed is c²/(2a), plus c times response delay. Begin convergence toward target speed when the remaining longitudinal travel to the desired slot approaches that amount. These quantities require validation for the actual aircraft; they are not control defaults.

Example only: if the target catches up at 20 kt, matching speed with an effective response of 0.25 kt/s takes 80 seconds and adds approximately 0.22 NM of relative travel, before additional aircraft/autothrottle delay. That is why waiting for the full desired gap before accelerating can overshoot the formation slot.

As the target overtakes, smoothly reduce the below-target speed offset. Enter CAPTURE after passing the ahead threshold with hysteresis and approach the slot with a bounded intercept. Enter FOLLOW only when longitudinal, lateral and vertical errors are small and stable. Use distinct exit thresholds and dwell time. Do not use gamertag text or visual labels as measured positions.

## Lateral and vertical behavior

An aircraft behind and to our right requires a gradual change toward its flight-path line, not a turn directly at the aircraft. AHEAD_HOLD should keep track corrections small; a later CAPTURE phase can allow a larger, still bounded intercept. Heading conversion must account for wind and magnetic variation.

Do not use CameraGet raw altitude for AP altitude until the altitude datum has been validated. Compare against known own-aircraft and AI positions first; an ellipsoid/MSL discrepancy could exceed the requested vertical offset.

## Validation gate

No active control is implemented before multiplayer identity, position, movement and normal-camera coexistence have been demonstrated. If target measurements stop, reject the sample or enter delayed/lost behavior; do not continue seeking its last position. The current acquisition executable remains the tool for resolving that prerequisite.
