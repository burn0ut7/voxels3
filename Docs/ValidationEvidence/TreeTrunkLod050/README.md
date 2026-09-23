# Tree trunk LOD repair 050

The active dense oak retains a 158-triangle opaque trunk in its distant model. The whole-tree image cannot reconstruct trunk surfaces hidden behind foliage in the original orthographic capture. The derived trunk fills those exposed areas without changing the source meshes, collision, authored placement, wind controller, or 12/16 metre handoff.

The distant image uses one perspective correction beyond six source diameters; its original three corrections remain nearby. This keeps the measured forest GPU cost within the existing budget. The 18 legacy catalog trees retain their existing representation; they were not rebaked or requalified by this task.

## Results

| Fixed scenario | Original control | Repair |
| --- | ---: | ---: |
| Figure-eight moving FPS | 375.2091 | 379.27835 |
| Moving p95 / p99, ms | 4.2900 / 6.3066 | 4.2288 / 6.1931 |
| Standing FPS | 305.19952 | 306.0795 |
| Standing p95 / p99, ms | 3.8838 / 6.2196 | 3.7857 / 6.1189 |
| 1024-tree affected GPU scopes, ms | 4.488652 (accepted049) | 4.468362 |

The recorded cold candidate is `0003a2084edc46e6aec20b09ddf620d0`; the fresh original control is `9dc2c25f59414bc784c0eb59ed648b06`. Workload, resolution, seed, route and acceptance gates were unchanged. Source remained stable in each run; collision settled to4913 ready with no exceptions or failures. All current-control gates pass. The old049 frame-rate baseline is faster than both implementations in the current environment; this loss reproduces with the original code.

The hot comparison is retained as a failure: moving-0.299%, standing-5.0508%, process mean+20.597%. Editor resource/compilation history materially changes resident memory: the original process also reached18.57GB after explicit shader compiles. The final repaired editor, restarted after compilation, recovered to10,921MiB mean in the settled world. Do not interpret the cold comparison's negative37% process-memory difference as an optimization. The cold candidate also passes the historical049 memory gates.

Visual evidence covers the reported low-angle view, all eight azimuths at17/25/50m, the118/120/125m refinement boundary, and a clear elevated200m sweep. The handoff sequence10/15/17/15/11m produced Detailed/Detailed/Distant/Distant/Detailed. Existing differences in distant leaf/branch detail and shadow shape remain outside this trunk repair.

One final editor startup stalled before loading the scene. The identical-source retry succeeded, with five successful shader compiles, successful managed compilation, unchanged Sentry marker, no tree fallback or shader/parser/managed errors, and the known stock missing-resource warnings retained. See the ledger for the full sequence.

## Evidence and publication scope

- `final-cold-far-35.png` and `before-far-35.png`: repaired and original reported view.
- `restored-user-view.png`: playable view returned to the user's position.
- `final-trunk-contact-*.png`: final far/detailed trunk pairs.
- `final-boundary-contact-*.png`: original/repaired/detailed distance comparisons. The200m low-camera sheet retains terrain-obscured views; `final-distant-contact-200.png` supplies the declared clear higher viewpoint.
- `observations.zip`: camera/LOD records, source identities, raw performance runs, comparison calculations, compiler responses, and startup logs. Earlier filenames such as `final-canonical` are retained rejected candidates; the accepted run is explicitly identified above.
- Full uncropped sweep images and rejected diagnostic images remain in this local evidence directory.

The pre-existing tree subsystem is untracked in the repository. Publication therefore contains only this task's isolated source/asset delta and evidence; the working project has the repair installed. No unrelated working changes are included.

`final-source.patch.gz` applies to the task-start files identified in `source-provenance.json`. `final-atlas.patch.gz` contains the exact binary atlas/metadata delta. `derived-trunk-assets.zip` contains the new production bake tool and its three derived resources at their project-relative paths. Apply the decompressed patches with `git apply --ignore-space-change --binary`, and extract the derived bundle at the project root. Both source and atlas patch results were verified byte-for-byte against the installed candidate. These deltas require the existing local tree subsystem; they are not a standalone implementation.
