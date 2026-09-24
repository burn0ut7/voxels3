"""One native import bound shared by export and installation validation."""

# Native26.09.15 saturated a22-bit mesh table at4,194,303entries.
# Keep triangle corners below that bound in each model at each LOD.
MAX_LOD_TRIANGLES = 1_350_000
