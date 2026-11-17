# Special Avoidance UI Visibility Design

## Goal

Make the special dual-arm avoidance setting visible and usable in the System Parameters 1 page without mixing it with the logical safety-position X setting.

## Cause

The runtime-created dual-arm safety-distance group is located at `(590, 704)` and fully overlaps the existing jig-installation protection group at `(435, 701)` with size `(467, 178)`. WinForms places the dynamically added control behind the existing group, so the setting cannot be seen or operated.

## Design

- Keep the independent group containing custom-distance enablement, special avoidance enablement, and the distance value.
- Move that group to a new row below all existing Parameter 1 controls at `(435, 895)`.
- Increase the Parameter 1 auto-scroll minimum height to include the group through Y `1007`.
- Bring the runtime-created group to the front explicitly after adding it, so later layout changes cannot hide it.
- Keep `groupBox60` and `PT_SET.safepos` exclusively for logical safety-position X.
- Restore the five modified `FrSys` UI source files from the current committed source before applying the layout adjustment, because their uncommitted changes have damaged Chinese UI text encoding.

## Behavior

- The special-avoidance switch remains independent of the custom-distance switch.
- Custom distance remains effective only when its enablement switch is selected.
- Saving still persists all three values: custom enablement, special avoidance enablement, and configured distance.

## Verification

- Add a source-level regression script verifying the group does not overlap the jig-installation protection group, is added after form initialization, and is explicitly brought to the front.
- Run all available PowerShell regression scripts and `git diff --check`.
- Verify the parameter page exposes the group by scrolling to the new bottom row in the Windows application.
