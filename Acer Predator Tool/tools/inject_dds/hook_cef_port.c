/*
 * Injected early into NVIDIA App.
 * Hooks cef_initialize and sets remote_debugging_port=9333.
 * UI is suppressed by launching on a private Win32 desktop (see launcher.c).
 */
#include <windows.h>
#include <stdio.h>
#include <stdint.h>
#include <string.h>

#define CDP_PORT 9333
#define PORT_OFF 336

static void logmsg(const char* s) {
  char path[MAX_PATH];
  ExpandEnvironmentStringsA("%TEMP%\\hook_cef_port.log", path, MAX_PATH);
  HANDLE h = CreateFileA(path, FILE_APPEND_DATA, FILE_SHARE_READ, NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
  if (h == INVALID_HANDLE_VALUE) return;
  DWORD w;
  WriteFile(h, s, (DWORD)strlen(s), &w, NULL);
  WriteFile(h, "\r\n", 2, &w, NULL);
  CloseHandle(h);
}

typedef int (*cef_initialize_t)(const void* args, void* settings, void* app, void* sandbox);
static cef_initialize_t real_cef_initialize = NULL;
static BYTE stolen[16];
static void* hook_addr = NULL;

static int __cdecl hooked_cef_initialize(const void* args, void* settings, void* app, void* sandbox) {
  char buf[128];
  if (settings) {
    DWORD old;
    VirtualProtect((BYTE*)settings + PORT_OFF, 4, PAGE_READWRITE, &old);
    int before = *(int*)((BYTE*)settings + PORT_OFF);
    *(int*)((BYTE*)settings + PORT_OFF) = CDP_PORT;
    int after = *(int*)((BYTE*)settings + PORT_OFF);
    VirtualProtect((BYTE*)settings + PORT_OFF, 4, old, &old);
    sprintf(buf, "cef_initialize port %d -> %d size=%u", before, after, *(unsigned*)settings);
    logmsg(buf);
  } else {
    logmsg("cef_initialize settings=null");
  }

  DWORD old;
  VirtualProtect(hook_addr, sizeof(stolen), PAGE_EXECUTE_READWRITE, &old);
  memcpy(hook_addr, stolen, sizeof(stolen));
  VirtualProtect(hook_addr, sizeof(stolen), old, &old);
  FlushInstructionCache(GetCurrentProcess(), hook_addr, sizeof(stolen));

  int ret = real_cef_initialize(args, settings, app, sandbox);
  sprintf(buf, "cef_initialize ret=%d", ret);
  logmsg(buf);
  return ret;
}

static int install_hook(void) {
  HMODULE cef = GetModuleHandleA("libcef.dll");
  if (!cef) return 0;
  void* p = (void*)GetProcAddress(cef, "cef_initialize");
  if (!p) {
    logmsg("no cef_initialize export");
    return 0;
  }
  if (hook_addr) return 1;

  hook_addr = p;
  real_cef_initialize = (cef_initialize_t)p;
  memcpy(stolen, p, sizeof(stolen));

  BYTE tramp[12];
  tramp[0] = 0x48;
  tramp[1] = 0xB8;
  *(void**)(tramp + 2) = (void*)hooked_cef_initialize;
  tramp[10] = 0xFF;
  tramp[11] = 0xE0;

  DWORD old;
  VirtualProtect(p, sizeof(tramp), PAGE_EXECUTE_READWRITE, &old);
  memcpy(p, tramp, sizeof(tramp));
  VirtualProtect(p, sizeof(tramp), old, &old);
  FlushInstructionCache(GetCurrentProcess(), p, sizeof(tramp));
  logmsg("hook installed on cef_initialize");
  return 1;
}

static DWORD WINAPI worker(LPVOID x) {
  (void)x;
  logmsg("hook_cef_port worker start (desktop-isolated)");
  for (int i = 0; i < 200; i++) {
    if (install_hook()) break;
    Sleep(25);
  }
  if (!hook_addr) logmsg("FAILED to install hook");
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
