# Editor crash log — 7 September 2026

Two captured native dumps have the same failure signature. A manual overlapping
restart also interrupted the final Figure Eight control; the user reports a crash
following that restart. That interrupted control has no saved result and is not
counted as a pass. The available local dump timeline does not independently identify
a third crash at the time of the interrupted control.

| Local time (EDT) | UTC | Source state / observation | Verified dump signature |
| --- | --- | --- | --- |
| 01:28:49 | 05:28:49.915389 | PID41496 closed after stopped M1 play; original scan restored, 24 MiB vertices | Fatal native access violation `0xc0000005`, `tier0.dll + 0x5d7c4` |
| 01:44:38 | 05:44:38.773211 | PID42868 closed during connection recovery; 32 MiB vertices, lean reporting | Fatal native access violation `0xc0000005`, `tier0.dll + 0x5d7c4` |

These identify the faulting module and offset, not the caller or root cause. The
second occurrence rules out a failure exclusive to the 24 MiB constant. It does
not by itself prove an engine defect or exclude other project state. No shader
parser failure is established by these dumps.

## Latest shutdown log excerpt

Times below are local EDT, from the editor log before recovery restart:

```text
2026/09/07 01:43:25.7211 [Reflection] NullStaticReferences: force-clearing readonly field ShadowMapper.ContactShadowCompute (ComputeShader) — consider removing 'readonly'
2026/09/07 01:43:25.8531 [Reflection] NullStaticReferences: force-clearing readonly field ModelLoader.DefaultMaterial (Material) — consider removing 'readonly'
2026/09/07 01:43:25.8531 [Reflection] NullStaticReferences: force-clearing readonly field ModelLoader.FlatNormal (Texture) — consider removing 'readonly'
2026/09/07 01:43:25.8531 [Reflection] NullStaticReferences: force-clearing readonly field PreviewMaterial.Plane (Model) — consider removing 'readonly'
2026/09/07 01:43:25.8531 [Reflection] NullStaticReferences: force-clearing readonly field TileBoundsGrid._lineMaterial (Material) — consider removing 'readonly'
2026/09/07 01:43:26.3705 [engine/ToolFramework2] Shutting down Qt windows.............
2026/09/07 01:43:26.3705 [engine/Engine] Source2Shutdown
2026/09/07 01:43:26.3705 [engine/Engine] ShutdownSource2Logging
```

This is the sequence before the crash report, not a causal stack trace. The access
violation comes from the native minidump exception stream; the log does not name
its responsible caller. Full dumps contain machine/user metadata and are retained
locally by s&box rather than copied into the repository.

## Connection failure after overlapping launch

```text
2026/09/07 01:35:55.9545 [MCP] Couldn't start MCP server on port 7269 (Failed to listen on prefix 'http://localhost:7269/' because it conflicts with an existing registration on the machine.)
```

The user confirmed a manual restart and then crashes. The earlier statement that
the interrupted run was only a manual restart was too strong. It remains incomplete
regardless of cause. The crash marker was also previously misreported as unchanged;
explicit content and minidump checks corrected that claim.

Recovery: the agent restarted the editor after the old process fully exited. The
new editor is responsive, native compilation succeeds, and MCP is available again.
The final matched performance pair and shutdown attribution remain in progress.

## Captured Error dialog, 01:55 EDT

The agent captured the actual dialog through Windows accessibility before
acknowledging its OK button:

```text
Error
[mimalloc] error 11: double free detected
OK
```

The log immediately before this occurrence also records:

```text
2026/09/07 01:55:20.5126 [Interop] Object reference not set to an instance of an object.
System.NullReferenceException: Object reference not set to an instance of an object.
   at Sandbox.ResourceLibrary.GetAll[T]()
   at Editor.EditorMainWindow.GetUnsavedResources()
   at Editor.EditorMainWindow.OnClose()
   at Managed.SourceTools.Exports.Editor_Window_InternalCloseEvent(UInt32 self, IntPtr e)
2026/09/07 01:55:20.5472 [engine/ToolFramework2] Shutting down Qt windows.............
2026/09/07 01:55:20.5472 [engine/Engine] Source2Shutdown
2026/09/07 01:55:20.5472 [engine/Engine] ShutdownSource2Logging
```

This process had previous candidate/hotload history even though C# source was
restored before closing. The double-free message is now directly observed;
its owning allocation is still unknown. A clean original-code process is next.
