"""Offline analysis of an existing capture; never starts or changes the game.

Usage: python analyze.py CAPTURE_JSON RESULTS_JSONL OUTPUT_JSON
The fixed windows isolate the supplied 2026-09-07 capture, not acceptance tests.
CPU weights are sampled attribution estimates; allocation tick type labels are
threshold-crossing types, not exact allocation stacks or byte ownership.
"""
import collections
import hashlib
import itertools
import json
import pathlib
import sys


def digest(data):
	return hashlib.sha256(data).hexdigest()


def ranked(counter, limit=60):
	return [{"name": name, "value": round(value, 6)}
			for name, value in sorted(counter.items(), key=lambda item: (-item[1], item[0]))[:limit]]


capture_path, results_path, output_path = map(pathlib.Path, sys.argv[1:])
raw = capture_path.read_bytes()
profile = json.loads(raw)
assert profile["meta"]["sampleUnits"]["threadCPUDelta"] == "ns"
run_id = "aa45c1cda4e847aba3284cac926960e7"
matching = [line for line in results_path.read_bytes().splitlines()
			if line.strip() and json.loads(line).get("runId") == run_id]
assert len(matching) == 1
result = json.loads(matching[0])
output = {
	"capturePath": str(capture_path), "captureSha256": digest(raw),
	"captureBytes": len(raw), "meta": profile["meta"],
	"resultPath": str(results_path), "resultLineSha256": digest(matching[0]),
	"runId": run_id,
	"method": "Cumulative timeDeltas in ms; threadCPUDelta / 1e6 weights; "
			  "unique function names per sample for inclusive attribution. "
			  "Nested rows overlap. No per-frame times inferred from sampling.",
	"threads": [], "mainWindows": {}, "gcEvents": [], "heapEvents": [],
	"floatArrayTicks": [],
	"resultExtract": {key: result[key] for key in [
		"schemaVersion", "runId", "capturedAtUtc", "source", "test", "world",
		"frame", "runtime", "stationary", "memory", "streaming", "hierarchy",
		"meshing", "submission", "visibility", "profiler"]},
}
allocations = collections.Counter()
allocation_counts = collections.Counter()
marker_types = collections.Counter()
for thread in profile["threads"]:
	stacks = []
	for index, frame in enumerate(thread["stackTable"]["frame"]):
		function = thread["frameTable"]["func"][frame]
		name = thread["stringArray"][thread["funcTable"]["name"][function]]
		prefix = thread["stackTable"]["prefix"][index]
		assert prefix is None or prefix < index
		stacks.append((stacks[prefix] if prefix is not None else ()) + (name,))
	samples = thread["samples"]
	times = list(itertools.accumulate(samples["timeDeltas"]))
	inclusive = collections.Counter()
	leaf = collections.Counter()
	for stack, weight in zip(samples["stack"], samples["threadCPUDelta"]):
		if stack is None:
			continue
		weight = (weight or 0) / 1e6
		for name in set(stacks[stack]):
			inclusive[name] += weight
		leaf[stacks[stack][-1]] += weight
	output["threads"].append({
		"name": thread["name"], "tid": thread["tid"],
		"samples": samples["length"], "lastSampleMs": times[-1] if times else None,
		"weightedCpuMs": sum(x or 0 for x in samples["threadCPUDelta"]) / 1e6,
		"inclusiveMsTop": ranked(inclusive), "leafMsTop": ranked(leaf, 15),
	})
	if thread["tid"] == "22784":
		for label, lower, upper in [("preMovementWithEditorInteraction", 700, 3500),
									("movingInterior", 5000, 25000)]:
			weights = collections.Counter()
			count = 0
			total = 0
			for stack, weight, time in zip(samples["stack"], samples["threadCPUDelta"], times):
				if stack is None or not lower <= time < upper:
					continue
				count += 1
				weight = (weight or 0) / 1e6
				total += weight
				for name in set(stacks[stack]):
					weights[name] += weight
			relevant = {name: value for name, value in weights.items()
						if name.startswith(("Voxel", "Gpu", "ProceduralTerrain",
											"Sandbox.", "Editor."))}
			output["mainWindows"][label] = {
				"startMs": lower, "endMs": upper, "samples": count,
				"weightedCpuMs": total,
				"inclusiveMs": ranked(collections.Counter(relevant), 100),
			}
	markers = thread["markers"]
	for index, data in enumerate(markers["data"]):
		if not data:
			continue
		kind = data["type"]
		marker_types[kind] += 1
		event = {"thread": thread["name"], "startMs": markers["startTime"][index],
				 "endMs": markers["endTime"][index], "data": data}
		if kind == "GCMajor":
			output["gcEvents"].append(event)
		elif kind == "dotnet.gc.heap_stats":
			output["heapEvents"].append(event)
		elif kind == "GCMinor":
			allocations[data["typeName"]] += data["allocationAmount"]
			allocation_counts[data["typeName"]] += 1
			if data["typeName"] == "System.Single[]":
				output["floatArrayTicks"].append(event)
output["markerTypes"] = dict(marker_types)
output["allocationTickBytes"] = sum(allocations.values())
output["allocationThresholdTypeWeights"] = [
	{"type": name, "tickBytes": amount, "ticks": allocation_counts[name]}
	for name, amount in allocations.most_common()]
output_path.write_text(json.dumps(output, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"output": str(output_path), "captureSha256": output["captureSha256"],
				  "resultLineSha256": output["resultLineSha256"],
				  "gcDurationMs": sum(e["endMs"] - e["startMs"] for e in output["gcEvents"]),
				  "allocationTickBytes": output["allocationTickBytes"]}))
