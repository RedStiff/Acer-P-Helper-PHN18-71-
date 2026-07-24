"""CDP Get/SetDDS with retries. Requires NVIDIA App CDP on :9333.

Usage:
  python _hit_setdds.py           # DGPU MuxState=2
  python _hit_setdds.py igpu      # IGPU MuxState=1
  python _hit_setdds.py auto      # Automatic
  python _hit_setdds.py get       # Get only
"""
import json
import sys
import time
import urllib.request
import winreg

import websocket

PORT = 9333

JS_GET = (
    "(()=>new Promise(res=>{const q={command:'QUERY_IPC_EXTENSION_MESSAGE',"
    "system:'CrimsonNative',module:'NvCplDisplayPlugin',method:'GetDDSState',"
    "payload:{}};if(!window.cefQuery)return res({error:'no_cefQuery'});"
    "window.cefQuery({request:JSON.stringify(q),persistent:false,"
    "onSuccess:r=>res({ok:true,r}),onFailure:(c,e)=>res({ok:false,code:c,err:String(e)})});"
    "setTimeout(()=>res({error:'timeout'}),12000)}))()"
)


def js_set(automatic: bool, mux_state: int) -> str:
    auto_js = "true" if automatic else "false"
    return (
        "(()=>new Promise(res=>{const q={command:'QUERY_IPC_EXTENSION_MESSAGE',"
        "system:'CrimsonNative',module:'NvCplDisplayPlugin',method:'SetDDSState',"
        "payload:{bIsAutomatic:%s,MuxState:%d}};"
        "if(!window.cefQuery)return res({error:'no_cefQuery'});"
        "window.cefQuery({request:JSON.stringify(q),persistent:false,"
        "onSuccess:r=>res({ok:true,r}),onFailure:(c,e)=>res({ok:false,code:c,err:String(e)})});"
        "setTimeout(()=>res({error:'timeout'}),25000)}))()"
    ) % (auto_js, int(mux_state))


def ace():
    key = winreg.OpenKey(
        winreg.HKEY_LOCAL_MACHINE,
        r"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE",
    )
    state, _ = winreg.QueryValueEx(key, "InternalMuxState")
    auto, _ = winreg.QueryValueEx(key, "InternalMuxIsAutomaticMode")
    i2d, _ = winreg.QueryValueEx(key, "ACESwitchedI2D")
    return f"state={state}|auto={auto}|i2d={i2d}"


def page_ws():
    with urllib.request.urlopen(f"http://127.0.0.1:{PORT}/json", timeout=5) as response:
        targets = json.loads(response.read().decode())
    for target in targets:
        if target.get("type") == "page":
            return target["webSocketDebuggerUrl"], target
    raise RuntimeError("no page")


def cdp_eval(ws_url, expression, await_promise=True, timeout=30):
    ws = websocket.create_connection(ws_url, timeout=timeout, suppress_origin=True)
    msg_id = 1

    def send(method, params=None):
        nonlocal msg_id
        payload = {"id": msg_id, "method": method}
        if params is not None:
            payload["params"] = params
        ws.send(json.dumps(payload))
        current = msg_id
        msg_id += 1
        return current

    send("Runtime.enable")
    request_id = send(
        "Runtime.evaluate",
        {
            "expression": expression,
            "awaitPromise": await_promise,
            "returnByValue": True,
            "userGesture": True,
        },
    )
    deadline = time.time() + timeout
    result = None
    while time.time() < deadline:
        message = json.loads(ws.recv())
        if message.get("id") == request_id:
            result = message
            break
    ws.close()
    try:
        return result["result"]["result"]["value"]
    except Exception:
        return result


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "dgpu"
    print("ACE before", ace())
    ws_url, target = page_ws()
    print("page", target.get("title"))

    for _ in range(20):
        query_type = cdp_eval(ws_url, "typeof window.cefQuery", await_promise=False)
        print("typeof", query_type)
        if query_type == "function":
            break
        time.sleep(1)

    get_value = None
    for attempt in range(12):
        get_value = cdp_eval(ws_url, JS_GET)
        print(f"GET[{attempt}]", get_value)
        if isinstance(get_value, dict) and get_value.get("ok"):
            break
        time.sleep(2)

    if mode == "get":
        print("ACE", ace())
        return 0

    if mode == "auto":
        expression = js_set(True, 1)
    elif mode == "igpu":
        expression = js_set(False, 1)
    else:
        expression = js_set(False, 2)

    before = ace()
    set_value = None
    for attempt in range(8):
        set_value = cdp_eval(ws_url, expression)
        print(f"SET[{attempt}]", set_value)
        if isinstance(set_value, dict) and set_value.get("ok"):
            break
        time.sleep(2)

    time.sleep(5)
    after = ace()
    print("ACE after", after)
    print("HIT" if before != after else "NO_HIT")
    return 0 if before != after else 2


if __name__ == "__main__":
    raise SystemExit(main())
