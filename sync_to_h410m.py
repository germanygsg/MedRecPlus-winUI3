#!/usr/bin/env python3
"""Sync ~/workspace/medrecplus-winui3/src -> F:\\app\\medrecplus-winui\\src on H410M.

Creates remote directories first (WinRemote /file has no delete by design;
stale files are never removed by this script — rename remotely if needed).
Prints per-file OK/FAIL and a summary. Never prints the WinRemote token.
"""
import os
import sys

sys.path.insert(0, "/home/hatch/workspace/winremote/client")
from winremote import WinRemote, WinRemoteError

LOCAL_ROOT = "/home/hatch/workspace/medrecplus-winui3/src"
REMOTE_ROOT = r"F:\app\medrecplus-winui\src"

SKIP_DIRS = {".vs", "bin", "obj", ".git"}


def collect():
    files, dirs = [], set()
    for dirpath, dirnames, filenames in os.walk(LOCAL_ROOT):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        rel = os.path.relpath(dirpath, LOCAL_ROOT)
        rdir = REMOTE_ROOT if rel == "." else REMOTE_ROOT + "\\" + rel.replace("/", "\\")
        dirs.add(rdir)
        for fn in filenames:
            files.append((os.path.join(dirpath, fn), rdir + "\\" + fn))
    return sorted(dirs), sorted(files)


def main():
    try:
        with open("/home/hatch/workspace/winremote/.token") as f:
            token = f.read().strip()
    except Exception:
        print("FAIL: could not read winremote token")
        return 2
    try:
        agent = WinRemote("100.69.141.114", token=token, timeout=180)
        agent.health()
    except Exception as e:
        print(f"FAIL: winremote unreachable: {e}")
        return 3

    dirs, files = collect()
    print(f"{len(dirs)} dirs, {len(files)} files to sync")

    # Create directories (single PowerShell call)
    dirlist = ",".join(f"'{d.replace(chr(39), chr(39)*2)}'" for d in sorted(dirs))
    ps = f"New-Item -ItemType Directory -Force -Path {dirlist} | Out-Null; 'OK'"
    try:
        r = agent.shell_exec(ps, timeout=120, bitness="64")
        if (r.get("stdout") or "").strip() != "OK":
            print("FAIL: mkdir step:", (r.get("stderr") or "")[:300])
            return 1
    except Exception as e:
        print(f"FAIL: mkdir: {e}")
        return 1

    # NOTE (2026-10-14): agent.upload() RESUMES from the remote file's size, which
    # corrupts files edited in the middle (old head + new tail). Upload to a temp
    # name, then Move-Item -Force for an atomic replace.
    import time
    run_tag = f".sync_{int(time.time())}"
    # Clean any stale temp files from earlier runs first.
    try:
        agent.shell_exec(
            f"Get-ChildItem -Path '{REMOTE_ROOT}' -Recurse -Filter '*.sync_*' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue; 'CLEAN'",
            timeout=120, bitness="64")
    except Exception:
        pass
    ok, fail = 0, []
    moves = []
    for local, remote in files:
        tmp = remote + run_tag
        try:
            agent.upload(local, tmp)
            moves.append((tmp, remote))
            ok += 1
        except Exception as e:
            fail.append((remote, str(e)[:120]))
    if moves:
        ps = "\n".join(
            f"Move-Item -Force -Path '{t.replace(chr(39), chr(39)*2)}' -Destination '{d.replace(chr(39), chr(39)*2)}'"
            for t, d in moves) + "\n'MOVED'"
        try:
            r = agent.shell_exec(ps, timeout=120, bitness="64")
            if (r.get("stdout") or "").strip().splitlines()[-1] != "MOVED":
                print("FAIL: move step:", (r.get("stderr") or "")[:300])
                return 1
        except Exception as e:
            print(f"FAIL: move: {e}")
            return 1
    print(f"uploaded {ok}/{len(files)}")
    for remote, err in fail:
        print(f"  FAIL {remote}: {err}")
    return 1 if fail else 0


if __name__ == "__main__":
    sys.exit(main())
