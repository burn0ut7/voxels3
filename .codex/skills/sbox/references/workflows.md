# Workflows

Select only the topics involved in the task. Discover current pages through `docs QUERY`.

## Components and scenes

Inspect the existing `.sbproj`, similar component and actual scene/prefab references.
Resolve the intended lifecycle hook from [Component Methods](https://sbox.game/dev/doc/scene/components/component-methods/).
Consider activation, disabled ancestors, destruction, async cancellation and hotload.
Check serialized property types, references, nesting and instance overrides. Code that
compiles but is never attached to the intended GameObject is not an integrated feature.
Use installed templates for creation; resolve any template identifiers explicitly.

## Runtime API access

Read [API Whitelist](https://sbox.game/dev/doc/code/code-basics/api-whitelist/) when using
general .NET, reflection, filesystem or network facilities. Game and editor code have
different access. Check the target compiler's diagnostics; do not silently disable
restrictions or change project publishing mode to make a call compile.

## Networking

Read [Ownership](https://sbox.game/dev/doc/networking/ownership/),
[RPC Messages](https://sbox.game/dev/doc/networking/rpc-messages/) and relevant sync docs.
Decide who owns/simulates each object, who validates state changes, who can invoke each
RPC, and what a late joiner needs. Destination is distinct from caller permission.
Check proxy behavior, ownership transfer and disconnect handling. Verify significant
networked behavior with host/client instances; one local play session is insufficient.

## UI

Follow project Razor/PanelComponent patterns and
[UI](https://sbox.game/dev/doc/ui/) / [Styling Panels](https://sbox.game/dev/doc/ui/styling-panels/).
Look up supported style properties rather than assuming full browser CSS. Inspect panel
tree, computed styles and input behavior. For appearance changes, follow
[visual verification](visuals.md) and inspect the rendered UI. Verify binding/update
behavior as well as appearance.

## Assets and rendering

Resolve asset paths from local/native search. Confirm mount/package dependencies and
resource types; names guessed from other games are not evidence. Distinguish compiled
Cloud references from runtime mounting using
[Cloud Assets](https://sbox.game/dev/doc/assets/resources/cloud-assets/).
Use first-party shader/material samples matching the installed engine for shader work.
Verify visual changes in the relevant camera/rendered scene using
[visual verification](visuals.md).

## Validation and debugging

Capture baseline compiler status/log cursor; edit; observe the new build finish; read
diagnostics; then exercise the changed behavior. Compiler success fields may be null or
describe an earlier build. Resolve that ambiguity before claiming success. Inspect
truncation/counts instead of assuming the first diagnostic page is exhaustive.
For visually observable bugs, use the targeted capture-and-comparison workflow in
[visual verification](visuals.md); purely nonvisual fixes do not need screenshots.

Use existing tests or add focused behavior tests when useful. The
[Unit Tests](https://sbox.game/dev/doc/code/advanced-topics/unit-tests/) workflow supports
generated test projects, with engine initialization needed for engine-dependent cases.
Use [Hotloading](https://sbox.game/dev/doc/code/advanced-topics/hotloading/) when an edit
behaves differently from a clean start. Record reproduction, expected/actual result and
evidence; avoid repeated reloads that erase the only useful diagnostic context.
