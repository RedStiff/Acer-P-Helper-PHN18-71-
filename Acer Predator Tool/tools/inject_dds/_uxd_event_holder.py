"""Hold Local\\UXDServiceStarted so NvCplApiIsUxdServiceRunning succeeds."""
import ctypes
import time

kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
EVENT_NAME = "Local\\UXDServiceStarted-D40E81C4-06EF-454A-9E81-1F4D55CEBD57"

# Manual-reset, initially signaled.
handle = kernel32.CreateEventW(None, True, True, EVENT_NAME)
print(f"holding UXDServiceStarted handle={handle} err={ctypes.get_last_error()}", flush=True)
if not handle:
    raise SystemExit(1)

while True:
    time.sleep(3600)
