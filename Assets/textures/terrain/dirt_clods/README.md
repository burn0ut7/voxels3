# Dirt clod prototype

Unaccepted BU artwork derived from the existing CC0 dirt scan. Source color,
roughness, AO and fine normal detail remain from `../dirt`.

The source height central90% spans only0.267 of its normalized range.
`dirt_clod_height.png` remaps source p1=.22626078 to p99=.63300526, clamps,
then applies smoothstep. It is a16-bit linear height, deliberately reshaped
for larger clumps, not physically calibrated scan depth. The full shader
interval is80mm at1m tiling. The matching normal uses the remapped height's
box mip2 gradient plus bounded source fine-normal residual.

`08cm-tile1m-manifest.json` records source and derived hashes, remap and bake
parameters. The experiment recipe is `.codex/terrain-relief/bake_bu_dirt_normal.py`.
Visual and performance acceptance are pending.
