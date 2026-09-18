# Adjustable grass render range

The user accepted the tall meadow appearance and requested a farther, adjustable
render range. The working-tree menu exposes **Graphics → Grass range (m)** for
the local player. Values from 0 to 128 apply immediately; 0 disables grass and
64 is the initial default. Menu changes last for the scene session. The same
VoxelManager property can be authored in the scene inspector under Terrain Visuals.

Nearby density and tuft shape stay unchanged. Beyond 32 metres, the population
thins with inverse squared distance; the last quarter of the selected distance
fades to zero. Retained distant tufts compensate for this thinning in their size
fade. Published coarser terrain supplies roots past the LOD0 boundary. Area
weighting prevents the fixed 16-sample triangle budget from introducing another
arbitrary density drop at each coarse level. Transitions and inactive regions
remain excluded. No terrain streaming radius, collision, network state, root
buffer capacity or per-tuft geometry count changes.

## Fixed qualification

GRASS-RANGE-001/v1 in the ledger was defined before running. The 32 m control and
64 m expanded-option figure-eights use the exact previous meadow route, saved
world revision 3989/pages 873, camera, viewport and warmup/drain settings.
Both compare with the accepted meadow run `f06cc54d7a21461ab5f94a05a8a865d2`.
The setting variation is intentional; all other workload parameters stay fixed.

Existing 10% FPS/frame-tail/memory/allocation limits, zero exceptions/collision
failures/overflow, settled streaming, and +0.3 ms standing GPU target apply.
128 m receives visual and capacity checks, not a general performance guarantee.
Input stays enabled and the client stays visible throughout qualification.

`preview-64m.png` was captured during hot reload and arrival at the origin; the
terrain had not settled, so it is a preview only. Final cold captures and full
performance evidence determine acceptance. The uncommitted terrain-fade work is
held unchanged and excluded from this task. No authored scene changes are saved.

## Results

The initial comparisons failed frame-tail limits; one64m attempt also rendered
at4154x2294 because the editor applied its1.5DPI scale to the requested dimensions.
These failures remain in the ledger and raw evidence. Corrected runs rendered
the required2769x1529. A contemporaneous accepted-source control reproduced the
historical frame-tail shift even without the range change.

Against that same-environment control, both corrected32m and64m candidates pass
all unchanged gates. At64m moving FPS falls8.21%, standing FPS2.20%, standingp99
rises6.61%, and standing GPU rises0.04510ms. At32m moving FPS falls2.87%, while
standing FPS improves6.12%. Memory, allocations, timed exceptions, collision,
streaming and recorded capacity checks pass. See the two
`comparison-accepted-vs-corrected*.json` files and the ledger for exact values.

The subsequent wind task completed native fixed-camera range captures in the
preserved world3991; see [wind range evidence](../GrassWind/range-diagnostics.json).
Grass is absent at0m; overhead40m sees grass at64m but not32m; overhead80m sees
grass at128m but not64m; the128m endpoint has no central grass. These are appearance
checks on the newer world, not additional3989 performance comparisons.

Windows Computer Use was stopped with physical Escape during both attempts at
the menu check. Actual menu typing/clicking, invalid-input rejection and reopen
verification remain unverified; its UI file remains uncommitted. Native property changes and screenshots do not
replace those checks. Commit/push was left pending at each interruption.
