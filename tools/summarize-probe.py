"""Summarize live acquisition evidence without labeling unknown aircraft as multiplayer."""
import json
import pathlib
import sys

paths = [pathlib.Path(p) for p in sys.argv[1:]]
if not paths:
    paths = sorted(pathlib.Path("artifacts/identity-probe/logs").glob("*.jsonl"), key=lambda p: p.stat().st_mtime)[-2:]
for path in paths:
    aircraft, identities, labels, cameras, exceptions = {}, {}, {}, [], []
    own, summary, matches, short = None, None, [], []
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        try:
            row = json.loads(line)
        except json.JSONDecodeError:
            continue  # Last line may still be in flight.
        kind, data = row.get("kind"), row.get("data", {})
        if kind == "traffic_discovered" and data.get("kind") in ("Aircraft", "Helicopter"):
            aircraft[data["objectId"]] = {**data, "lastSeen": row["timestamp"]}
        elif kind == "traffic_identity":
            identities.setdefault(data["objectId"], {})[data["field"]] = data["value"]
        elif kind == "smart_camera_target":
            labels[(data["type"], data["description"])] = data
        elif kind == "own_telemetry":
            own = data
        elif kind == "probe_summary":
            summary = {k: v for k, v in data.items() if k != "aircraft"}
        elif kind == "target_search_match":
            matches.append(data)
        elif kind == "simconnect_exception":
            exceptions.append(data)
        elif kind == "discovery_envelope" and data["size"] < (296 if data["definition"] == 2 else 48):
            short.append(data)
        elif kind.startswith("camera_"):
            cameras.append({"kind": kind, **data})
    print(json.dumps({"log": str(path), "aircraft": list(aircraft.values()), "identityFields": identities,
                      "smartLabels": list(labels.values()), "ownLatest": own, "summary": summary,
                      "matches": matches, "exceptions": exceptions[-10:], "shortPackets": short[-10:],
                      "camera": cameras[-15:]}, indent=2))
