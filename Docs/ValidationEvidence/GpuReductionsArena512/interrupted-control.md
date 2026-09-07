# Interrupted size-restored control and shutdown observation

The 32 MiB control in PID19608 has no saved result and is not a pass. The log
records its normal start and later drain, followed by no performance result.
PID42868 started separately at 05:35:49 UTC; this was not an agent-issued restart.
The reason is unknown. The current process also logged camera movement outside
agent actions. An uninterrupted testing window was requested before further live
mutation.

```text
2026/09/07 01:34:03.4540 [VoxelWorld] performance.test.begin task="GPU-MESHING-512-001/v1-arena32-cold-control" revision="aab4bc8-arena32-cold-control" loops=1 speed=2500 distance=50000 center=[0,0,0]
2026/09/07 01:36:05.4369 [VoxelWorld] gpu.geometry.trimmed emptyTrailingArenas=1 remainingArenas=14
```

A separate shutdown issue predates this interruption. The previous PID41496 was
asked to close normally after play stopped. Its native Sentry event is fatal at
2026-09-07T05:28:49.915389Z; the minidump exception stream reports access violation
0xc0000005, fault address in tier0.dll at module offset0x5d7c4. This identifies the
fault module, not its root cause or the responsible caller. No claim is made that
it is a meshing, shader parser or arena-capacity failure. Reports/attachments had
already been transferred out of their directories; the local Sentry envelope
provided the event and minidump evidence. Private machine metadata is not copied.

The initial unchanged-marker statement was incorrect because a mixed PowerShell
table suppressed the displayed property. The actual marker is
2026-09-07T05:28:49.916354Z. It precedes PID19608 and did not advance during its
successful cold M1 run or subsequent interrupted control. Cold shader loading and
runtime geometry were exercised; shutdown stability remains separately unresolved.

The new editor log at05:35:55 UTC reports that the MCP server could not bind
port7269 because an existing registration conflicted. This accounts for the
unavailable automation endpoint after the overlapping launch. It does not explain
who initiated that launch or why the old process stopped before saving its result.
Further automated validation needs the endpoint restored in an uninterrupted
editor session.

The user subsequently confirmed the restart was manual. The interrupted control
is therefore not evidence of a process crash or arena-capacity regression. Resume
with an orderly endpoint recovery and a fresh32/24 MiB pair in one editor process;
the separate earlier shutdown access violation remains recorded independently.
