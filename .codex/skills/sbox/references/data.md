# Data lookup

Use Python 3.10+ with `scripts/sbox_data.py`, resolving that path against this skill directory.
The helper uses only the standard library. Global `--engine PATH` and `--cache PATH`
options precede the command. The default cache is this skill's `cache/` directory.

```text
python scripts/sbox_data.py inventory
python scripts/sbox_data.py status
python scripts/sbox_data.py xml "M:Sandbox.Component.OnUpdate" --exact
python scripts/sbox_data.py api "M:Sandbox.Component.OnUpdate" --exact
python scripts/sbox_data.py api "Sandbox.GameObject.Clone" --limit 5
python scripts/sbox_data.py docs "ownership"
python scripts/sbox_data.py fetch-doc "/dev/doc/networking/ownership.md"
```

`status` reads build/cache metadata without traversing the installation. Reserve
`inventory` for initial exploration or coverage questions, not routine invocations.

`fetch-doc` returns a cache path; read its relevant passages with normal file tools.
It reuses a downloaded page unless `--refresh` is supplied. Search commands default to
five compact results and have `--limit 1..30`, `--offset`, and `--details`.
Exact API lookup accepts a `DocId`
or full name; use `DocId` to distinguish overloads. Preserve the source metadata returned.
Raw API records contain declared members; inherited members and reference IDs are not
expanded by this helper. Use the official parser or follow the referenced type separately.

API/XML searches reuse SQLite snapshots under `cache/`. A cheap script-level timer gates
silent maintenance to once every eight hours; intervening searches do not read the
installed version, enumerate XML files, compare source timestamps, or contact the network.
A missing snapshot builds once. Explicit API downloads invalidate its index; subsequent
lookup builds it once. Snapshot metadata describes the indexed data, not a newly checked
installation. Read [maintenance.md](maintenance.md) only for explicit maintenance or
troubleshooting. A concrete API conflict can justify targeted investigation. Do not
load maintenance logs, check freshness with extra model calls, or narrate normal upkeep.

## Online data

- [llms.txt](https://sbox.game/llms.txt): official document manifest. Run `fetch-doc-index`
  to refresh it, then `docs QUERY`. Retrieve only task-relevant pages initially.
- [API schema](https://sbox.game/api/schema): machine-readable data from latest staging.
  Run `fetch-api`. If the raw HTML does not expose the link, open the schema page in the
  web tool/browser, copy the current **Download api.json** link and run
  `fetch-api --url "COPIED_URL"`. Never manufacture a dated release URL.
- [API reference](https://sbox.game/api): readable symbol pages and API changes.
- [AssemblySchema](https://github.com/Facepunch/Facepunch.AssemblySchema): official parser
  and DLL/XML-to-schema route for a future installed-version index. `Rebuild()` connects
  the schema graph. A downloaded schema is not proof of the installed assembly surface.
- [sbox-public](https://github.com/Facepunch/sbox-public): implementation and examples.
  It contains engine source. Pin a commit for claims; do not run its bootstrap just to
  answer a game scripting question.

## Local data

| Location relative to engine | Useful evidence |
|---|---|
| `.version` | Installed build identity; retain raw fields without guessing a Steam branch |
| `bin/managed/*.xml` and companion DLLs | Installed comments and metadata; XML can include internal types and omit undocumented members |
| `addons/tools/Code/Mcp` | Installed editor tool implementations and parameter behavior |
| `addons/tools/Code/Editor/EditorPreferences/PageMcp.cs` | Native MCP preferences and actual URL display |
| `addons`, `editor` | Shipped C#/Razor/editor examples; some are privileged engine/editor code |
| `templates`, `samples` | Installed project layouts and concrete scene/resource examples |
| `core` | Engine assets, materials, shaders, prefabs; search paths before opening large resources |
| `logs` | Historical evidence; correlate to the current session and edit |
| Requested project | `.sbproj`, source, scenes, prefabs, settings, package references and tests |

Treat the installation as evidence. Make game changes in the intended project; do not
patch shipped engine examples to implement an unrelated game feature. Downloaded package
cache contents are not automatically project dependencies or licensed reusable examples.

## Retrieval interpretation

A zero XML hit means no matching comment, not no such API. A zero staging-schema hit can
reflect a query, inheritance, visibility or version issue. Source existence does not
imply whitelist permission. Prefer live installed metadata and compiler evidence for
availability, official documentation for usage, and version-matched source for behavior.
Record conflicts explicitly instead of merging incompatible snapshots.
