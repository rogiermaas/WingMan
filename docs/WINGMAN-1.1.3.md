# WingMan 1.1.3

- Keep on top now explicitly restores the native Windows topmost state at startup, handle creation, and when focus changes or Windows drops the flag. It does not activate WingMan or take keyboard focus from MSFS. Both normal and compact views use the same setting.
- Automatic speed defaults use design stall/cruise data instead of current IAS during takeoff. The former calculation could produce a one-knot range, observed as 227.5–228.5 kt in the stock 787, holding speed at 228 kt and preventing climbs through the minimum-speed protection. A versioned migration repairs that specific old generated range; unrelated custom ranges are retained.
- Altitude and V/S ramps retain fractional movement between 5 Hz commands. Previously, a 40-unit change could round back to the same 100-unit selector value every update and prevent further movement.
- Speed caps and vertical speed-protection messages now appear in the main status while following; a vertical restriction no longer hides the speed-cap explanation.

Validation: 175 offline checks passed, including climb and descent ramps at 5 Hz, the legacy range fingerprint, light-aircraft defaults, native topmost recovery and unchanged foreground focus. Read-only live telemetry confirmed DESIGN SPEED VC is supplied by the stock 787. The reported session's logs confirmed that speed, altitude and V/S commands were being acknowledged; constraints held speed at 228 kt and V/S at zero. No separate live autopilot test commands were injected.

The existing simulator/aircraft mode requirements remain in place. Stock aircraft still require the pilot to engage an appropriate autopilot/autothrottle mode and V/S mode for automatic vertical output. Aircraft performance and configured limits can still prevent catching a faster lead.

This executable-only update uses the existing update-manifest key. Windows Authenticode signing remains pending.
