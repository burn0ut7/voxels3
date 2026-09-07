# Visual verification

Use screenshots as evidence for UI appearance, rendering, lighting/materials, scene
composition and visually observable bugs. When the success criterion is visual, inspect
the relevant rendered result before declaring success. Compiler output or property values
alone cannot establish appearance. Screenshots can also help locate or understand an
unclear symptom; they are not required for every bug or code edit.

## Focus the capture

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
