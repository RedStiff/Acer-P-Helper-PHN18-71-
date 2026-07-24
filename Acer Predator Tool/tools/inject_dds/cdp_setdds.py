"""Connect to NVIDIA App CDP and invoke SetDDSState via window.cefQuery."""
import json
import time
import urllib.request

try:
    import websocket  # websocket-client
except ImportError:
    import subprocess, sys
    subprocess.check_call([sys.executable, '-m', 'pip', 'install', 'websocket-client', '-q'])
    import websocket

PORT = 9333

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
    onFailure: (c,e) => resolve({ok:false, code:c, err:e})
  });
  setTimeout(() => resolve({error:'timeout'}), 8000);
}))()
"""

JS_SET = r"""
((auto, mux) => new Promise((resolve) => {
  const q = {
    command: 'QUERY_IPC_EXTENSION_MESSAGE',
    system: 'CrimsonNative',
    module: 'NvCplDisplayPlugin',
    method: 'SetDDSState',
    payload: { bIsAutomatic: !!auto, MuxState: mux|0 }
  };
  if (!window.cefQuery) { resolve({error:'no_cefQuery'}); return; }
  window.cefQuery({
    request: JSON.stringify(q),
    persistent: false,
    onSuccess: (r) => resolve({ok:true, r}),
    onFailure: (c,e) => resolve({ok:false, code:c, err:e})
  });
  setTimeout(() => resolve({error:'timeout'}), 15000);
}))(false, 2)
"""


def get_page_ws():
    with urllib.request.urlopen(f'http://127.0.0.1:{PORT}/json', timeout=5) as r:
        targets = json.loads(r.read().decode())
    for t in targets:
        if t.get('type') == 'page' and 'NVIDIA' in (t.get('title') or ''):
            return t['webSocketDebuggerUrl'], t
    for t in targets:
        if t.get('type') == 'page':
            return t['webSocketDebuggerUrl'], t
    raise RuntimeError('no page target: ' + json.dumps(targets)[:500])


def cdp_eval(ws_url, expression, await_promise=True, timeout=20):
    # NVIDIA CDP rejects Origin header with 403; suppress it.
    ws = websocket.create_connection(
        ws_url,
        timeout=timeout,
        suppress_origin=True,
    )
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

    # Enable runtime
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
        raw = ws.recv()
        msg = json.loads(raw)
        if msg.get('id') == rid:
            result = msg
            break
    ws.close()
    return result


def ace():
    import winreg
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


def main():
    print('ACE before', ace())
    # Wait for plugins
    for i in range(15):
        try:
            ws_url, t = get_page_ws()
            print('target', t.get('title'), t.get('url', '')[:80])
            break
        except Exception as e:
            print('wait target', e)
            time.sleep(1)
    else:
        raise SystemExit('no target')

    # Probe cefQuery exists
    r0 = cdp_eval(ws_url, "typeof window.cefQuery", await_promise=False)
    print('typeof cefQuery', json.dumps(r0)[:300])

    print('GetDDSState...')
    r1 = cdp_eval(ws_url, JS_GET)
    print('GET', json.dumps(r1)[:800])

    print('SetDDSState DGPU...')
    r2 = cdp_eval(ws_url, JS_SET)
    print('SET', json.dumps(r2)[:800])
    time.sleep(4)
    print('ACE after', ace())


if __name__ == '__main__':
    main()
