# Quiet automatic and manual maintenance

Load when the user asks about upkeep or a specific freshness problem. Each lookup checks
a small local timer. At most once every eight hours, the script attempts data maintenance
with a ten-second subprocess timeout. Ordinary calls between attempts use the existing
snapshot. No model/API call or separate scheduler is involved. A lock prevents overlapping
automatic refreshes; failures also wait eight hours before retrying. Maintenance is lazy:
it runs on the next lookup after the interval, not while the skill is idle.

The script captures maintenance output. Results remain in `cache/maintenance.json` and
`cache/auto-refresh.json`; partial failures and timeouts are not reported as full success.
Do not load or narrate these records during routine work. Existing data remains usable
when a source fails. A timed-out refresh can have updated some sources; it is not an atomic
update of the whole corpus. Set `SBOX_SKIP_AUTO_REFRESH=1` for explicitly offline use.

From the project directory:

```text
python sbox/scripts/sbox_data.py maintenance
python sbox/scripts/sbox_data.py maintenance --apply
```

These commands bypass the interval. The first checks without replacing evidence. The second applies available data
updates. Both compare installed engine/XML state, the official documentation manifest,
cached Markdown page contents, and the API schema. Results distinguish changed, unchanged
and unavailable sources. Only cached pages are checked; this is not a crawl of every page.
The last result is saved to `cache/maintenance.json`, read only when requested.

If the API download link is unavailable in raw HTML, open
[API Schema](https://sbox.game/api/schema), copy its current download URL and pass
`--api-url "COPIED_URL"`. Report that check as incomplete until resolved; a failed lookup
does not mean the schema is current. Explicit downloads remain available in `data.md`.

Data updates and skill instruction updates are different. New API entries usually need
only a data refresh. When engine/MCP behavior or official guidance changes, inspect the
affected source/docs, revise only the relevant reference or helper, then run:

```text
python -m unittest discover -s sbox/tests
```

Validate the skill after editing instructions. Do not rewrite the entry point for every
engine release, and do not declare instruction compatibility from version numbers alone.
Preserve existing snapshots for sources that cannot be checked or downloaded.
