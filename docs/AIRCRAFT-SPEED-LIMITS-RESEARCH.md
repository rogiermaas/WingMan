# Aircraft speed limits research

Research date: 18 September 2026. This records findings for a future speed-envelope feature; it does not describe a feature implemented in WingMan 1.1.3.

## Maximum operating speed versus achievable speed

Aircraft commonly specify an airspeed ceiling (VMO) and a Mach ceiling (MMO). Both apply: as pressure falls with altitude, the Mach ceiling corresponds to a lower calibrated airspeed. Neither ceiling guarantees that the aircraft has enough thrust to sustain that speed, especially while climbing. Weight, configuration, temperature and available thrust also matter. Groundspeed is not an aircraft structural speed limit.

[Airbus's explanation](https://safetyfirst.airbus.com/control-your-speed-in-cruise/?airbus-iframe=true&airbus-post=2139) covers the speed envelope, crossover altitude, and reduced thrust margin at high altitude.

## Published examples

| Aircraft | Published VMO | MMO | Source |
| --- | --- | --- | --- |
| Boeing 737-800 | 340 KCAS | 0.82 | [FAA TCDS A16WE, revision 57, section VII, page 23](https://upload.wikimedia.org/wikipedia/commons/d/d0/A16WE_Rev_57.pdf), dated 28 February 2017; reference data, check the applicable aircraft AFM for other limits |
| Airbus A320 family, including A321 | 350 kt | 0.82 | [Airbus Safety First, Control Your Speed, table of VMO/MMO](https://safetyfirst.airbus.com/app/themes/mh_newsdesk/pdf/safety_first_special_edition_-_control_your_speed.pdf) |
| Boeing 787 | 350 KEAS, as explicitly written in the TCDS | 0.90 | [EASA.IM.A.115 issue 30, 17 December 2025](https://www.easa.europa.eu/en/downloads/7302/en) |

Do not treat KEAS, KCAS, KIAS, true airspeed and groundspeed as interchangeable. In particular, the 787's TCDS entry must not be copied as a 350 KIAS MCP limit.

Illustration for the 737-800 and A321, gear/flaps retracted, using standard-atmosphere pressure and subsonic compressible-flow conversion. Values are calculated approximate KCAS ceilings, not cruise targets, thrust guarantees, or airspace speed permissions; allow margin below the ceiling.

| Pressure altitude | 737-800 | A321 |
| --- | --- | --- |
| 10,000 ft | 340 kt | 350 kt |
| 20,000 ft | 340 kt | 350 kt |
| 30,000 ft | 312 kt | 312 kt |
| 35,000 ft | 279 kt | 279 kt |

Calculation: pressure ratio delta = (1 - 0.0065 * h / 288.15)^5.2558797, h in metres below 11 km. qc/p0 = delta * ((1 + 0.2*M*M)^3.5 - 1). CAS = a0 * sqrt(5 * ((1 + qc/p0)^(2/7) - 1)), with a0 = 340.294 m/s. Use min(VMO, CAS at MMO). Do not use this atmospheric illustration in place of live pressure and validated aircraft instrument data.

## MSFS variables and live observations

[SDK miscellaneous variables](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimVars/Aircraft_SimVars/Aircraft_Misc_Variables.htm) document AIRSPEED BARBER POLE as the redline airspeed, dynamic on some aircraft, and BARBER POLE MACH as its associated Mach number.

[SDK flight-model variables](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimVars/Aircraft_SimVars/Aircraft_FlightModel_Variables.htm) document MACH MAX OPERATE, REFERENCE SPEED MAX IAS, REFERENCE SPEED MAX IAS GEAR DOWN and DESIGN SPEED VC. The last is ideal cruise speed from aircraft configuration, not a certified maximum IAS.

Read-only SimConnect probes of the user's stock 787-10 returned six samples without exceptions:

- Earlier observation near indicated 27,294 ft: barber pole 355 kt; associated Mach 0.8883; maximum design Mach 0.90.
- Saved observation at 09:48:00 UTC near indicated 32,592 ft: barber pole 317.4 kt; associated Mach 0.8867; maximum design Mach 0.90; reference maximum IAS 450 kt; gear-down reference 280 kt; design cruise speed 495 kt. Aircraft clean, Mach 0.7824, IAS 280.1 kt, TAS 469.7 kt.

Evidence: `artifacts/aircraft-speed-limit-observation.json`; probe: `artifacts/read-follow-telemetry.py`. Indicated altitude is not necessarily pressure altitude. Reported simulator values are not proof that every field matches the add-on's PFD or certified AFM. The 450 kt reference field is clearly unsuitable as an unchecked operational cap for this aircraft. The dynamically changing barber pole is the more promising input and still needs cockpit comparison and aircraft-specific validation.

## Proposed WingMan behavior

Use the most restrictive of the pilot's configured ceiling and validated aircraft limits, with margin below the redline/Mach ceiling. Account for flap/gear configuration, reject zero/non-finite/implausible data, and retain a conservative fallback with a visible explanation when reliable limits are unavailable. Do not interpret ideal cruise speed as a redline or silently raise a pilot's selected maximum.

Convert between IAS/CAS/Mach consistently with the aircraft's actual instruments and live pressure; directly targeting calibrated airspeed as IAS can introduce errors in MSFS. Validate both the stock 787 and PMDG interfaces before enabling automatic limits generally. Minimum-speed protection also needs configuration/weight-aware data rather than a static design stall speed alone.

If the selected lead cannot be caught within these limits, show that reason. An operating ceiling also needs to be distinguished from available climb/acceleration performance. Automatic IAS/Mach mode switching would be a separate behavior to implement and test.

WingMan 1.1.3 repairs collapsed initial speed settings and command ramping; it does not yet consume these dynamic redline variables.
