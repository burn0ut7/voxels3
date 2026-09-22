# Single sprite-leaf test tree and cleanup

2026-09-22: the user requested removing all test-tree placements, importing one
fresh tree using the optimized sprite leaves, and removing unused old trees.

The native editor replaced the100-tree grove with **Sprite Leaf Test Oak**, a
fresh oak_growth_18_open_grown_271828_dense prefab at(-1800,-1800,186.125),
identity rotation/unit scale. The saved basic_example.scene contains exactly one
authored tree. Other scene settings remain unchanged; the native save regenerated
only the player's clothing-object/component IDs. The game is left playing with
the detached camera facing the oak and automatic viewport sizing restored.

## Cleanup

13unused experimental specimens and their exclusive distant-render resources
(838files,10,584,924,600bytes) are outside Assets. The18catalog specimens, active
oak and all shared dependencies remain. No remaining shipping source references
a removed specimen; all catalog model/prefab/export manifests resolve.

**Permanent deletion was rejected by automatic approval review: "blocked by
policy".** The safer completed operation moved the files into
`.codex/unused-tree-archive-20260922`; this is a recovery archive, not reclaimed
disk space. See the [exact inventory](single-tree-cleanup-result.json).

The previous grove is preserved as [compressed scene evidence](single-tree-previous-grove.scene.gz),
and the task-only scene edit is [recorded separately](single-tree-scene.patch).
Scene and renderer changes remain local: the shared tree subsystem/assets were
already untracked, and the preceding100-tree optimization memory qualification
is incomplete. Publishing those unrelated source/assets is outside this cleanup.
This report/evidence can be committed independently without declaring that earlier
optimization accepted or publishing a scene with missing dependencies.

## Verified in the playable world

- One enabled authored tree with complete distant silhouette, textured close-up
  foliage and roots meeting the terrain; native screenshots inspected.
- Close renderer readback: woodLOD0; all four foliage piecesLOD2, the existing
  four-vertex/two-triangle leaf cards. Exported leaf count remains544606.
- Six unchanged-camera frames show leaf movement. Wind/material/branch attachment
  code remains the existing production implementation.
- A native physics ray from(-1900,-1800,250) to(-1700,-1800,250) hits the retained
  Solid trunk ModelCollider at(-1807.86072,-1800,250).
- Native code compilation succeeds with zero errors. Scene/resource reload is
  checked after the concurrent asset rebuild finished.

[Whole tree](single-tree046-verified-full.png),
[wind frame0](single-tree046-verified-wind-0.png),
[wind frame5](single-tree046-verified-wind-5.png),
[renderer/camera/source readback](single-tree046-verified-visual.json).

## New single-tree baseline

TREE-PERFORMANCE-038/v2-single-tree reuses the canonical figure-eight inputs:
seed1337/generator52, engine26.09.15,2769x1529,FOV75,speed2500,distance50000,
one loop, start(-1.6258175,1.2225341,340),grass64m,gameplay radius8,LOD0..5,
4/4halfextents,cells32/base16,ready4913/zero pending warmup,automatic drain and
10s stationary capture. Only the user-requested authored tree workload changes.
Visible interactive editor; existing hot editor after several Play sessions.

| Measurement | Moving | Stationary |
| --- | ---: | ---: |
| Average FPS |405.23|326.73|
| Frame p95(ms)|4.0947|4.1594|
| Frame p99(ms)|6.1022|6.0031|
| Engine GPU average(ms)|6.3732|6.9256|
| Process memory average(bytes)|12299448487|12412217753|
| GPU memory average(bytes)|5232101391|5187267055|
| Managed allocation(bytes/frame)|69243.625|34056.348|

Run02dbf9db2e664b4881344906bf4e1dbb completed19:28:24local; zero timed
exceptions/collision failures,4913ready/zero pending; streaming settled8.9s.
Source and retained asset hashes are unchanged. New-baseline completion and
correctness criteria pass. **This is not an optimization comparison with the
previous100-tree grove, and does not pass its unresolved memory gate.**

[Summary](single-tree046-summary.json), [full result](single-tree046.json.gz),
[preflight](single-tree046-before.json), [asset identity](single-tree046-assets.json).

## Preserved failed setup observations

The first native performance trigger rejected unsettled collision after the
fixed-position reset; no timed run started. The same inputs succeeded after the
production state settled. [Rejected preflight](single-tree046-preflight-rejected.json).

The first visual attempt overlapped another task swapping an8-view shader against
the retained16-view atlas, producing a doubled distant image. It failed inspection
and is not acceptance evidence. That task restored all11backed-up sources/assets
byte-exactly and deferred further changes. Play was restarted before the verified
captures and baseline. [Invalid image](single-tree046-full.png),
[invalid setup readback](single-tree046-visual.json).
