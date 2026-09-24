# Dry Mud Field001 dirt

Source: [Dry Mud Field001](https://polyhaven.com/a/dry_mud_field_001),
Rob Tuytel (photography), Rico Cilliers (processing),
[CC0](https://polyhaven.com/license). Imported2026-09-19.

The BZ dirt appearance was approved on2026-09-19. On2026-09-20 the user
requested removal of the scanned tractor-tread impressions. Active color,
height, roughness, AO and shading-normal maps now use clean patches of this
same scan in the affected areas. The original `nor_gl` file is retained only
as a bake input; it is not the normal texture bound by the terrain material.

Documented tile3m and authored height interval120mm remain unchanged. This
interval is an art choice, not measured scan depth. The untouched BZ height
was EXR red filtered periodically at Gaussian sigma2texels and quantized to
16-bit PNG. Track removal shares patch coordinates/weights across all maps,
continues broad height from surrounding unmasked soil, and transfers donor
clod detail. Macro normals derive from the final quantized height at box mip2.
Fine source-normal residual removes Gaussian8 slopes, caps length at1 and
applies0.5 gain. The manifest records exact donor rectangles, mask, seed,
parameters, input/output hashes and generator hash.

Reproduce with [remove_dirt_tracks.py](../../../../Tools/remove_dirt_tracks.py):

```text
python Tools/remove_dirt_tracks.py .codex/dirt-track-removal/source OUTPUT_DIRECTORY
```

The source directory is the retained untouched BZ snapshot; the tool checks
its height hash and rejects already patched inputs. Outputs are the five
active maps and manifest. Runtime visual results and the unavailable canonical
performance baseline are recorded under DIRT-TRACKS-001/v1 in
[the validation ledger](../../../../Docs/ValidationResults.md).
