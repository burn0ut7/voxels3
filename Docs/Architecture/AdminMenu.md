# Admin debug menu

First slice: local host only (offline counts as host), no guest grants or RPCs.
The player prefab owns AdminFlightMode, a normal PlayerController movement mode.
It owns noclip state and validated speed (100..10000 world units/s, default1000,
Shift multiplier3). PlayerController remains the camera and movement owner.
The scene's AdminMenu PanelComponent owns open/targeting/text/status UI state.
Q toggles the menu through the existing Menu action; close button also works.
Only active menu/target selection captures gameplay inputs through PreInput and
terrain-tool admission. Closed UI performs no world queries or text refresh.

Flight overrides movement velocity, gravity and solid collisions through the
engine MoveMode extension point; mode exit restores solid collision state and
clears velocity. Collision readiness continues streaming for the real player,
but must not hold a noclipping body. Returning to walking requires current
collision coverage and a clear standing body trace. All changes run on the
engine thread; no jobs, terrain edits, replication protocol or persistent data.

Teleport is one click on already published voxel_terrain collision, maximum
32768 units from the camera. A miss leaves position untouched with visible status.
Destination uses hit position plus outward standing-body clearance; full standing
bounds must be ready and unoccupied. No distant field sampler or separate mesh
query is introduced: unavailable collision is an explicit limitation of this slice.
The menu uses the actual mouse pixel ray, not the button's screen position: its
button arms a separate fullscreen target picker. Normal streaming follows the
real moved player; existing readiness protection handles subsequent movement.

Alternatives: disabling PlayerController would duplicate camera/input ownership;
a new terrain ray marcher would duplicate collision query responsibility. Reuse
MoveMode and scene traces for this bounded debug slice. Future admin tools can
add ordinary sections/actions to the panel without a registry framework.
Validation and performance acceptance: ADMIN-001/v1 in ../ValidationResults.md.
Engine API evidence: installed compiler plus official MoveMode source:
https://github.com/Facepunch/sbox-public/blob/master/engine/Sandbox.Engine/Scene/Components/Game/PlayerController/Modes/MoveMode.cs

The clearance check also queries the canonical field's conservative density range
for the whole standing box. Physics mesh overlap cannot exclude being buried inside
a solid volume. Uncertain bounds reject safely (including some valid tight spaces).
Teleport has32units of extra plane clearance. This is a conservative admission rule,
not a new field representation. A voxel_admin console toggle shares the menu's
normal open/close path. Engine text inputs use TextEntry Value:bind.
