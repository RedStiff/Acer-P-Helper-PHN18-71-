/*
 * probe_uxd_noapp.c — prove/disprove DDS without NVIDIA App process.
 *
 * Modes:
 *   foreign   — LoadLibrary App nvcpl.dll in THIS process, SetSetting(0x330)+Execute
 *   inject-nvcontainer — inject helper into user-session nvcontainer.exe
 *   status    — App count + UXD event + ACE
 *
 * Usage (admin recommended):
 *   probe_uxd_noapp.exe status
 *   probe_uxd_noapp.exe foreign dgpu|igpu|auto
 *   probe_uxd_noapp.exe inject-nvcontainer dgpu
 */
#include <windows.h>
#include <tlhelp32.h>
#include <stdio.h>
#include <string.h>
#include <stdint.h>

#define SETTING_DDS_MUX 0x330
#define SETTING_ALL 0x10000
#define UXD_EVENT L"Local\\UXDServiceStarted-D40E81C4-06EF-454A-9E81-1F4D55CEBD57"
#define PIPE_NAME "\\\\.\\pipe\\AcerPredatorDds"
#define HELPER_NAME L"dds_native_helper.dll"

typedef int (*NvCplApiSetSetting_t)(int, void*, int, void*);
typedef int (*NvCplApiExecute_t)(int, int, int);
typedef int (*NvCplApiIsUxdServiceRunning_t)(void);

static void ace_print(const char* tag) {
  HKEY k;
  if (RegOpenKeyExA(HKEY_LOCAL_MACHINE,
                    "SYSTEM\\CurrentControlSet\\Services\\nvlddmkm\\Global\\NvHybrid\\Persistence\\ACE",
                    0, KEY_READ, &k) != 0) {
    printf("%s ACE=unavailable\n", tag);
    return;
  }
  DWORD state = 0, autom = 0, i2d = 0, sz = sizeof(DWORD), t;
  RegQueryValueExA(k, "InternalMuxState", NULL, &t, (BYTE*)&state, &sz); sz = sizeof(DWORD);
  RegQueryValueExA(k, "InternalMuxIsAutomaticMode", NULL, &t, (BYTE*)&autom, &sz); sz = sizeof(DWORD);
  RegQueryValueExA(k, "ACESwitchedI2D", NULL, &t, (BYTE*)&i2d, &sz);
  RegCloseKey(k);
  printf("%s ACE state=%lu auto=%lu i2d=%lu\n", tag, state, autom, i2d);
}

static int uxd_open(void) {
  HANDLE h = OpenEventW(SYNCHRONIZE, FALSE, UXD_EVENT);
  if (!h) {
    printf("UXDServiceStarted OpenEvent err=%lu\n", GetLastError());
    return 0;
  }
  printf("UXDServiceStarted OK handle=%p\n", h);
  CloseHandle(h);
  return 1;
}

static int count_proc(const wchar_t* name) {
  HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
  if (snap == INVALID_HANDLE_VALUE) return -1;
  PROCESSENTRY32W pe; pe.dwSize = sizeof(pe);
  int n = 0;
  if (Process32FirstW(snap, &pe)) {
    do {
      if (_wcsicmp(pe.szExeFile, name) == 0) n++;
    } while (Process32NextW(snap, &pe));
  }
  CloseHandle(snap);
  return n;
}

static int kill_named(const wchar_t* name) {
  HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
  if (snap == INVALID_HANDLE_VALUE) return 0;
  PROCESSENTRY32W pe; pe.dwSize = sizeof(pe);
  int killed = 0;
  if (Process32FirstW(snap, &pe)) {
    do {
      if (_wcsicmp(pe.szExeFile, name) != 0) continue;
      HANDLE p = OpenProcess(PROCESS_TERMINATE, FALSE, pe.th32ProcessID);
      if (p) {
        if (TerminateProcess(p, 0)) killed++;
        CloseHandle(p);
      }
    } while (Process32NextW(snap, &pe));
  }
  CloseHandle(snap);
  return killed;
}

static int parse_mode(const char* s, int* mux, int* autom) {
  if (_strnicmp(s, "igpu", 4) == 0 || _strnicmp(s, "optimus", 7) == 0) { *mux = 1; *autom = 0; return 1; }
  if (_strnicmp(s, "auto", 4) == 0) { *mux = 1; *autom = 1; return 1; }
  if (_strnicmp(s, "dgpu", 4) == 0 || _strnicmp(s, "nvidia", 6) == 0) { *mux = 2; *autom = 0; return 1; }
  return 0;
}

