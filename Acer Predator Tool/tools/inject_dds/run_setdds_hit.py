"""Spawn NVIDIA App with forced CDP, invoke SetDDSState via cefQuery, measure ACE HIT."""
import base64
import json
import os
import socket
import subprocess
import sys
import time
import urllib.request
import winreg
from urllib.parse import urlparse

import frida

try:
    import websocket
except ImportError:
    subprocess.check_call([sys.executable, '-m', 'pip', 'install', 'websocket-client', '-q'])
    import websocket

EXE = r"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF\NVIDIA App.exe"
CWD = r"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF"
PORT = 9333

HOOK_JS = r"""
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
  if (!cefInit) return false;
  hooked = true;
  send({t:'hook', addr: cefInit.toString()});
  Interceptor.attach(cefInit, {
    onEnter(args) {
      const settings = args[1];
      // CEF128 size=440: remote_debugging_port@336, background_color@344
      // Do NOT clear command_line_args_disabled (@100) — that leaves plugins in "failed".
      // CDP websocket uses suppress_origin=True instead of --remote-allow-origins.
      settings.add(336).writeS32(9333);
      send({t:'port_set', port: settings.add(336).readS32()});
    },
    onLeave(retval) { send({t:'leave', ret: retval.toInt32()}); }
  });
  return true;
}
if (!hook()) {
  const iv = setInterval(() => { if (hook()) clearInterval(iv); }, 20);
}
"""

JS_GET = r"""
(() => new Promise((resolve) => {
  const q = {
    command: 'QUERY_IPC_EXTENSION_MESSAGE',
    system: 'CrimsonNative',
    module: 'NvCplDisplayPlugin',
    method: 'GetDDSState',
    payload: {}
  };
  if (!window.cefQuery) { resolve({error:'no_cefQuery'}); return; }
  window.cefQuery({
    request: JSON.stringify(q),
    persistent: false,
    onSuccess: (r) => resolve({ok:true, r}),
    onFailure: (c,e) => resolve({ok:false, code:c, err:String(e)})
  });
  setTimeout(() => resolve({error:'timeout'}), 12000);
}))()
"""


def js_set(auto: bool, mux: int) -> str:
    return f"""
(() => new Promise((resolve) => {{
  const q = {{
    command: 'QUERY_IPC_EXTENSION_MESSAGE',
    system: 'CrimsonNative',
    module: 'NvCplDisplayPlugin',
    method: 'SetDDSState',
    payload: {{ bIsAutomatic: {str(auto).lower()}, MuxState: {int(mux)} }}
  }};
  if (!window.cefQuery) {{ resolve({{error:'no_cefQuery'}}); return; }}
  window.cefQuery({{
    request: JSON.stringify(q),
    persistent: false,
    onSuccess: (r) => resolve({{ok:true, r}}),
    onFailure: (c,e) => resolve({{ok:false, code:c, err:String(e)}})
  }});
  setTimeout(() => resolve({{error:'timeout'}}), 25000);
}}))()
"""


def ace():
    try:
        k = winreg.OpenKey(
            winreg.HKEY_LOCAL_MACHINE,
            r'SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE'
        )
        state, _ = winreg.QueryValueEx(k, 'InternalMuxState')
        auto, _ = winreg.QueryValueEx(k, 'InternalMuxIsAutomaticMode')
        i2d, _ = winreg.QueryValueEx(k, 'ACESwitchedI2D')
        return f'state={state}|auto={auto}|i2d={i2d}'
    except OSError as e:
        return f'ace_err={e}'


def wait_cdp(timeout=40):
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            with urllib.request.urlopen(f'http://127.0.0.1:{PORT}/json', timeout=2) as r:
                return json.loads(r.read().decode())
        except Exception:
            time.sleep(0.4)
    raise RuntimeError('CDP not up')


