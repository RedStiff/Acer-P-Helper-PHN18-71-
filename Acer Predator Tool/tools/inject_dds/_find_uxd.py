import pathlib, re
d = pathlib.Path(r"C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvcpl.dll").read_bytes()
for m in re.finditer(rb"[\x20-\x7e]{6,80}", d):
    s = m.group().decode()
    if "UXD" in s.upper() and any(k in s for k in ("Service", "Event", "Started", "Global", "Local", "Running")):
        print(s)

# also check NVDisplay.Container plugins folder
root = pathlib.Path(r"C:\Program Files\NVIDIA Corporation")
for p in root.rglob("*"):
    if p.is_file() and "uxd" in p.name.lower():
        print("FILE", p)
