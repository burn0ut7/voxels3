# The Dead Wake: inventory and tree gathering

Revision 2, September 24, 2026. **Qualified offline opening prototype.** The
user explicitly rejected the former Pack counters and ground-log progression.
That candidate and its failures remain in the validation ledger; it is superseded,
not an accepted gameplay foundation. [Game direction](../TheDeadWake.md) owns the
larger roadmap. This contract describes the implemented replacement and its limits.

## Player-facing loop

Start empty. Gather a small finite supply of branches directly from a standing
tree and pick up loose stone. Open the traditional inventory, craft a stone axe,
equip it through the hotbar and chop the standing tree for larger wood yields.
Trees are the source of wood. There is no starter log field to grind. The authored
oak is the first tree interaction target; this slice does not claim a populated
forest, voxel mining, multiplayer inventory or persistence.

The inventory has 27 backpack slots plus nine hotbar slots. Left click picks up,
places, swaps or merges a stack; right click takes half or places one. Shift-click
transfers between storage and hotbar. Number keys and the wheel select hotbar
slots. Empty or non-tool slots leave the player without an axe. Tab opens/closes
the inventory; Escape and the visible Close control also close it. Closing returns
any cursor-held items safely; if storage cannot accept them, keep the window open
with an explicit explanation. Crafting with a held stack is refused. There is no
hidden fixed Wood/Stone counter separate from the slots.

## Ownership and transactions

DeadWakeSurvivor remains the local player action owner. Its DeadWakeInventory owns
36 immutable-value stack records, one cursor stack, selected hotbar index and a
revision counter. The cursor is part of the same inventory, not a copy or a UI-only
item. HUD and contextual prompts derive from these records. Item definitions are
currently Wood, Stone and StoneAxe: resource stacks cap at 64, axes at 1. No catalog
of unimplemented items, service framework or recipe queue is introduced.

Adding resources first verifies whole-yield capacity; rejected gathering never
consumes the world resource. Crafting stages removal of 4 wood and 2 stone and addition
of one axe, then commits only if the result fits. Multiple axes are legitimate
items when paid for. Slot moves conserve item totals, obey stack limits and commit
on the engine thread. Inventory transfers and world actions are local-host/offline
only, guarded at the survivor/UI action boundary. Joining a network session
suspends the opening; no unsynchronized co-op claim is made. Session restart resets
inventory and world harvest state; no durable save is written. A network transition
returns the cursor if it fits. Otherwise the suspended inventory stays visible with
the held item and an explanation; leaving the network session restores arranging
and normal close. No held item is discarded or hidden.

DeadWakeResource owns each loose/dropped stack. DeadWakeTree owns finite standing
tree branch supply and trunk harvest state. Renderers/colliders are derived
presentation. Felling physics starts from OnUpdate after the resource transaction
commits. A first-solid eye ray with 128-unit reach and no player/viewmodel hit
selects the resource; terrain and other solid obstacles block it. The current oak
has six branches (0.8-second gathering cooldown) and twelve axe contacts, yielding
four wood each. A swing contacts at 0.2 seconds, recovers by 0.48 seconds and can
start again after 0.7 seconds. Axe hits resolve
once at the contact phase with a fresh ray, capacity and current selection check;
opening UI, unequipping or networking cancels pending action without rewards.

DeadWakeResources owns bounded loose-stone placement and shared item models. It
uses canonical terrain collision and generated water level, rejects slopes,
unsupported/wet sites and overlap with live resource objects. Retry places only
missing resources and cannot replenish consumed ones. Ground wood/log starter
models and their harvest path are removed rather than retained as a second loop.
Drops use the same real item records, ground support and reach rules. G drops one
selected item; holding the configured Run modifier drops the stack. Failed ground
placement preserves inventory. There are at most 128 owned live world stacks.
The standing oak retains its existing model/LOD/solid-trunk owners; tree resource
state integrates with that actual scene hierarchy rather than replacing its art.

## Presentation and finish