def page_ws(targets):
    for t in targets:
        if t.get('type') == 'page' and 'NVIDIA' in (t.get('title') or ''):
            return t['webSocketDebuggerUrl'], t
    for t in targets:
        if t.get('type') == 'page':
            return t['webSocketDebuggerUrl'], t
    raise RuntimeError('no page')


def cdp_eval(ws_url, expression, await_promise=True, timeout=30):
    ws = websocket.create_connection(ws_url, timeout=timeout, suppress_origin=True)
    msg_id = 1

    def send(method, params=None):
        nonlocal msg_id
        payload = {'id': msg_id, 'method': method}
        if params is not None:
            payload['params'] = params
        ws.send(json.dumps(payload))
        mid = msg_id
        msg_id += 1
        return mid

    send('Runtime.enable')
    rid = send('Runtime.evaluate', {
        'expression': expression,
        'awaitPromise': await_promise,
        'returnByValue': True,
        'userGesture': True,
    })
    deadline = time.time() + timeout
    result = None
    while time.time() < deadline:
        msg = json.loads(ws.recv())
        if msg.get('id') == rid:
            result = msg
            break
    ws.close()
    return result


def spawn_app():
    subprocess.run(['taskkill', '/F', '/IM', 'NVIDIA App.exe'], capture_output=True)
    time.sleep(3)
    device = frida.get_local_device()
    # No Chromium CLI flags — NVIDIA sets command_line_args_disabled; CDP port comes from settings hook only.
    pid = device.spawn([EXE], cwd=CWD)
    print('spawned', pid)
    session = device.attach(pid)
    left = {'done': False}

    def on_msg(m, _d):
        payload = m.get('payload', m)
        print('FRIDA', payload)
        if isinstance(payload, dict) and payload.get('t') == 'leave':
            left['done'] = True

    script = session.create_script(HOOK_JS)
    script.on('message', on_msg)
    script.load()
    device.resume(pid)

    # Detach soon after cef_initialize to reduce plugin init interference
    deadline = time.time() + 15
    while time.time() < deadline and not left['done']:
        time.sleep(0.1)
    try:
        script.unload()
        session.detach()
        print('frida detached')
    except Exception as e:
        print('detach', e)
    return pid


def extract_value(cdp_result):
    try:
        return cdp_result['result']['result']['value']
    except Exception:
        return cdp_result


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else 'dgpu'
    print('ACE before', ace())
    spawn_app()
    targets = wait_cdp(40)
    ws_url, t = page_ws(targets)
    print('page', t.get('title'), (t.get('url') or '')[:90])

    # Wait for plugins to leave Initializing
    time.sleep(12)

    r0 = extract_value(cdp_eval(ws_url, "typeof window.cefQuery", await_promise=False))
    print('typeof cefQuery', r0)

    # Retry Get/Set while plugin recovers from Initializing/failed
    get_val = None
    for attempt in range(8):
        get_val = extract_value(cdp_eval(ws_url, JS_GET))
        print(f'GET[{attempt}]', get_val)
        if isinstance(get_val, dict) and get_val.get('ok'):
            break
        err = str(get_val)
        if 'failed' not in err.lower() and 'timeout' not in err.lower() and 'Initializing' not in err:
            break
        time.sleep(3)

    if mode == 'auto':
        expr = js_set(True, 1)
    elif mode == 'igpu':
        expr = js_set(False, 1)
    else:
        expr = js_set(False, 2)

    before = ace()
    set_val = None
    for attempt in range(8):
        set_val = extract_value(cdp_eval(ws_url, expr))
        print(f'SET[{attempt}]', set_val)
        if isinstance(set_val, dict) and set_val.get('ok'):
            break
        err = str(set_val)
        if 'failed' in err.lower() or 'Initializing' in err or 'timeout' in err.lower():
            time.sleep(3)
            continue
        break

    time.sleep(5)
    after = ace()
    print('ACE after', after)
    hit = before != after
    print('HIT' if hit else 'NO_HIT')
    return 0 if hit else 2


if __name__ == '__main__':
    raise SystemExit(main())
