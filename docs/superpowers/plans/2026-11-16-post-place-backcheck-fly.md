# Post-place backcheck flight capture

Goal: After both placements in a normal pair, capture WsMod_Shp while retreating along Y, validate every pending pocket before LeaveFixture can advance. The user confirmed this scope after the November 16 log analysis.

- [x] Add executable regression coverage for flight result reuse, fixed fallback, barcode rejection, stop and pending acceptance.
- [x] Defer normal production pair acceptance to LeaveFixture; special modes and disabled flight retain existing checks. Do not reuse entry/pre-place images. Keep placed/pending state on stop, discard flight images on each re-entry.
- [x] Reuse existing flight capture, negative-direction calibrated offsets, trigger cleanup and speed handling. Different X columns or insufficient travel fall back to static capture. Separate barcode position retains static capture. Rework must empty its incoming nozzle before picking a rejected placed part; replacement uses static verification.
- [x] Run focused regressions, compile Debug to a temporary output, inspect scoped diff. No commit or deployment. Machine must validate reverse-direction offsets, motion limits and exposure.

Validation: three-nozzle-post-place-fly-regression (50 assertions plus release order), three-nozzle-checks, sequence, coordinator (31), independent dispatch (11), stop-state (35), collision envelope, two-speed planning (25) and motion observer (32) passed. New regression first reproduced the absence of flight-result support; another red/green case verified restart of replacement after the rejected part is already removed.

The existing checkout remains dirty with unrelated work preserved. Debug output is isolated in TEMP; no commit or deployment performed. Independent read-only review found no confirmed P1/P2 in this task delta. Final Debug build exited 0: C:/Users/gdfanyd/AppData/Local/Temp/MINI-PostPlaceFly-Verified-b18b09da18ff4f7f9edd59b76320f12a/build.log.
