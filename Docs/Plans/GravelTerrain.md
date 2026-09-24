# Separate gravel terrain

Gravel is material ID7, appended to the catalog with its own rough texture and
parallax treatment. It currently has no automatic world-generation placement.
The user rejected the initial noise-based upland patches; their CPU/GPU recipe
and bindings have been removed entirely. No river, beach or slope placement is
assumed as a replacement. Deliberate placement is outside this correction.

The current generator emits zero gravel weight. Existing grass, dirt, stone,
sand, snow, marsh and placed dirt assignment rules remain in control. Gravel
rendering support stays available, but no gameplay tool currently places it.
Density, collision, water and saved field formats are unchanged.

## Rendering contract

The existing four8-bit weights keep their precision. A fifth explicit gravel
weight occupies one float at byte28; sand remains the remainder of all five.
The shared CPU/regular/transition/grass vertex layout becomes32bytes. A third
plane in the existing edge-flags buffer transports gravel without adding a GPU
buffer binding. Quantization preserves the total and cannot introduce sand into
a mixture that had none. Placed dirt suppresses every procedural weight.

This costs4bytes per terrain vertex (14.3% more bytes,12.5% fewer vertices in each
fixed-size arena) and4bytes per scratch edge slot. Arena byte budgets remain
unchanged. Reducing every material to5-bit precision was rejected; deriving
gravel during drawing was rejected because canonical material queries and
generated geometry must agree. No per-material draw or per-frame allocation.

The generated gravel color and height art share a0.8m tile and35mm height
interval. The existing bounded triplanar relief ray traces gravel together with
neighboring materials, fading between8–16m and near grazing angles. Gravel
normals derive from that same filtered height at the hit position. Constant
roughness0.95 and height-based cavity shading keep the surface matte. No geometry
displacement or collision detail is claimed. Generated map registration, wrap
seams and apparent grain scale require in-world visual acceptance.

## Qualification

The editor endpoint was unavailable during initial preparation. Shader cold
compile, production material/placement checks, moving close views, LOD seams and
the unchanged figure-eight comparison remain required. See the GRAVEL-001 correction in the
validation ledger. Implementation is a candidate until those checks pass.

Scalar TEXCOORD layout is supported by the engine's
[VertexLayout implementation](https://github.com/Facepunch/sbox-public/blob/master/engine/Sandbox.Engine/Systems/Render/VertexLayout.cs),
which maps float to R32_FLOAT. This upstream evidence supports the chosen field,
but does not replace the installed-engine cold-start check.
