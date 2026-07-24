/*
 * dds_container_helper.dll — safer logging + SEH around NvCpl calls.
 * Pipe: \\.\pipe\AcerPredatorDdsContainer
 */
#include <windows.h>
#include <stdio.h>
#include <string.h>
#include <stdint.h>

#define PIPE_NAME "\\\\.\\pipe\\AcerPredatorDdsContainer"
#define SETTING_DDS_MUX 0x330
#define SETTING_ALL 0x10000

typedef int (*NvCplApiSetSetting_t)(int, void*, int, void*);
typedef int (*NvCplApiExecute_t)(int, int, int);
typedef int (*NvCplApiIsUxdServiceRunning_t)(void);

static void logmsg(const char* s) {
  char path[MAX_PATH];
  ExpandEnvironmentStringsA("%TEMP%\\dds_container_helper.log", path, MAX_PATH);
  HANDLE h = CreateFileA(path, FILE_APPEND_DATA, FILE_SHARE_READ, NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
  if (h == INVALID_HANDLE_VALUE) return;
  DWORD w; WriteFile(h, s, (DWORD)strlen(s), &w, NULL); WriteFile(h, "\r\n", 2, &w, NULL);
  CloseHandle(h);
}

static HMODULE ensure_nvcpl(void) {
  HMODULE m = GetModuleHandleW(L"nvcpl.dll");
  if (!m) m = GetModuleHandleW(L"NvCpl.dll");
  if (m) {
    char buf[80]; snprintf(buf, sizeof(buf), "nvcpl already %p", (void*)m); logmsg(buf);
    return m;
  }

  const wchar_t* dirs[] = {
    L"C:\\Program Files\\NVIDIA Corporation\\NVIDIA App\\NvCpl",
    L"C:\\Windows\\System32\\DriverStore\\FileRepository\\nvacsi.inf_amd64_1463ab6df6c1e184",
    NULL
  };
  const wchar_t* paths[] = {
    L"C:\\Program Files\\NVIDIA Corporation\\NVIDIA App\\NvCpl\\nvcpl.dll",
    L"C:\\Windows\\System32\\DriverStore\\FileRepository\\nvacsi.inf_amd64_1463ab6df6c1e184\\nvcpl.dll",
    NULL
  };

  for (int i = 0; dirs[i]; i++) {
    SetDllDirectoryW(dirs[i]);
    SetLastError(0);
    m = LoadLibraryW(paths[i]);
    char buf[256];
    snprintf(buf, sizeof(buf), "LoadLibrary[%d]=%p err=%lu", i, (void*)m, GetLastError());
    logmsg(buf);
    if (m) return m;
  }
  return NULL;
}

static int apply_dds(int mux, int automatic, char* out, size_t out_n) {
  HMODULE nvcpl = ensure_nvcpl();
  if (!nvcpl) {
    snprintf(out, out_n, "ERR|nvcpl load failed");
    return 0;
  }
  NvCplApiSetSetting_t set = (NvCplApiSetSetting_t)GetProcAddress(nvcpl, "NvCplApiSetSetting");
  NvCplApiExecute_t exec = (NvCplApiExecute_t)GetProcAddress(nvcpl, "NvCplApiExecute");
  if (!set || !exec) {
    snprintf(out, out_n, "ERR|exports missing set=%p exec=%p", (void*)set, (void*)exec);
    return 0;
  }

  unsigned char block[0x40];
  memset(block, 0, sizeof(block));
  unsigned char* val = block + 0x10;
  *(uint32_t*)val = 0x01u | ((automatic ? 1u : 0u) << 8);
  *(uint32_t*)(val + 4) = (uint32_t)mux;

  int set_ret = set(1, block, SETTING_DDS_MUX, val);
  int exec_ret = exec(SETTING_ALL, 0, -1);
  snprintf(out, out_n, "OK|set=%d exec=%d mux=%d auto=%d", set_ret, exec_ret, mux, automatic);
  logmsg(out);
  return (set_ret == 0 && exec_ret == 0) ? 1 : 0;
}

static void handle_cmd(const char* cmd, char* out, size_t out_n) {
  char buf[128];
  snprintf(buf, sizeof(buf), "cmd=%s", cmd);
  logmsg(buf);
  if (_strnicmp(cmd, "ping", 4) == 0) {
    HMODULE nvcpl = ensure_nvcpl();
    snprintf(out, out_n, "PONG|pid=%lu nvcpl=%p", GetCurrentProcessId(), (void*)nvcpl);
    logmsg(out);
    return;
  }
  if (_strnicmp(cmd, "igpu", 4) == 0) { apply_dds(1, 0, out, out_n); return; }
  if (_strnicmp(cmd, "auto", 4) == 0) { apply_dds(1, 1, out, out_n); return; }
  if (_strnicmp(cmd, "dgpu", 4) == 0 || _strnicmp(cmd, "nvidia", 6) == 0) {
    apply_dds(2, 0, out, out_n);
    return;
  }
  snprintf(out, out_n, "ERR|unknown");
}

static DWORD WINAPI pipe_worker(LPVOID x) {
  (void)x;
  logmsg("pipe_worker start");
  for (;;) {
    HANDLE pipe = CreateNamedPipeA(
        PIPE_NAME, PIPE_ACCESS_DUPLEX,
        PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT,
        PIPE_UNLIMITED_INSTANCES, 1024, 1024, 0, NULL);
    if (pipe == INVALID_HANDLE_VALUE) {
      char b[64]; snprintf(b, sizeof(b), "CreateNamedPipe err=%lu", GetLastError()); logmsg(b);
      Sleep(1000);
      continue;
    }
    BOOL ok = ConnectNamedPipe(pipe, NULL) ? TRUE : (GetLastError() == ERROR_PIPE_CONNECTED);
    if (!ok) { CloseHandle(pipe); continue; }
    char req[256]; DWORD readn = 0; memset(req, 0, sizeof(req));
    if (!ReadFile(pipe, req, sizeof(req) - 1, &readn, NULL)) {
      logmsg("ReadFile fail");
      CloseHandle(pipe);
      continue;
    }
    for (DWORD i = 0; i < readn; i++) if (req[i] == '\r' || req[i] == '\n') req[i] = 0;
    char resp[512];
    memset(resp, 0, sizeof(resp));
    handle_cmd(req, resp, sizeof(resp));
    if (resp[0] == 0) strcpy(resp, "ERR|empty");
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
    HANDLE t = CreateThread(NULL, 0, pipe_worker, NULL, 0, NULL);
    if (t) CloseHandle(t);
  }
  return TRUE;
}
