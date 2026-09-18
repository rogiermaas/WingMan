import json
import math
import pathlib
import sys

root = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else pathlib.Path("artifacts/bridge-probe/logs")
paths = [root] if root.is_file() else sorted(root.glob("*.jsonl"), key=lambda p: p.stat().st_mtime)[-1:]
for path in paths:
    titles, positions, fields, coherent = {}, {}, {}, {}
    own = None
    counts = {}
    errors = []
    motions = []
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        try:
            row = json.loads(line)
        except json.JSONDecodeError:
            continue
        kind, data = row["kind"], row["data"]
        counts[kind] = counts.get(kind, 0) + 1
        if kind == "traffic_discovered": titles[data["objectId"]] = data
        if kind == "direct_position_result": positions[data["objectId"]] = data
        if kind == "traffic_identity": fields.setdefault(data["objectId"], {})[data["field"]] = data["value"]
        if kind == "own_telemetry": own = data
        if kind == "coherent_traffic" and data["Name"]: coherent[data["TrafficId"]] = data
        if kind in ("coherent_packet_rejected", "simconnect_exception"): errors.append(data)
        if kind == "focus_motion": motions.append(data)
    def distance(lat, lon):
        if not own: return None
        p = own["Position"]
        dlat, dlon = math.radians(lat - p["Latitude"]), math.radians(lon - p["Longitude"])
        a = math.sin(dlat/2)**2 + math.cos(math.radians(lat))*math.cos(math.radians(p["Latitude"]))*math.sin(dlon/2)**2
        return round(3440.065*2*math.asin(min(1, math.sqrt(a))), 3)
    candidates = []
    for oid, title in titles.items():
        if "A321" not in title["title"] and not (not title["title"] and title["kind"] == "Aircraft"): continue
        position = positions.get(oid)
        item = {**title, "identity": fields.get(oid), "position": position}
        if position and position["accepted"]:
            p = position["position"]
            item["distanceNmAtLastOwnSample"] = distance(p["Latitude"], p["Longitude"])
        candidates.append(item)
    for sample in coherent.values(): sample["distanceNmAtLastOwnSample"] = distance(sample["Latitude"], sample["Longitude"])
    print(json.dumps({"log": str(path), "counts": counts, "candidates": candidates,
        "namedCoherent": list(coherent.values()), "own": own, "errors": errors[-5:], "motion": motions[-5:]}, indent=2))