Use [inventory concept](../../Art/References/TheDeadWake/inventory-v1.png) and its
[brief](../../Art/References/TheDeadWake/inventory-brief.md): charcoal window,
ivory labels, natural material icons, clearly bounded slots, adjacent crafting,
selected hotbar outline, ample world margins. Closed HUD contains the actual
hotbar, compact next-step/interaction feedback and session-reset notice; remove
Pack counters. Recipe quantities distinguish required from available. Hovered and
cursor-held items remain identifiable; no invisible item on close or hidden full
inventory failure. Input labels use configured bindings. Hover names derive from current slot contents.
Inventory capture holds and restores the controller input setting, while clearing
wish movement; gravity remains active. Inventory, admin and biome-map modal capture
share input suppression. Disabled owners release capture, cancel a pending strike, reset swing animation
and hide their HUD and held tool. Re-enabling cannot finish an abandoned strike.
The brief close grace period prevents a closing click from striking the world.

Use the saved [axe motion reference](../../Art/References/TheDeadWake/hatchet-motion-v1.png)
for ready/preparation/contact/recovery and award at contact. The original generated
[flint texture](../../Art/References/TheDeadWake/flint-brief.md) remains the held
axe material. [Tree-felling reference](../../Art/References/TheDeadWake/tree-felling-v1.png)
sets an away-from-player fall, grounded rest and a short stump, with no repeated
reward from the fallen visual. Independent in-world checks cover the fitted cut,
contact/recovery, grounded trunk and inventory presentation in the authored scene.
The final canonical performance qualification and historical comparison limits are
recorded in the validation ledger. No health, stamina, waves or fabricated progress
widgets appear before those systems exist.

## Bounds and alternatives

Thirty-six slots are a fixed small bounded collection. Slot changes are immediate
engine-thread operations; only non-hot-path crafting clones the 36-value staging
array. UI rebuilds on inventory/action state revisions; mouse cursor position must
not rebuild the whole inventory every frame. Closed inventory must not retain a
hidden expensive grid. Resource target traces remain 10 Hz plus fresh mutation rays.
No per-frame full-scene enumeration or threaded inventory mutation.
The installed engine allocates a lookup predicate for each action query. Survivor
checks reject buttons that are up before asking for their pressed edge, and reject
cooldowns or empty-hand drops before querying input. Binding resolution and edge
semantics remain owned by the engine; there is no duplicate input-state cache.
Hotbar, gather, attack-start and drop queries run in OnFixedUpdate using the
engine's accumulated tick input; UI open/close, capture, contact timing and visual
recovery remain per-frame. Fixed actions reject modal/suppressed input and the
inventory, settings and biome-map toggle keys, including when they first arrive
before the UI frame.
No world action executes while opening or closing these menus. The installed engine
retains press/release and wheel events between ticks; we do not cache them ourselves.
This repair's allocation and frame-cost qualification is recorded in the ledger;
source equivalence alone does not establish its performance benefit.

The previous pair of counters was rejected because it cannot support conventional
storage, selection and transfer. A data-only inventory prototype disconnected from
world gathering is also rejected. A full item database, workstation system,
authoritative multiplayer protocol and save migration remain separate increments.
Their absence must stay explicit; they are not silently represented by UI buttons.

Required checks include the complete tree→inventory→craft→equip→tree loop;
stack split/place-one/merge/swap/quick-transfer; cursor close/reopen; full capacity
and recipe-output rejection; cooldown/reach/obstruction; finite tree depletion,
falling contact and no duplicate rewards; UI input capture and return to movement;
network refusal; restart. Source inspection does not establish usability or visual
finish. Preserve prior canonical performance failures; the unchanged figure-eight
still measures the actual authored one-oak scene. New gameplay criteria are frozen
before first run in DEAD-WAKE-INVENTORY-002/v1.

## Autonomous editor play control

