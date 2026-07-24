/*
 * dds_native_helper.dll — inject into NVIDIA App that already has NvCpl.dll + UXD.
 * Named pipe: \\.\pipe\AcerPredatorDds
 * Client writes one line: igpu | auto | dgpu | ping
 *
 * Also keeps all windows of this process hidden (background DDS host, no UI).
 * No CDP. Uses NvCplApiSetSetting(0x330) + NvCplApiExecute(0x10000,0,-1).
 */
#include <windows.h>
#include <stdio.h>
#include <string.h>
#include <stdint.h>

#define PIPE_NAME "\\\\.\\pipe\\AcerPredatorDds"
#define SETTING_DDS_MUX 0x330
#define SETTING_ALL 0x10000

typedef int (*NvCplApiSetSetting_t)(int, void*, int, void*);
typedef int (*NvCplApiExecute_t)(int, int, int);

static void logmsg(const char* s) {
  char path[MAX_PATH];
  ExpandEnvironmentStringsA("%TEMP%\\dds_native_helper.log", path, MAX_PATH);
  HANDLE h = CreateFileA(path, FILE_APPEND_DATA, FILE_SHARE_READ, NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
  if (h == INVALID_HANDLE_VALUE)
    return;
  DWORD w;
  WriteFile(h, s, (DWORD)strlen(s), &w, NULL);
  WriteFile(h, "\r\n", 2, &w, NULL);
  CloseHandle(h);
}

static int apply_dds(int mux, int automatic, char* out, size_t out_n) {
  HMODULE nvcpl = GetModuleHandleA("NvCpl.dll");
  if (!nvcpl)
    nvcpl = GetModuleHandleA("nvcpl.dll");
  if (!nvcpl) {
    snprintf(out, out_n, "ERR|NvCpl.dll not loaded in host");
    return 0;
  }

  NvCplApiSetSetting_t set = (NvCplApiSetSetting_t)GetProcAddress(nvcpl, "NvCplApiSetSetting");
  NvCplApiExecute_t exec = (NvCplApiExecute_t)GetProcAddress(nvcpl, "NvCplApiExecute");
  if (!set || !exec) {
    snprintf(out, out_n, "ERR|exports missing");
    return 0;
  }

  unsigned char block[0x40];
  memset(block, 0, sizeof(block));
  unsigned char* val = block + 0x10;
  *(uint32_t*)val = 0x01u | ((automatic ? 1u : 0u) << 8);
  *(uint32_t*)(val + 4) = (uint32_t)mux;
  *(uint32_t*)(val + 8) = 0;
  *(uint32_t*)(val + 12) = 0;

  int set_ret = set(1, block, SETTING_DDS_MUX, val);
  int exec_ret = exec(SETTING_ALL, 0, -1);
  snprintf(out, out_n, "OK|set=%d exec=%d mux=%d auto=%d", set_ret, exec_ret, mux, automatic);
  char buf[128];
  snprintf(buf, sizeof(buf), "apply mux=%d auto=%d set=%d exec=%d", mux, automatic, set_ret, exec_ret);
  logmsg(buf);
  return (set_ret == 0 && exec_ret == 0) ? 1 : 0;
}

static void handle_cmd(const char* cmd, char* out, size_t out_n) {
  if (_strnicmp(cmd, "igpu", 4) == 0)
    apply_dds(1, 0, out, out_n);
  else if (_strnicmp(cmd, "auto", 4) == 0)
    apply_dds(1, 1, out, out_n);
  else if (_strnicmp(cmd, "dgpu", 4) == 0 || _strnicmp(cmd, "nvidia", 6) == 0)
    apply_dds(2, 0, out, out_n);
  else if (_strnicmp(cmd, "ping", 4) == 0) {
    HMODULE nvcpl = GetModuleHandleA("NvCpl.dll");
    if (!nvcpl)
      nvcpl = GetModuleHandleA("nvcpl.dll");
    snprintf(out, out_n, "PONG|nvcpl=%p pid=%lu", (void*)nvcpl, GetCurrentProcessId());
  } else {
    snprintf(out, out_n, "ERR|unknown cmd (igpu|auto|dgpu|ping)");
  }
}

static BOOL CALLBACK hide_self_cb(HWND hwnd, LPARAM lp) {
  (void)lp;
  DWORD pid = 0;
  GetWindowThreadProcessId(hwnd, &pid);
  if (pid == GetCurrentProcessId())
    ShowWindow(hwnd, SW_HIDE);
  return TRUE;
}

static DWORD WINAPI hide_worker(LPVOID x) {
  (void)x;
  /* Hide CEF chrome during warm-up only — do not permanently block user opening NVIDIA App. */
  logmsg("hide_worker start (20s)");
  for (int i = 0; i < 200; i++) {
    EnumWindows(hide_self_cb, 0);
    Sleep(100);
  }
  logmsg("hide_worker stop");
  return 0;
}

static DWORD WINAPI pipe_worker(LPVOID x) {
  (void)x;
  logmsg("pipe_worker start");
  for (;;) {
    HANDLE pipe = CreateNamedPipeA(
        PIPE_NAME,
        PIPE_ACCESS_DUPLEX,
        PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT,
        1, 1024, 1024, 0, NULL);
    if (pipe == INVALID_HANDLE_VALUE) {
      logmsg("CreateNamedPipe failed");
      Sleep(1000);
      continue;
    }
    BOOL ok = ConnectNamedPipe(pipe, NULL) ? TRUE : (GetLastError() == ERROR_PIPE_CONNECTED);
    if (!ok) {
      CloseHandle(pipe);
      continue;
    }

    char req[256];
    DWORD readn = 0;
    memset(req, 0, sizeof(req));
    if (!ReadFile(pipe, req, sizeof(req) - 1, &readn, NULL)) {
      CloseHandle(pipe);
      continue;
    }
    for (DWORD i = 0; i < readn; i++) {
      if (req[i] == '\r' || req[i] == '\n')
        req[i] = 0;
    }
    char resp[256];
    handle_cmd(req, resp, sizeof(resp));
    DWORD written = 0;
    WriteFile(pipe, resp, (DWORD)strlen(resp), &written, NULL);
    FlushFileBuffers(pipe);
    DisconnectNamedPipe(pipe);
    CloseHandle(pipe);
  }
  return 0;
}

BOOL WINAPI DllMain(HINSTANCE h, DWORD reason, LPVOID r) {
  (void)r;
  if (reason == DLL_PROCESS_ATTACH) {
    DisableThreadLibraryCalls(h);
    logmsg("DllMain attach");
    HANDLE t1 = CreateThread(NULL, 0, hide_worker, NULL, 0, NULL);
    HANDLE t2 = CreateThread(NULL, 0, pipe_worker, NULL, 0, NULL);
    if (t1)
      CloseHandle(t1);
    if (t2)
      CloseHandle(t2);
  }
  return TRUE;
}
