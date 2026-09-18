# WingMan 1.1.2

Trying to follow while stationary in 1.1.1 could display “Invalid own-aircraft data.” WingMan now explains that the pilot must take off and climb to at least 500 ft above ground. Below that height it shows the current height and the climb requirement. Other invalid telemetry reports identify the affected measurement instead of using one generic message.

The existing airspeed, height, data freshness and autopilot checks remain enforced. This release does not enable taxiing or automatic takeoff. Rejected engagement attempts are recorded in the local diagnostic log.

The Followable and Follow user switches now use a fully custom control, removing the underlying native checkbox painting that could leave lines beside the rounded switch. Mouse clicks, keyboard Space activation and the accessible checked state are retained.

Validation: all 165 offline checks passed, including stationary-ground rejection with no commands, the 500 ft boundary and invalid airborne measurements. A read-only simulator query reproduced the reported condition in a stationary 787-10. No live autopilot commands were sent during diagnosis or testing.

The update is still executable-only and uses the existing update-manifest verification key. Windows Authenticode signing remains pending; this is not a SignPath-signed release.
