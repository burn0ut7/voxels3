# Cave noise parity correction

Status: version-13 implementation candidate; qualification in progress, not accepted.

The landform qualification found two deep CPU/GPU density errors, 0.63684464
and 0.16308594. Live production inspector experiments separately associate
these with reciprocal input scaling and contraction in simplex unskewing.
The original cave salts, wavelengths, thresholds, regional mask and depth
limits are preserved in the current source. Matching a particular GPU's
contraction in CPU code is insufficient as a portable correction.

## Proposed boundary

TerrainNoise owns deterministic simplex cell and corner selection. Its input
is world position, integer wavelength and seed; callers no longer divide
positions before sampling. TerrainCaves retains cave composition and controls.
The initial cave-noise interval uses the global clamped range [-1,1]; the old
gradient-only bound did not cover observed boundary jumps. GPU noise
mirrors that contract; no extra buffers, dispatches or mutable state are needed.
This is a numerical field change and needs a new generator identity before
adoption. Preserve the existing version-12 saved world; never silently reinterpret
its edits under a different base field. This document does not authorize a
migration, waive compatibility checks or establish acceptance.

## Integer selection candidate

Use signed 64-bit arithmetic for selection only. Quantize each input coordinate
to a nearest-even integer Q at 256 units per world unit. Multiplication by this
power of two is exact for finite binary32 coordinates within the representable
domain; CPU and GPU rounding semantics must be verified. The maximum introduced
coordinate displacement is 1/512 world unit. This is an internal numerical
contract, not another terrain-tuning control.

With D = wavelength * 256, compute the skewed integer cell using mathematical
floor division (including negative numerators):

- i = floor((4*Qx + Qy + Qz)/(3*D)); similarly for j and k.
- Rank Rx = Qx - i*D, Ry = Qy - j*D, Rz = Qz - k*D using the existing tie order.
- Let S = i+j+k. Form each unskewed offset from the exact integer numerator
  6*(Qaxis - cellAxis*D) + S*D, divided by 6*D only after cancellation.
- Evaluate the original four gradient contributions with float offsets. Hash
  each selected lattice coordinate with the existing low-32-bit hash contract.

The integer cell/rank operations remove the discontinuous decisions from
floating-point contraction and reciprocal substitution. Floating-point density
values still need the existing tolerance checks; this is not a bit-exact density
claim. Input quantization and the retained simplex kernel do not establish a
new claim of mathematical continuity for caves.

The 1048576 constant is an edit-brush limit, not a streaming limit. A 32-bit
fixed-point implementation would therefore introduce an invalid hidden domain
restriction. Before coding, prove all integer intermediates fit for the actual
regular/transition coordinate arithmetic, halo and refinement inputs. Preserve
any existing overflow limitations explicitly rather than treating infinity as
a property of finite coordinates.

## Alternatives and costs

Local precise qualifiers were compiled but produced no NoContraction decoration
and did not correct either failure. A source-level function precision override
was not found in the bounded Slang investigation. Slang fma maps to GLSL.std.450
Fma, which does not guarantee fused precision without suitable decorations.
Neither route is an established portable solution in this engine.

Changing the noise kernel or replacing caves would expand the intended slice.
Uploading a second CPU-generated density representation would change ownership,
streaming and edge refinement. Neither is selected by this candidate.

64-bit integer division can increase generation cost and requires engine/device
support. Verify actual VFX compilation and generated instructions first, then
cold visible startup and real deep/surface density audits. Retain existing
work budgets; measure the canonical figure-eight before acceptance. If supported
compilation or runtime cost fails, preserve the result and reassess this design;
do not add a hidden fallback with different field semantics.

## Required qualification

Define a fixed candidate scenario before execution. Reproduce the two known
failures, their neighboring samples, all regular LODs and all transition faces;
include negative coordinates, near-zero samples and far-coordinate arithmetic.
Repeat cave-envelope, bounds, edits/save identity, matching guest and geometry
checks under the final generator version. Measure shape changes and performance.
No task completion or commit/push follows from the two diagnostic CPU experiments.

Evidence and primary links are in [the validation ledger](../ValidationResults.md)
and [the research library](../smooth_procedural_voxel_terrain_resources.md).

## Implemented candidate and range check

