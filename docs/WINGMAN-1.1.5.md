# WingMan 1.1.5

Long-range catch-up now uses the pilot's available IAS/Mach speed range instead of limiting the requested ground speed to only 40 kt above the lead. The speed difference decreases as the aircraft approaches the requested formation position. With 100 NM separation and Behind set to 4 NM, the gap to close is 96 NM.

The guidance projects closing motion 20 seconds ahead and limits positive closure demand using a braking-distance curve based on a 0.5 kt/s response assumption. Near the slot, a proportional correction smoothly approaches the lead's speed. Existing minimum IAS, maximum IAS, maximum Mach, motion filtering and readback checks remain active. Ahead-of-slot slowdown remains bounded to 40 kt below the lead before aircraft limits apply. These response assumptions are guidance tuning, not a guarantee of an add-on aircraft's acceleration or braking performance.

When a speed limit clips the requested catch-up demand, heading wind compensation now uses the airspeed actually commanded. This avoids under-correcting crosswind while requesting a large catch-up speed.

Validation: 214 checks pass. Added checks cover 100 NM separation with a 4 NM slot, gradual speed reduction near that slot, braking for high closure speed, ahead-of-slot recovery, retained Mach/IAS limits and wind correction at a limited speed. Closed-loop simulations with 20-second speed response lag and 0.5 kt/s acceleration/deceleration limits settle from both 20 NM and 2 NM slot errors with less than 0.1 NM overshoot. Actual aircraft performance and configured limits still determine attainable catch-up speed.

No additional setting is required. Changing the existing spacing and speed ceilings still applies without stopping Follow.
