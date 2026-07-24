import frida, time, subprocess, os, urllib.request, json

EXE = r"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF\NVIDIA App.exe"
CWD = r"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF"

# From settings dump: size=440, background_color at 344 => remote_debugging_port at 336.
JS = r"""
'use strict';
let hooked = false;
function resolveExport(modName, expName) {
  try {
    const m = Process.getModuleByName(modName);
    if (m && m.findExportByName) {
      const p = m.findExportByName(expName);
      if (p) return p;
    }
  } catch (e) {}
  try { if (Module.getExportByName) return Module.getExportByName(modName, expName); } catch (e) {}
  return null;
}
function hook() {
  if (hooked) return true;
  const cefInit = resolveExport('libcef.dll', 'cef_initialize');
  if (!cefInit) { send({t:'waiting'}); return false; }
  hooked = true;
  send({t:'hook', addr: cefInit.toString()});
  Interceptor.attach(cefInit, {
    onEnter(args) {
      const settings = args[1];
      const size = settings.readU32();
      const bg = settings.add(344).readS32();
      const portBefore = settings.add(336).readS32();
      const uncaughtBefore = settings.add(340).readS32();
      send({t:'before', size:size, port:portBefore, uncaught:uncaughtBefore, bg:bg});
      settings.add(336).writeS32(9333);
      send({t:'after', port: settings.add(336).readS32(), uncaught: settings.add(340).readS32(), bg: settings.add(344).readS32()});
    },
    onLeave(retval) { send({t:'leave', ret: retval.toInt32()}); }
  });
  return true;
}
if (!hook()) {
  const iv = setInterval(() => { if (hook()) clearInterval(iv); }, 20);
}
"""

def main():
    subprocess.run(['taskkill', '/F', '/IM', 'NVIDIA App.exe'], capture_output=True)
    time.sleep(2)
    device = frida.get_local_device()
    # remote-allow-origins required for CDP websocket from local scripts
    pid = device.spawn([EXE, '--remote-allow-origins=*', '--nv-remote-debugging-port=9333'], cwd=CWD)
    print('spawned', pid)
    session = device.attach(pid)
    script = session.create_script(JS)
    script.on('message', lambda m, d: print('MSG', m))
    script.load()
    device.resume(pid)
    print('resumed')
    time.sleep(12)

    for port in (9333, 9222, 9229):
        try:
            with urllib.request.urlopen(f'http://127.0.0.1:{port}/json', timeout=2) as r:
                body = r.read().decode()
                print('CDP OK', port, body[:400])
                # Try evaluate SetDDSState on first page target
                targets = json.loads(body)
                return targets, port
        except Exception as e:
            print('CDP fail', port, type(e).__name__, e)

    p = os.path.expandvars(r'%LOCALAPPDATA%\NVIDIA Corporation\NVIDIA App\CefCache\DevToolsActivePort')
    print('DevToolsActivePort', os.path.exists(p))
    if os.path.exists(p):
        print(open(p, encoding='utf-8', errors='ignore').read())

    # netstat-like via powershell leftover: check listening
    return None, None

if __name__ == '__main__':
    main()
