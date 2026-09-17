## Sand and seeded material regions (2026-09-15, qualification pending)

Sand is stable catalog ID6, with warm pale-yellow checker colors. Generation
selects actual material nodes; water/air and explicit placed dirt keep priority.
Sand changes material identity only, without changing density or collision.

ProceduralSand owns the immutable sand recipe. Relative to the world's sea
level, exterior heights from -512 through64 units receive a96-unit sand layer.
The composed exterior includes river beds/banks, so the same rule covers both
ocean shorelines and the current sea-plane rivers. Seeded patches extend to
height96 on non-mountain ground. Sand is absent outside that water-level band:
there are no unrestricted inland patches. Height is a shoreline eligibility
proxy, not a computed horizontal distance to water; very flat low inland terrain
can share this band. Deep ocean floors below-512 retain their prior materials.

Occasional buried sand is restricted to the same -512..64 exterior-height band,
at surface-relative depths144..384. The original soil occupies the96..144 gap;
buried deposits replace stone. There are no arbitrary deep underground deposits.
Bounds are in world units (base cell16), and exact inclusive/exclusive tests live
in ProceduralSand. Player additions above the original exterior remain dirt.

MaterialSpawnRegion is the material-independent foundation used by both patch
rules. Explicit seed, salt, region dimensions and probability determine one
ellipsoidal deposit per eligible region. Floor partitions negative coordinates.
Two integer hashes choose occupancy, a jittered center, and radius; there are no
global RNG calls, traversal dependencies, caches, allocations or neighbor scans.
Centers0.4..0.6 and radii0.22..0.34 of region extent keep deposits inside their
own region. Region edges are therefore intentionally deposit-free; this is not
a vein generator. Column mode gives surface footprints; volumetric mode gives
buried ellipsoids. Sand surface regions are1024x1024, probability.35/salt17011;
buried regions are768x768x384, probability.22/salt29137. Probability controls
region activation, not the final fraction of sand blocks. Material/depth/water
eligibility remains owned by the calling recipe. Future ores can reuse this
sampler with their own eligibility; no ore catalog entries or rules are added.

CPU logical queries and the generation-only HLSL mirror consume identical
parameters. Every regular/transition crossing still reconstructs the existing
eight material contributors. Sand contributes to normalization while the four
existing weights are stored explicitly; sand is their remaining weight to one.
Packing preserves the rounded explicit-weight sum, preventing false sand from
independent rounding. Dirt overrides attenuate all inherited weights through
the existing interpolation, including implicit sand. The draw shader blends
the fifth palette entry without any region/noise/height evaluation. Vertex and
edge buffer sizes remain unchanged (28-byte vertices); palette grows32 bytes.

This is immutable derived material generation. Workers only read recipe inputs;
the mesher owns engine-thread palette lifetime and generated geometry. A fresh
world/Play rebuild regenerates the material derivatives. No separate mutable
material state or save format is introduced. Like the prior snow rule, existing
worlds adopt the new derived palette/assignment while retaining density and
explicit dirt edits. Transfer protocol4 rejects peers running the earlier material
recipe through the existing manifest version check; save identity stays unchanged.
Multiplayer parity needs qualification and is not established by matching source.

Alternatives: per-node independent random rolls cause speckle; coherent regions
provide grouped deposits. A general biome/ore graph exceeds this slice. Extra
vertex channels would grow all geometry; an implicit fifth normalized weight
fits the existing payload. This representation supports five blended solids;
future additional types must revisit encoding explicitly, not overload an ID.

Budget: at most two hashes per eligible region lookup, eight contributors per
crossing, no extra terrain/river queries, no recurring CPU allocation. Shore
nodes short-circuit without region hashes. These are operation/storage bounds,
not measured FPS claims. Canonical figure-eight, shader cold start, shoreline
appearance, material queries, edits and LOD correctness remain acceptance gates.
