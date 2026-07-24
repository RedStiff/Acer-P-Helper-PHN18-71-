"""Launch NVIDIA App suspended, inject cef_initialize port hook (no Frida), SetDDSState via CDP."""
import json
import subprocess
import sys
import time
import urllib.request
import winreg
import ctypes
from ctypes import wintypes

try:
    import websocket
except ImportError:
    subprocess.check_call([sys.executable, '-m', 'pip', 'install', 'websocket-client', '-q'])
    import websocket

EXE = r"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF\NVIDIA App.exe"
CWD = r"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF"
DLL = r"e:\Projects\Acer Predator Tool\Acer Predator Tool\tools\inject_dds\hook_cef_port.dll"
PORT = 9333

kernel32 = ctypes.WinDLL('kernel32', use_last_error=True)

CREATE_SUSPENDED = 0x00000004
PROCESS_ALL = 0x1F0FFF


class STARTUPINFO(ctypes.Structure):
    _fields_ = [
        ('cb', wintypes.DWORD),
        ('lpReserved', wintypes.LPWSTR),
        ('lpDesktop', wintypes.LPWSTR),
        ('lpTitle', wintypes.LPWSTR),
        ('dwX', wintypes.DWORD), ('dwY', wintypes.DWORD),
        ('dwXSize', wintypes.DWORD), ('dwYSize', wintypes.DWORD),
        ('dwXCountChars', wintypes.DWORD), ('dwYCountChars', wintypes.DWORD),
        ('dwFillAttribute', wintypes.DWORD),
        ('dwFlags', wintypes.DWORD),
        ('wShowWindow', wintypes.WORD),
        ('cbReserved2', wintypes.WORD),
        ('lpReserved2', ctypes.POINTER(wintypes.BYTE)),
        ('hStdInput', wintypes.HANDLE),
        ('hStdOutput', wintypes.HANDLE),
        ('hStdError', wintypes.HANDLE),
    ]


class PROCESS_INFORMATION(ctypes.Structure):
    _fields_ = [
        ('hProcess', wintypes.HANDLE),
        ('hThread', wintypes.HANDLE),
        ('dwProcessId', wintypes.DWORD),
        ('dwThreadId', wintypes.DWORD),
    ]


def inject(pid, dll_path):
    h = kernel32.OpenProcess(PROCESS_ALL, False, pid)
    if not h:
        raise OSError('OpenProcess', ctypes.get_last_error())
    path = dll_path.encode('utf-16le') + b'\x00\x00'
    remote = kernel32.VirtualAllocEx(h, None, len(path), 0x3000, 0x04)
    written = ctypes.c_size_t()
    if not kernel32.WriteProcessMemory(h, remote, path, len(path), ctypes.byref(written)):
        raise OSError('WriteProcessMemory', ctypes.get_last_error())
    k32 = kernel32.GetModuleHandleW('kernel32.dll')
    load = kernel32.GetProcAddress(k32, b'LoadLibraryW')
    th = kernel32.CreateRemoteThread(h, None, 0, load, remote, 0, None)
    if not th:
        raise OSError('CreateRemoteThread', ctypes.get_last_error())
    kernel32.WaitForSingleObject(th, 15000)
    kernel32.CloseHandle(th)
    kernel32.CloseHandle(h)


def spawn_suspended():
    si = STARTUPINFO()
    si.cb = ctypes.sizeof(si)
    pi = PROCESS_INFORMATION()
    cmd = f'"{EXE}"'
    ok = kernel32.CreateProcessW(
        EXE, cmd, None, None, False, CREATE_SUSPENDED, None, CWD,
        ctypes.byref(si), ctypes.byref(pi)
    )
    if not ok:
        raise OSError('CreateProcess', ctypes.get_last_error())
    return pi


def ace():
    k = winreg.OpenKey(
        winreg.HKEY_LOCAL_MACHINE,
        r'SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE'
    )
    state, _ = winreg.QueryValueEx(k, 'InternalMuxState')
    auto, _ = winreg.QueryValueEx(k, 'InternalMuxIsAutomaticMode')
    i2d, _ = winreg.QueryValueEx(k, 'ACESwitchedI2D')
    return f'state={state}|auto={auto}|i2d={i2d}'


