# Visual verification

Use screenshots as evidence for UI appearance, rendering, lighting/materials, scene
composition and visually observable bugs. When the success criterion is visual, inspect
the relevant rendered result before declaring success. Compiler output or property values
alone cannot establish appearance. Screenshots can also help locate or understand an
unclear symptom; they are not required for every bug or code edit.

## Focus the capture

### Use the native s&box MCP server

When asked to look at the game or take a game screenshot, capture the rendered view
through the embedded s&box MCP server. Do not use desktop/computer-use capture as a
substitute: it can return a foreground browser or another game even when targeting
the s&box window. Do not require the user to foreground s&box for a native capture.

1. Call `editor_status` to confirm the intended project and play/edit state.
2. Discover the current schema with `search_tools` using `camera_screenshot`.
3. Call `call_tool` with name `camera_screenshot` and arguments such as
   `{"width":1600,"height":900,"includeUi":false}`. Omit `camera` for the main
   scene camera, or use a discovered camera ID when a different view is intended.
4. Inspect the returned image itself and confirm it depicts the intended game view.
   A successful tool result alone is not visual evidence.

For the editor scene view, use `editor_camera_screenshot`. If the user is viewing a
detached runtime camera, discover whether the project supplies an appropriate tool
such as `ejected_camera_screenshot`; do not assume the main camera matches that view.
See [native MCP capture tools](mcp.md#screenshots) for view selection.

Screenshot permission authorizes capture of the existing view, not camera movement,
player teleportation, application activation, starting/stopping play, or terrain edits.
Respect the user's computer-control restrictions. If native capture is unavailable,
report the specific blocker and continue file work; do not silently switch to desktop
capture or claim a different application's image represents the game.

Define what the image should establish: clipping, visibility, alignment, missing material,
incorrect lighting, or another concrete symptom. Reproduce the relevant state and choose
the right play/edit mode, camera, panel, resolution and interaction state. Read targeted
logs, panel properties or scene state first when they can locate the issue cheaply.

Prefer a panel crop with enough surrounding context for layout, shadows or overlap. Use
a camera frame when composition or the wider scene matters. Choose the smallest useful
resolution; preserve the target viewport/aspect ratio when testing responsive layout or
resolution-dependent artifacts. Too small a capture can hide the defect.

## Capture at useful checkpoints

Reuse an adequate user-provided or earlier image. For a visual bug, capture the reproduced
problem if no usable baseline exists, then inspect a comparable result after the relevant
fix is ready. Additional images should answer an unresolved question, test another
necessary state, or verify a new change. Do not capture after every tool call, code edit,
compile, or timer tick. Stop when the visual criterion is established.

Inspect the returned image, not just the tool's success flag or saved path. Summarize the
finding briefly and retain the image reference for reuse rather than reloading or
reattaching identical images. Pair images with runtime/state checks for input, animation,
networking or timing; a still image alone cannot establish those behaviors.

Discover the screenshot tools in [mcp.md](mcp.md). If the required live view or capture
tool is unavailable, continue other checks and explicitly leave visual verification
pending. Do not claim screenshots were inspected when only metadata was read.