Version13 implements the selection above in CPU/GPU noise. CPU MathF.Round
provides nearest-even rounding. The inspected Slang round emitted GLSL Round,
so GPU quantization explicitly uses floor, fractional comparison and the low
integer bit for halfway values. The final inspected transition payload declares Int64 and contains56 signed
integer divisions; cold visible startup and deep audits are recorded in
INTEGER-CAVE-013/v1. One near-zero sign mismatch remains, so those observations
do not establish full parity or acceptance.

For the current fixed32-cell chunks, base spacing16 and maximum LOD6, a chunk
is at most32768 world units wide. Even allowing the full signed32-bit chunk
coordinate and a conservative one-chunk halo gives abs(world) < 2^47. Thus
abs(Q) < 2^55, skew numerators < 6*2^55, and D <= 16384*256 = 2^22.
The floor-cell product has magnitude <= 2*abs(Q)+D. Consequently each remainder
has magnitude <= 3*abs(Q)+D, and the largest unskew expression is bounded by
24*2^55+9*2^22 < 2^60, below signed64 limits. Actual cancellation produces
small local offset numerators before conversion to float. Hash casts explicitly
retain low32 bits. This covers the current coordinate/scale envelope, not future
arbitrary cell sizes or an unbounded real-number world. Existing int32 sample
conversions/streaming overflow limitations are not repaired by this slice.

The first candidate uses global [-1,1] simplex bounds. This is conservative but
may increase generation work. Acceptance requires measuring that cost; restore
a tighter interval only with a proof covering cell/rank changes and quantization.

## Tighter bound candidate (version13 field unchanged)

CPU sampling and classification share one simplex-location routine. For each
noise wavelength, compare cell indices and the six corner-order bits at all
eight quantized AABB corners. A selected cell/order is an intersection of linear
half-spaces in Q coordinates: the three skew-floor intervals and the three rank
inequalities. It is convex. Nearest-even coordinate rounding is monotone, so
all quantized interior points lie in the box spanned by quantized endpoints.
Matching corners therefore proves the four contributing lattice vertices stay
fixed throughout the query. If any corner differs, return the global [-1,1]
interval; do not apply a gradient-only bound across the kernel's jumps.

Within one fixed cell/order, bound each contribution's gradient by
(0.6-t)^3*(0.6+7*t), t=radiusSquared in[0,0.6]. Its maximum is below0.164;
four contributions times32 stay below21. Use22 with rounding margin. Add
sqrt(3)/256 world units to the center-to-box radius for both endpoint and center
quantization. Use actual distances from the rounded float center to endpoints,
then convert by wavelength. Add0.0001 noise-unit arithmetic padding and round
final interval endpoints outward. This is a conservative classification change,
not a new field recipe or a claim that the entire retained kernel is continuous.

CAVE-BOUNDS-013/v1 compares the global-bound baseline with this implementation
through normal play restart, the fixed cave teleport, timed state captures and
existing density/mesh audits. It does not replace figure-eight performance gates.

The first eight-lookup implementation reduced total work but worsened sampling
tails in CAVE-BOUNDS-013/v1. The replacement computes the same proof by extrema:
quantize min/max, locate the minimum once, and test upper skew numerators against
(i+1)*3D on each axis. Positive skew coefficients prove the lower cell limits
from the minimum and the upper limits from the maximum. With those indices
fixed, each pairwise remainder order is constant only if its independent min/max
intervals preserve the minimum corner's >= or < relation. Check XY, XZ and YZ.
These are exactly the linear constraints tested by the eight corners, without
repeating floor divisions. The selected field sample still uses the shared
SimplexIndices/LocateSimplex code; this interval calculation is only a bound.

## Near-zero endpoint observation

CAVE-ENDPOINT-013/v1 uses the production column inspector at the remaining
transition mismatch. The CPU value+0.000076293945 repeats at Z-28848 and its
two immediate binary32 neighbors. The next sample above that plateau is negative.
This constrains the CPU crossing to one representable-coordinate interval; it
does not measure the GPU crossing or actual mesh displacement. The prior GPU
value at Z-28848 is negative. No tolerance relaxation or sign snapping is adopted.
The observation used the same world at revision2, so it cannot be presented as
an unchanged revision0 validation run. See the ledger and raw column evidence.
