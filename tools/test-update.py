"""Offline real-process updater smoke test. Never connects to MSFS or arms flight control."""
import ctypes, json, os, pathlib, shutil, subprocess, time, uuid, winreg

root = pathlib.Path(__file__).resolve().parents[1]
profile = "update-" + uuid.uuid4().hex[:10]
env = dict(os.environ, WINGMAN_TEST_PROFILE=profile, WINGMAN_OFFLINE_TEST="1")
install = root / "artifacts" / profile
install.mkdir()
exe = install / "WingMan.exe"
shutil.copy2(root / "dist/WingMan/WingMan.exe", exe)
data = pathlib.Path(env["LOCALAPPDATA"]) / "WingMan/Tests" / profile
key_path = "Software\\WingMan\\Tests\\" + profile
with winreg.CreateKey(winreg.HKEY_CURRENT_USER, key_path) as key:
    winreg.SetValueEx(key, "Network", 0, winreg.REG_SZ, json.dumps(dict(server="https://wingman.rogiermaas.nl/",name="Update test pilot",room="",id=uuid.uuid4().hex,token="x"*64,share=True,reconnect=False,lead="",automaticUpdates=False)))
p = subprocess.Popen([str(exe), "--update-test", "http://127.0.0.1:18787/"], env=env, cwd=root)
deadline = time.monotonic() + 90
job_dir = None
try:
    while time.monotonic() < deadline:
        marker = data / "test-update-directory.txt"
        if marker.exists(): job_dir = pathlib.Path(marker.read_text())
        if job_dir:
            if (job_dir / "update-error.txt").exists(): raise RuntimeError((job_dir / "update-error.txt").read_text())
            if (job_dir / "result.txt").exists(): break
        time.sleep(.2)
    else: raise RuntimeError("Updater did not complete within 90 seconds")
    p.wait(timeout=5)
    assert (install / "WingMan.exe.previous").exists(), "No rollback copy"
    assert (job_dir / "ready.txt").read_text() == "1.1.1", "Replacement never acknowledged the new version"
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER, key_path) as key:
        ticket = json.loads(winreg.QueryValueEx(key, "UpdateResume")[0])
        assert ticket["resume"]["wasFollowing"] is False, "Stale follow intent survived a stop during download"
        assert json.loads(winreg.QueryValueEx(key, "Network")[0])["name"] == "Update test pilot", "Pilot settings lost"
    result = dict(passed=True, version="1.1.1", current_follow_state_preserved=True, rollback_copy=True, profile=profile)
    (root / "artifacts/update-test-result.json").write_text(json.dumps(result, indent=2))
    print(json.dumps(result))
finally:
    # Close only WingMan windows belonging to this exact isolated executable.
    processes = subprocess.check_output(["powershell.exe", "-NoProfile", "-Command", "Get-Process WingMan -ErrorAction SilentlyContinue | Select-Object Id,Path | ConvertTo-Json -Compress"], text=True).strip()
    if processes:
        entries = json.loads(processes); entries = entries if isinstance(entries, list) else [entries]
        ids = {entry["Id"] for entry in entries if entry.get("Path", "").lower() == str(exe).lower()}
        CALLBACK = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)
        def close(hwnd, _):
            pid = ctypes.c_ulong(); ctypes.windll.user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
            if pid.value in ids: ctypes.windll.user32.PostMessageW(hwnd, 0x10, 0, 0)
            return True
        ctypes.windll.user32.EnumWindows(CALLBACK(close), 0)