static int foreign_set(int mux, int autom) {
  /* Prefer App NvCpl path (same binaries the App uses). */
  SetDllDirectoryW(L"C:\\Program Files\\NVIDIA Corporation\\NVIDIA App\\NvCpl");
  HMODULE nvcpl = LoadLibraryW(L"C:\\Program Files\\NVIDIA Corporation\\NVIDIA App\\NvCpl\\nvcpl.dll");
  if (!nvcpl) {
    printf("LoadLibrary nvcpl failed %lu\n", GetLastError());
    return 0;
  }
  NvCplApiIsUxdServiceRunning_t isuxd =
      (NvCplApiIsUxdServiceRunning_t)GetProcAddress(nvcpl, "NvCplApiIsUxdServiceRunning");
  NvCplApiSetSetting_t set = (NvCplApiSetSetting_t)GetProcAddress(nvcpl, "NvCplApiSetSetting");
  NvCplApiExecute_t exec = (NvCplApiExecute_t)GetProcAddress(nvcpl, "NvCplApiExecute");
  printf("exports isuxd=%p set=%p exec=%p\n", isuxd, set, exec);
  if (isuxd) printf("NvCplApiIsUxdServiceRunning -> %d\n", isuxd());
  if (!set || !exec) return 0;

  unsigned char block[0x40];
  memset(block, 0, sizeof(block));
  unsigned char* val = block + 0x10;
  *(uint32_t*)val = 0x01u | ((autom ? 1u : 0u) << 8);
  *(uint32_t*)(val + 4) = (uint32_t)mux;

  ace_print("BEFORE");
  int set_ret = set(1, block, SETTING_DDS_MUX, val);
  int exec_ret = exec(SETTING_ALL, 0, -1);
  printf("SetSetting=%d Execute=%d mux=%d auto=%d\n", set_ret, exec_ret, mux, autom);
  Sleep(2000);
  ace_print("AFTER");
  return (set_ret == 0 && exec_ret == 0);
}

static int module_loaded(DWORD pid, const wchar_t* mod) {
  HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
  if (snap == INVALID_HANDLE_VALUE) return 0;
  MODULEENTRY32W me; me.dwSize = sizeof(me);
  int found = 0;
  if (Module32FirstW(snap, &me)) {
    do {
      if (_wcsicmp(me.szModule, mod) == 0) { found = 1; break; }
    } while (Module32NextW(snap, &me));
  }
  CloseHandle(snap);
  return found;
}

static DWORD find_user_nvcontainer(void) {
  /* Prefer session nvcontainer that already has NvMessageBus (user plugins). */
  HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
  if (snap == INVALID_HANDLE_VALUE) return 0;
  PROCESSENTRY32W pe; pe.dwSize = sizeof(pe);
  DWORD found = 0, fallback = 0;
  if (Process32FirstW(snap, &pe)) {
    do {
      if (_wcsicmp(pe.szExeFile, L"nvcontainer.exe") != 0) continue;
      DWORD sid = 0;
      HANDLE p = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pe.th32ProcessID);
      if (p) {
        /* SessionId via ProcessIdToSessionId */
        ProcessIdToSessionId(pe.th32ProcessID, &sid);
        CloseHandle(p);
      }
      if (sid == 0) continue; /* skip session-0 SYSTEM containers */
      if (module_loaded(pe.th32ProcessID, L"NvMessageBus.dll")) {
        found = pe.th32ProcessID;
        break;
      }
      if (!fallback) fallback = pe.th32ProcessID;
    } while (Process32NextW(snap, &pe));
  }
  CloseHandle(snap);
  return found ? found : fallback;
}

static int inject(DWORD pid, const wchar_t* dll) {
  HANDLE proc = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
                                PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
                            FALSE, pid);
  if (!proc) { printf("OpenProcess %lu\n", GetLastError()); return 0; }
  size_t bytes = (wcslen(dll) + 1) * sizeof(wchar_t);
  LPVOID remote = VirtualAllocEx(proc, NULL, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
  if (!remote) { CloseHandle(proc); return 0; }
  WriteProcessMemory(proc, remote, dll, bytes, NULL);
  FARPROC load = GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "LoadLibraryW");
  HANDLE th = CreateRemoteThread(proc, NULL, 0, (LPTHREAD_START_ROUTINE)load, remote, 0, NULL);
  if (!th) { printf("CreateRemoteThread %lu\n", GetLastError()); CloseHandle(proc); return 0; }
  WaitForSingleObject(th, 15000);
  DWORD code = 0; GetExitCodeThread(th, &code);
  CloseHandle(th); CloseHandle(proc);
  printf("LoadLibraryW remote=%p\n", (void*)(uintptr_t)code);
  return code != 0;
}

