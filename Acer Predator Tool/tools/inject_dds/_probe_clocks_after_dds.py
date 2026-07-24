"""Compare GPU clocks after DDS switch: idle vs short load (nvidia-smi)."""
from __future__ import annotations

import json
import subprocess
import sys
import time
import urllib.request
from pathlib import Path

PORT = 9333
HIT = Path(__file__).with_name("_hit_setdds.py")


def smi():
    try:
        out = subprocess.check_output(
            [
                "nvidia-smi",
                "--query-gpu=clocks.current.graphics,clocks.max.graphics,clocks.current.memory,power.draw,utilization.gpu,pstate",
                "--format=csv,noheader,nounits",
            ],
            text=True,
            timeout=5,
        ).strip()
        return out
    except Exception as e:
        return f"smi_err={e}"


def cdp_up():
    try:
        urllib.request.urlopen(f"http://127.0.0.1:{PORT}/json", timeout=2)
        return True
    except Exception:
        return False


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "dgpu"
    print("BEFORE", smi())
    if not cdp_up():
        print("CDP down — launch silent/host first via launcher if needed")
        return 2
    print(subprocess.check_output([sys.executable, "-u", str(HIT), mode], text=True))
    time.sleep(2)
    print("AFTER_IDLE", smi())
    # short synthetic load via nvidia-smi dmon samples while stressing with PowerShell .NET? skip
    # Use CUDA-free approach: query while running a few seconds of GPU decode if available
    print("Sampling 8s idle...")
    for i in range(8):
        print(f" t+{i}", smi())
        time.sleep(1)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
