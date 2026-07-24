#include <windows.h>
#include <stdio.h>
#include <stdint.h>
#include <string.h>

static void logmsg(const char* s) {
  char path[MAX_PATH];
  ExpandEnvironmentStringsA("%TEMP%\\inject_dds.log", path, MAX_PATH);
  HANDLE h = CreateFileA(path, FILE_APPEND_DATA, FILE_SHARE_READ, NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
  if (h == INVALID_HANDLE_VALUE) return;
  DWORD w; WriteFile(h, s, (DWORD)strlen(s), &w, NULL);
  WriteFile(h, "\r\n", 2, &w, NULL);
  CloseHandle(h);
}

/* Plugin method signature guesses */
typedef int (*fn_s)(const char*);
typedef int (*fn_ss)(const char*, const char*);
typedef int (*fn_sp)(const char*, void*);
typedef int (*fn_ps)(void*, const char*);
typedef int (*fn_pss)(void*, const char*, const char*);
typedef int (*fn_psp)(void*, const char*, void*);
typedef void* (*fn_v)(void);
typedef int (*fn_i)(int);

static const char* REQ_GET =
  "{\"command\":\"QUERY_IPC_EXTENSION_MESSAGE\",\"system\":\"CrimsonNative\","
  "\"module\":\"NvCplDisplayPlugin\",\"method\":\"GetDDSState\",\"payload\":{}}";

static const char* REQ_SET_DGPU =
  "{\"command\":\"QUERY_IPC_EXTENSION_MESSAGE\",\"system\":\"CrimsonNative\","
  "\"module\":\"NvCplDisplayPlugin\",\"method\":\"SetDDSState\","
  "\"payload\":{\"bIsAutomatic\":false,\"MuxState\":2}}";

static const char* REQ_SET_ONLY =
  "{\"bIsAutomatic\":false,\"MuxState\":2}";

static const char* REQ_METHOD_ONLY =
  "{\"method\":\"SetDDSState\",\"payload\":{\"bIsAutomatic\":false,\"MuxState\":2}}";

static void try_call(const char* label, void* fn, const char* req) {
  char buf[256];
  if (!fn || !IsBadCodePtr((FARPROC)fn) == 0 && (uintptr_t)fn < 0x10000) {
    /* IsBadCodePtr inverted logic is messy; just check null/low */
  }
  if (!fn || (uintptr_t)fn < 0x10000) return;

  /* Skip obvious ret-stub: first byte 0xC2/0xC3 */
  unsigned char op = *(unsigned char*)fn;
  if (op == 0xC2 || op == 0xC3) {
    sprintf(buf, "%s skip ret-stub", label);
    logmsg(buf);
    return;
  }

  int st;
  st = ((fn_s)fn)(req);
  sprintf(buf, "%s fn_s(req) -> %d", label, st);
  logmsg(buf);

  st = ((fn_ss)fn)("SetDDSState", req);
  sprintf(buf, "%s fn_ss(method,req) -> %d", label, st);
  logmsg(buf);

  st = ((fn_ss)fn)("NvCplDisplayPlugin", "SetDDSState");
  sprintf(buf, "%s fn_ss(mod,method) -> %d", label, st);
  logmsg(buf);
}

static void try_plugin_methods(void) {
  HMODULE plug = GetModuleHandleA("NvCplDisplayPlugin.dll");
  typedef void* (*getinfo_t)(void);
  getinfo_t gi = (getinfo_t)GetProcAddress(plug, "NvCefPluginGetInfo");
  void** info = (void**)gi();
  char buf[128];
  sprintf(buf, "info=%p ver/ptr0=%p name=%s", (void*)info, info[0], (char*)info[1]);
  logmsg(buf);

  /* Methods at [3..10] */
  for (int i = 3; i <= 10; i++) {
    sprintf(buf, "method[%d]=%p", i, info[i]);
    logmsg(buf);
  }

  /* Prefer larger non-stub methods: [3],[4],[9] looked substantial */
  const char* reqs[] = { REQ_GET, REQ_SET_DGPU, REQ_SET_ONLY, REQ_METHOD_ONLY, "GetDDSState", "SetDDSState", NULL };
  int idxs[] = { 3, 4, 5, 6, 7, 8, 9, 10 };
  for (unsigned ri = 0; reqs[ri]; ri++) {
    for (unsigned ii = 0; ii < sizeof(idxs)/sizeof(idxs[0]); ii++) {
      int i = idxs[ii];
      char label[64];
      sprintf(label, "m%d/r%d", i, ri);
      /* Only try fn_s first to limit crash surface */
      void* fn = info[i];
      if (!fn || (uintptr_t)fn < 0x10000) continue;
      unsigned char op = *(unsigned char*)fn;
      if (op == 0xC2 || op == 0xC3 || op == 0xCC) continue;

      int st = ((fn_s)fn)(reqs[ri]);
      sprintf(buf, "%s -> %d req=%.40s", label, st, reqs[ri]);
      logmsg(buf);
      Sleep(200);
    }
  }
}

/* Call through ICefPluginManager in exe if we can find it - skip */

/* Use Chrome DevTools via browser Mojo - try ExecuteJavaScript on main frame
   by finding CefBrowser from Chromium Widget userdata chain. */
static void try_exec_js_bruteforce(void) {
  HMODULE cef = GetModuleHandleA("libcef.dll");
  if (!cef) { logmsg("no libcef"); return; }
  /* cef_string_utf16_set etc */
  void* p_exec_test = (void*)GetProcAddress(cef, "cef_execute_java_script_with_user_gesture_for_tests");
  char buf[128];
  sprintf(buf, "cef_exec_test=%p", p_exec_test);
  logmsg(buf);

  /* Read payload file */
  char path[MAX_PATH];
  ExpandEnvironmentStringsA("%TEMP%\\inject_dds_payload.js", path, MAX_PATH);
  HANDLE h = CreateFileA(path, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
  const char* js =
    "(()=>{var q={command:'QUERY_IPC_EXTENSION_MESSAGE',system:'CrimsonNative',module:'NvCplDisplayPlugin',method:'SetDDSState',payload:{bIsAutomatic:false,MuxState:2}};"
    "return new Promise((res,rej)=>{if(!window.cefQuery)return res('no_cefQuery');"
    "window.cefQuery({request:JSON.stringify(q),persistent:false,onSuccess:r=>res('ok:'+r),onFailure:(c,e)=>res('fail:'+c+':'+e)});});})()";
  if (h != INVALID_HANDLE_VALUE) {
    DWORD w; WriteFile(h, js, (DWORD)strlen(js), &w, NULL); CloseHandle(h);
  }

  /* Post a custom thread message won't work.
     Use keybd_event to open nothing.
     Instead: find V8 via renderer - inject into renderer process separately. */
  logmsg("JS payload written; need renderer inject for cefQuery");
}

static DWORD WINAPI worker(LPVOID p) {
  (void)p;
  Sleep(800);
  logmsg("=== inject_dds v3 start ===");
  try_plugin_methods();
  try_exec_js_bruteforce();
  logmsg("=== inject_dds v3 done ===");
  return 0;
}

BOOL WINAPI DllMain(HINSTANCE h, DWORD reason, LPVOID r) {
  (void)r;
  if (reason == DLL_PROCESS_ATTACH) {
    DisableThreadLibraryCalls(h);
    HANDLE t = CreateThread(NULL, 0, worker, NULL, 0, NULL);
    if (t) CloseHandle(t);
  }
  return TRUE;
}