def wait_cdp(timeout=45):
    t0 = time.time()
    while time.time() - t0 < timeout:
        try:
            with urllib.request.urlopen(f'http://127.0.0.1:{PORT}/json', timeout=2) as r:
                return json.loads(r.read().decode())
        except Exception:
            time.sleep(0.5)
    raise RuntimeError('CDP not up')


def cdp_eval(ws_url, expression, await_promise=True, timeout=30):
    ws = websocket.create_connection(ws_url, timeout=timeout, suppress_origin=True)
    mid = {'n': 1}

    def send(method, params=None):
        i = mid['n']; mid['n'] += 1
        msg = {'id': i, 'method': method}
        if params is not None:
            msg['params'] = params
        ws.send(json.dumps(msg))
        return i

    send('Runtime.enable')
    rid = send('Runtime.evaluate', {
        'expression': expression,
        'awaitPromise': await_promise,
        'returnByValue': True,
        'userGesture': True,
    })
    deadline = time.time() + timeout
    out = None
    while time.time() < deadline:
        m = json.loads(ws.recv())
        if m.get('id') == rid:
            out = m
            break
    ws.close()
    try:
        return out['result']['result']['value']
    except Exception:
        return out


JS_GET = """(()=>new Promise(res=>{const q={command:'QUERY_IPC_EXTENSION_MESSAGE',system:'CrimsonNative',module:'NvCplDisplayPlugin',method:'GetDDSState',payload:{}};if(!window.cefQuery)return res({error:'no_cefQuery'});window.cefQuery({request:JSON.stringify(q),persistent:false,onSuccess:r=>res({ok:true,r}),onFailure:(c,e)=>res({ok:false,code:c,err:String(e)})});setTimeout(()=>res({error:'timeout'}),12000)}))()"""


def js_set(auto, mux):
    return f"""(()=>new Promise(res=>{{const q={{command:'QUERY_IPC_EXTENSION_MESSAGE',system:'CrimsonNative',module:'NvCplDisplayPlugin',method:'SetDDSState',payload:{{bIsAutomatic:{str(auto).lower()},MuxState:{int(mux)}}}}};if(!window.cefQuery)return res({{error:'no_cefQuery'}});window.cefQuery({{request:JSON.stringify(q),persistent:false,onSuccess:r=>res({{ok:true,r}}),onFailure:(c,e)=>res({{ok:false,code:c,err:String(e)}})}});setTimeout(()=>res({{error:'timeout'}}),25000)}}))()"""


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else 'dgpu'
    subprocess.run(['taskkill', '/F', '/IM', 'NVIDIA App.exe'], capture_output=True)
    time.sleep(2)

    print('ACE before', ace())
    pi = spawn_suspended()
    print('pid', pi.dwProcessId, 'suspended')
    inject(pi.dwProcessId, DLL)
    print('injected', DLL)
    kernel32.ResumeThread(pi.hThread)
    print('resumed')

    targets = wait_cdp(50)
    page = next(t for t in targets if t.get('type') == 'page')
    ws = page['webSocketDebuggerUrl']
    print('page', page.get('title'))

    # Wait for plugins - without Frida they should initialize
    for i in range(20):
        t = cdp_eval(ws, "typeof window.cefQuery", await_promise=False)
        if t == 'function':
            break
        time.sleep(1)
    print('typeof cefQuery', t)

    for i in range(10):
        g = cdp_eval(ws, JS_GET)
        print(f'GET[{i}]', g)
        if isinstance(g, dict) and g.get('ok'):
            break
        time.sleep(2)

    expr = js_set(True, 1) if mode == 'auto' else js_set(False, 1 if mode == 'igpu' else 2)
    before = ace()
    for i in range(10):
        s = cdp_eval(ws, expr)
        print(f'SET[{i}]', s)
        if isinstance(s, dict) and s.get('ok'):
            break
        time.sleep(2)
    time.sleep(5)
    after = ace()
    print('ACE after', after)
    print('HIT' if before != after else 'NO_HIT')
    return 0 if before != after else 2


if __name__ == '__main__':
    raise SystemExit(main())
