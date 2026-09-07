---
name: sbox
description: Build and debug Facepunch s&box C# games and editor tools using installed API evidence, official docs, and native MCP. Use for s&box scripting, scenes, networking, UI, and assets; not generic sandboxes or Garry's Mod Lua.
---

# s&box

## Working loop

- Reuse the established project and cached evidence. The helper handles freshness silently;
  do not spend model calls checking maintenance age or narrating routine refreshes.
- Establish the intended `.sbproj` and game/editor/test context when unknown. The live
  editor project can differ from the working directory. Default engine:
  `C:\Program Files (x86)\Steam\steamapps\common\sbox`.
- Resolve uncertain APIs before coding. Prefer installed evidence; online schema tracks
  staging. XML can include internals and omit members: neither presence nor absence
  establishes game-code accessibility. Check overloads and whitelist where relevant.
- Integrate with the project's actual components, scenes, references and assets. Verify
  the latest edit through engine diagnostics and relevant runtime behavior when available;
  distinguish completed checks from pending ones.
- For visual outcomes, inspect targeted MCP screenshots before claiming visual success.
  Follow [visual verification](references/visuals.md); capture only when it resolves a question.

## Load only what the task needs

| Need | Reference |
|---|---|
| API/docs lookup, cache refresh, source locations | [data.md](references/data.md) |
| Lifecycle, networking, UI, assets, debugging | Relevant section of [workflows.md](references/workflows.md) |
| Live editor inspection, mutation, recovery, MCP extension | [mcp.md](references/mcp.md) |
| User requests an update check or refresh | [maintenance.md](references/maintenance.md) |

For known APIs and a narrow edit, work directly. Do not inventory the installation, load
all references, download corpora, or repeat settled research on every invocation.

## Quick lookup

Resolve `scripts/sbox_data.py` relative to this skill. Python 3.10+, no extra packages.

```text
python scripts/sbox_data.py xml "M:Sandbox.Component.OnUpdate" --exact
python scripts/sbox_data.py api "Sandbox.GameObject.Clone" --limit 5
python scripts/sbox_data.py docs "ownership"
```

Search first, then inspect an exact `DocId`; use `--details` for full search evidence.
The script checks a local timer per lookup and refreshes at most every eight hours.
Maintenance logs stay on disk; normal output contains only the requested evidence.
The helper is not a live MCP client or a complete inheritance graph. If live tools are
unavailable, continue local work and identify the remaining editor check.
