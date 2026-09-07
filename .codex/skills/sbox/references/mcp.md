# Native editor MCP

Verified against official docs, installed source/XML on build `26.09.01c`, and upstream
commit `9de061bb0fe2dc73ff29a134a0041928f2a47166` on 6 September 2026. This is not a record
of a successful live connection. Rediscover capabilities on each relevant session.

The [official MCP page](https://sbox.game/dev/doc/editor/mcp-server/) describes the native
endpoint, normally `http://127.0.0.1:7269/mcp`. Use **Editor → Preferences → MCP Server →
Copy Url** for the configured address. Keep the connection local. If the server is not
available to Codex, do file and documentation work and explain the missing live checks.
Do not assume a skill installation configures a connector.

## Discovery and targets

Native entry points include `editor_status`, `list_toolsets`, `describe_toolset`,
`search_tools`, `call_tool`, `call_tools`, and `read_console`. Use discovered input schemas.
Search with a few specific terms: the native search matches every keyword. Tools can
change after hotload. Inspect the project root and editing scene before mutation;
upstream `editor_status` mixes game scene identity with editing-session dirty state.
Use `list_scenes` and target inspection to resolve that ambiguity.

Native installed tool source is in `addons/tools/Code/Mcp`: `Scene.cs`, `SceneFiles.cs`,
`Components.cs`, `AssetSystem.cs`, `Packages.cs`, `EditorTools.cs`, `Play.cs`, and `Ui.cs`.
Discover exact operations before assuming an extension is needed. `compile_status` exists
in the inspected installation, with counts and up to 50 diagnostics per compiler.
`get_component_type` describes inspector properties; it is not a general C# symbol index.

Use returned GUIDs for objects/components and returned resource paths for assets. Native
coordinates use inches, +X forward, +Y left, +Z up; angles use degrees. Follow the schema's
comma-string vector/angle representation rather than inventing a JSON object shape.

## Screenshots

Installed `Scene.cs` and `Ui.cs` on build `26.09.01c` provide the following image tools.
Discover their current schemas before use; this source inspection is not a live capture test.

| Tool | Appropriate view |
|---|---|
| `ui_screenshot` | One panel cropped to its screen rectangle; use a path from `ui_panel_dump` and padding for shadows/overlap. Requires the running scene camera. |
| `camera_screenshot` | A scene camera's rendered image; choose a camera ID, width, height and `includeUi` as needed. |
| `editor_camera_screenshot` | Editor scene camera view; not the player's view or full editor application UI. |

The inspected camera tools default to 1280×720 and accept dimensions from 16 to 4096;
choose dimensions for the question, not the maximum. UI probes can return images too:
set their `screenshot` argument false when only numeric results are needed.
Use [visual verification](visuals.md) for capture timing, image inspection and context economy.

## Mutation and recovery

Inspect before editing, preserve an undo path appropriate to the operation, read back
the result and save when persistence is part of the request. Check actual tool behavior:
not every tool implements undo or saves. A read-only annotation is a hint, not authority.

At the pinned upstream revision, a call queued behind a blocked main thread can return a
timeout and still execute later. **Do not blindly retry a timed-out mutation.** Reconcile
scene/asset state and logs first; if uncertain, stop that operation and report uncertainty.
`call_tools` executes sequentially and is not transactional. Inspect every result block;
earlier edits remain if a later call fails. These caveats come from
[ToolRegistry](https://github.com/Facepunch/sbox-public/blob/9de061bb0fe2dc73ff29a134a0041928f2a47166/engine/Sandbox.Tools/Mcp/ToolRegistry.cs)
and [TopLevelTools](https://github.com/Facepunch/sbox-public/blob/9de061bb0fe2dc73ff29a134a0041928f2a47166/engine/Sandbox.Tools/Mcp/TopLevelTools.cs).

## Extending the native server

When explicitly implementing a missing capability, use the installed `Editor.Mcp`
attributes and schemas. Prefer stable typed results with totals, paging and source/build
identity. Verify main-thread and async behavior against current engine source. Keep reads
separate from writes; test discovery, error behavior, hotload and readback in the target
editor. Avoid a replacement transport unless native support has a demonstrated gap.

Potential extensions: full installed type/member inspection, effective input/settings,
package/content resolution and disk-versus-compiled revision evidence. These are design
targets, not tools bundled with this skill. [fobiat/sbox-skill](https://github.com/fobiat/sbox-skill)
is a community design reference; review source/version compatibility before adoption.
