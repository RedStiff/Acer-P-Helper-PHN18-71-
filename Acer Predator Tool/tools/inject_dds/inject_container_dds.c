/*
 * inject_container_dds.c — inject dds_container_helper.dll into user nvcontainer,
 * send igpu|auto|dgpu over \\.\pipe\AcerPredatorDdsContainer. No NVIDIA App.
 */
#include <windows.h>
#include <tlhelp32.h>
#include <stdio.h>
#include <string.h>

#define PIPE_NAME "\\\\.\\pipe\\AcerPredatorDdsContainer"
#define HELPER_NAME L"dds_container_helper.dll"

static void resolve_helper(wchar_t* out, size_t n) {
  wchar_t self[MAX_PATH];
  GetModuleFileNameW(NULL, self, MAX_PATH);
  wchar_t* slash = wcsrchr(self, L'\\');
  if (slash) *(slash + 1) = 0; else self[0] = 0;
  swprintf(out, n, L"%s%s", self, HELPER_NAME);
}

static int module_loaded(DWORD pid, const wchar_t* mod) {
  HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
  if (snap == INVALID_HANDLE_VALUE) return 0;
  MODULEENTRY32W me; me.dwSize = sizeof(me);
  int found = 0;
  if (Module32FirstW(snap, &me)) {
    do { if (_wcsicmp(me.szModule, mod) == 0) { found = 1; break; } }
    while (Module32NextW(snap, &me));
  }
  CloseHandle(snap);
  return found;
}

static DWORD find_user_nvcontainer(void) {
  HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
  if (snap == INVALID_HANDLE_VALUE) return 0;
  PROCESSENTRY32W pe; pe.dwSize = sizeof(pe);
  DWORD found = 0, fallback = 0;
  if (Process32FirstW(snap, &pe)) {
    do {
      if (_wcsicmp(pe.szExeFile, L"nvcontainer.exe") != 0) continue;
      DWORD sid = 0;
      ProcessIdToSessionId(pe.th32ProcessID, &sid);
      if (sid == 0) continue;
      if (module_loaded(pe.th32ProcessID, L"NvMessageBus.dll")) { found = pe.th32ProcessID; break; }
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
  if (!proc) { wprintf(L"OpenProcess %lu\n", GetLastError()); return 0; }
  size_t bytes = (wcslen(dll) + 1) * sizeof(wchar_t);
  LPVOID remote = VirtualAllocEx(proc, NULL, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
  if (!remote) { CloseHandle(proc); return 0; }
  WriteProcessMemory(proc, remote, dll, bytes, NULL);
  FARPROC load = GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "LoadLibraryW");
  HANDLE th = CreateRemoteThread(proc, NULL, 0, (LPTHREAD_START_ROUTINE)load, remote, 0, NULL);
  if (!th) { wprintf(L"CreateRemoteThread %lu\n", GetLastError()); CloseHandle(proc); return 0; }
  WaitForSingleObject(th, 15000);
  DWORD code = 0; GetExitCodeThread(th, &code);
  CloseHandle(th); CloseHandle(proc);
  wprintf(L"LoadLibraryW remote=%p\n", (void*)(uintptr_t)code);
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

static void ace(const char* tag) {
  HKEY k;
  if (RegOpenKeyExA(HKEY_LOCAL_MACHINE,
                    "SYSTEM\\CurrentControlSet\\Services\\nvlddmkm\\Global\\NvHybrid\\Persistence\\ACE",
                    0, KEY_READ, &k) != 0) { printf("%s ACE=?\n", tag); return; }
  DWORD state=0,autom=0,i2d=0,sz=sizeof(DWORD),t;
  RegQueryValueExA(k,"InternalMuxState",NULL,&t,(BYTE*)&state,&sz); sz=sizeof(DWORD);
  RegQueryValueExA(k,"InternalMuxIsAutomaticMode",NULL,&t,(BYTE*)&autom,&sz); sz=sizeof(DWORD);
  RegQueryValueExA(k,"ACESwitchedI2D",NULL,&t,(BYTE*)&i2d,&sz);
  RegCloseKey(k);
  printf("%s state=%lu auto=%lu i2d=%lu\n", tag, state, autom, i2d);
}

int main(int argc, char** argv) {
  const char* cmd = argc > 1 ? argv[1] : "ping";
  wchar_t dll[MAX_PATH];
  resolve_helper(dll, MAX_PATH);
  if (GetFileAttributesW(dll) == INVALID_FILE_ATTRIBUTES) {
    wprintf(L"missing %s\n", dll);
    return 5;
  }

  /* Ensure no App dependency for this experiment */
  printf("Note: leave NVIDIA App killed for a clean no-App test.\n");

  DWORD pid = find_user_nvcontainer();
  if (!pid) { printf("no user nvcontainer\n"); return 2; }
  printf("target pid=%lu\n", pid);

  if (!module_loaded(pid, HELPER_NAME)) {
    if (!inject(pid, dll)) return 3;
    Sleep(500);
  } else {
    printf("helper already loaded\n");
  }

  ace("BEFORE");
  char resp[512];
  if (!pipe_cmd(cmd, resp, sizeof(resp))) { printf("%s\n", resp); return 4; }
  printf("RESP %s\n", resp);
  Sleep(2000);
  ace("AFTER");
  return 0;
}