static int pipe_cmd(const char* cmd, char* resp, size_t resp_n) {
  for (int i = 0; i < 50; i++) {
    HANDLE pipe = CreateFileA(PIPE_NAME, GENERIC_READ | GENERIC_WRITE, 0, NULL, OPEN_EXISTING, 0, NULL);
    if (pipe != INVALID_HANDLE_VALUE) {
      DWORD mode = PIPE_READMODE_MESSAGE;
      SetNamedPipeHandleState(pipe, &mode, NULL, NULL);
      DWORD w = 0; WriteFile(pipe, cmd, (DWORD)strlen(cmd), &w, NULL);
      DWORD r = 0; memset(resp, 0, resp_n);
      ReadFile(pipe, resp, (DWORD)resp_n - 1, &r, NULL);
      CloseHandle(pipe);
      return 1;
    }
    Sleep(100);
  }
  snprintf(resp, resp_n, "ERR|pipe timeout");
  return 0;
}

static void resolve_helper(wchar_t* out, size_t n) {
  wchar_t self[MAX_PATH];
  GetModuleFileNameW(NULL, self, MAX_PATH);
  wchar_t* slash = wcsrchr(self, L'\\');
  if (slash) *(slash + 1) = 0; else self[0] = 0;
  swprintf(out, n, L"%s%s", self, HELPER_NAME);
}

/*
 * Helper for nvcontainer path: same dds_native_helper only works if NvCpl already loaded.
 * For nvcontainer we need a different helper that LoadLibrary's nvcpl first.
 * This probe only attempts inject of existing helper to see ping result.
 */
static int inject_nvcontainer(const char* cmd) {
  wchar_t dll[MAX_PATH];
  resolve_helper(dll, MAX_PATH);
  if (GetFileAttributesW(dll) == INVALID_FILE_ATTRIBUTES) {
    wprintf(L"missing %s\n", dll);
    return 0;
  }
  DWORD pid = find_user_nvcontainer();
  if (!pid) { printf("no user-session nvcontainer\n"); return 0; }
  printf("target nvcontainer pid=%lu\n", pid);
  if (!module_loaded(pid, HELPER_NAME)) {
    if (!inject(pid, dll)) return 0;
    Sleep(400);
  }
  char resp[512];
  pipe_cmd("ping", resp, sizeof(resp));
  printf("PING %s\n", resp);
  ace_print("BEFORE");
  pipe_cmd(cmd, resp, sizeof(resp));
  printf("RESP %s\n", resp);
  Sleep(2000);
  ace_print("AFTER");
  return 1;
}

int main(int argc, char** argv) {
  const char* mode = argc > 1 ? argv[1] : "status";
  const char* arg = argc > 2 ? argv[2] : "dgpu";

  printf("NVIDIA App count=%d\n", count_proc(L"NVIDIA App.exe"));
  uxd_open();
  ace_print("NOW");

  if (_stricmp(mode, "status") == 0)
    return 0;

  if (_stricmp(mode, "kill-app") == 0) {
    int k = kill_named(L"NVIDIA App.exe");
    printf("killed App=%d\n", k);
    Sleep(1500);
    printf("App left=%d\n", count_proc(L"NVIDIA App.exe"));
    uxd_open();
    return 0;
  }

  if (_stricmp(mode, "foreign") == 0) {
    int mux = 2, autom = 0;
    if (!parse_mode(arg, &mux, &autom)) { printf("bad mode\n"); return 2; }
    /* ensure App dead so we are not cheating */
    kill_named(L"NVIDIA App.exe");
    Sleep(1000);
    printf("App after kill=%d\n", count_proc(L"NVIDIA App.exe"));
    uxd_open();
    foreign_set(mux, autom);
    return 0;
  }

  if (_stricmp(mode, "inject-nvcontainer") == 0) {
    kill_named(L"NVIDIA App.exe");
    Sleep(1000);
    printf("App after kill=%d\n", count_proc(L"NVIDIA App.exe"));
    inject_nvcontainer(arg);
    return 0;
  }

  printf("usage: status | kill-app | foreign <mode> | inject-nvcontainer <mode>\n");
  return 1;
}
