# Stationary shoreline flicker investigation

Status: unresolved; no runtime change retained.

The user reports flicker while stationary and rotating the camera. Coverage
inspection found no overlapping terrain parents or missing/extra seams.
The original far-bank observation was interrupted by the concurrent sand task's
saved-world restart. The complete experiment is recorded under
WATER-SHORE-FLICKER-001/v1 and /v2 in [the ledger](../../ValidationResults.md).

## Resident-bank comparison

Camera(-1800,-1700,800), pitch35, yaw47/46.8/47.2/47, FOV60,1600x900.
All water generation was settled:1152chunks,1072published,8187submitted vertices.

- `baseline-0..3.png`: original water shader, canonical sea height.
- `candidate-0..3.png`: rejected rendering-only0.125-unit downward offset,
  with1.2seconds settling after each camera change.

The contact edge changes with angle. Repeating the exact first angle produces
pixel-identical images within both sequences. The offset changes6951pixels in
the first view but does not establish elimination of the reported flicker.
It was withdrawn, and the original shader restored byte-for-byte and compiled.
No geometry count, generation, medium query, or terrain edit was changed.

These images come from CameraComponent.RenderToBitmap and are not a recording
of the displayed viewport. Actual viewport antialiasing and the original far
bank still require inspection. No performance improvement or completed fix is
claimed. No figure-eight was run for the withdrawn candidate.