Editor/GameplayInputTools.cs owns temporary, bounded native MCP action playback.
It supplies configured action names to the engine input state around the real
scene update/fixed-update callbacks. Gameplay still passes through the production
player/controller and survivor Input handlers; the editor tool never changes
inventory, resource value or cooldowns. Current/previous button state and analog
movement are restored after each stage. Optional wheel/Escape pulses use the public
input setters once per input context; only injected pulse state is restored, so a
consumed physical Escape is not replayed during cancellation. Optional held modifiers precede the primary action by a bounded lead-in. A
command holds for at most three seconds per active input context;
physical input, cancellation, pause, scene replacement and Play stop end it.
Cancellation delivers release on the next active scene stage. A human holding the
same action takes priority over a synthetic release. A paused/stopped/suppressed
context cannot consume a release; it is terminated with an explicit status and
never replays on resume. A one-second extra wall-clock deadline bounds stalled
contexts; incomplete input is reported as expired, never completed.
Scene hooks exist only for an active command and are removed outside callbacks;
cleanup also releases the scene reference. Edge counts describe supplied input,
not independent proof that a gameplay consumer acted on it.
The scene remains visible and normal human controls remain enabled. Readback
reports actual runtime components rather than authored prefab copies. This is
editor tooling, with no shipped runtime component or alternate gameplay path.

Installed 26.09.22 XML and the official Facepunch sbox-public Input.Actions.cs,
Input.Context.cs, Scene.Tick.cs and Scene.System.cs establish action edges,
per-stage input contexts and disposable Scene.AddHook callbacks. Compilation and
actual gather/craft/harvest behavior must verify installation compatibility.


UI automation resolves the actual active ScreenPanel tree, checks the intended
control against the topmost visible pointer receiver at its center and queues
ordinary MousePanelEvents. It uses the event modifier state for shift-click.
`Queued` means only that events were submitted; a separate component readback and
render establish their outcome. Native commands never call inventory methods.
UI hooks are released from the editor frame callback, outside scene iteration.

The bounded `record_play_camera` editor command records the actual current Play
camera plus UI into a new WebM file, at up to 30 FPS and 960 x 540. It does not
advance simulation, use a separate scene or replace player input. Wall-clock frame
timestamps accompany each clip; pauses, scene replacement and stop end capture.
This capture adds rendering/encoding work and must never run inside performance
measurements. A recording provides motion evidence, not a frame-rate benchmark.


### Cut presentation and verified scope

The initial falling whole-root model failed the reference's severed-trunk intent.
The first circular radius-10 cut was rejected for a visible seam. The saved
oak-cut-v1.json contour now owns the cut height (14 local units) and exact section
shape for this authored oak. On depletion, bark/fine-wood shaders clip below that local plane;
a downward end-grain cap follows the falling tree and a matching upward cap stays
on its stump. Both use the saved oak end-grain albedo. Standing trees use a disabled
cut threshold. No terrain density or original tree asset is rewritten. The existing
simple fall capsule remains derived physics above the cut. The final bank views
show a grounded trunk with no demonstrated woody-branch penetration in valid views;
occluded contact interfaces and other terrain/tree combinations are not qualified.

Alternatives considered: duplicating the imported tree mesh for a permanently cut
asset would bind gameplay to one export and duplicate geometry; runtime triangle
surgery would require rebuilding the large authored model. A local render cut and
small cap preserve the existing tree material/LOD owners with bounded work. This
render approximation passed the recorded close/oblique seam, shadow and bank-contact
checks for this authored oak; it is not a general mesh-fracture simulation.
The shaft now extends to local Z=-12 (head remains at Z=16..22.5) so the first-person
handle continues out of frame during contact. World-drop offset and 36 × 10 × 4 collider cover
the same complete model lying on its broad side; no separate held-only shape is introduced.

Cut geometry refinement: the constant-radius approximation showed an open crescent
in close inspection. The replacement contour is authored from the actual imported
LOD0 mesh at the same cut plane, using the read-only editor model_plane_section
inspection command. It reports bounded local-space triangle/plane intersections,
not gameplay state. A saved ordered contour supplies both caps and the stump side;
there is no whole-tree CPU extraction during play. Freeze felled wood wind and LOD
so its static cut edge continues to match that contour. This replaces the circular
cap implementation, without retaining a second cut path.
