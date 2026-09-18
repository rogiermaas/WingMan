"""Read-only acquisition experiment through MSFS's local SDK Coherent inspector."""
import json
import pathlib
import sys
import time
import urllib.request

sys.path.insert(0, str(pathlib.Path("artifacts/python").resolve()))
import websocket

if len(sys.argv) < 3:
    text = pathlib.Path("artifacts/coherent-main.js").read_text(encoding="utf-8")
    for needle in ("initializeWebSocket(", "ws://", "page="):
        start = 0
        for _ in range(6):
            pos = text.find(needle, start)
            if pos < 0:
                break
            print(text[max(0, pos - 200):pos + 350])
            start = pos + len(needle)
    sys.exit(0)

url, expression_path = sys.argv[1:3]
expression = pathlib.Path(expression_path).read_text(encoding="utf-8")
connection = websocket.create_connection(url, timeout=8, suppress_origin=True)
try:
    count = min(int(sys.argv[3]), 600) if len(sys.argv) > 3 else 1
    for request_id in range(1, count + 1):
        connection.send(json.dumps({"id": request_id, "method": "Runtime.evaluate", "params": {
            "expression": expression, "returnByValue": True, "objectGroup": "escort-acquisition"}}))
        deadline = time.monotonic() + 10
        message = None
        while time.monotonic() < deadline:
            reply = json.loads(connection.recv())
            if reply.get("id") == request_id:
                message = reply
                break
        if message is not None:
            destination = pathlib.Path("artifacts/coherent")
            destination.mkdir(parents=True, exist_ok=True)
            (destination / f"{time.time_ns()}-{pathlib.Path(expression_path).stem}.json").write_text(json.dumps(message, indent=2), encoding="utf-8")
            if count == 1 or request_id % 10 == 0:
                print(json.dumps(message, ensure_ascii=True), flush=True)
        if count > 1:
            time.sleep(1)
finally:
    connection.close()
