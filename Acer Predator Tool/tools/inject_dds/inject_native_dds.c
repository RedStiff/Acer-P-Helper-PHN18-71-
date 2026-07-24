/*
 * inject_native_dds.c — inject dds_native_helper.dll into NVIDIA App with NvCpl,
 * then send a pipe command (igpu|auto|dgpu|ping).
 *
 * Usage:
 *   inject_native_dds.exe ping
 *   inject_native_dds.exe dgpu
 *   inject_native_dds.exe --launch auto   (start stock App if needed)
 */
#include <windows.h>
#include <tlhelp32.h>
#include <stdio.h>
#include <string.h>

#define PIPE_NAME "\\\\.\\pipe\\AcerPredatorDds"
#define HELPER_NAME L"dds_native_helper.dll"

static void resolve_helper(wchar_t* out, size_t n) {
  wchar_t self[MAX_PATH];
  GetModuleFileNameW(NULL, self, MAX_PATH);
  wchar_t* slash = wcsrchr(self, L'\\');
  if (slash) *(slash + 1) = 0;
  else self[0] = 0;
  swprintf(out, n, L"%s%s", self, HELPER_NAME);
}

static int module_loaded(DWORD pid, const wchar_t* mod) {
  HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
  if (snap == INVALID_HANDLE_VALUE) return 0;
  MODULEENTRY32W me;
  me.dwSize = sizeof(me);
  int found = 0;
  if (Module32FirstW(snap, &me)) {
    do {
      if (_wcsicmp(me.szModule, mod) == 0) { found = 1; break; }
    } while (Module32NextW(snap, &me));
  }
  CloseHandle(snap);
  return found;
}

static DWORD find_nvcpl_pid(void) {
  HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
  if (snap == INVALID_HANDLE_VALUE) return 0;
  PROCESSENTRY32W pe;
  pe.dwSize = sizeof(pe);
  DWORD found = 0;
  if (Process32FirstW(snap, &pe)) {
    do {
      if (_wcsicmp(pe.szExeFile, L"NVIDIA App.exe") != 0) continue;
      if (module_loaded(pe.th32ProcessID, L"NvCpl.dll") ||
          module_loaded(pe.th32ProcessID, L"nvcpl.dll")) {
        found = pe.th32ProcessID;
        break;
      }
    } while (Process32NextW(snap, &pe));
  }
  CloseHandle(snap);
  return found;
}

static int inject(DWORD pid, const wchar_t* dll) {
  HANDLE proc = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
                                PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
                            FALSE, pid);
  if (!proc) {
    wprintf(L"OpenProcess %lu\n", GetLastError());
    return 0;
  }
  size_t bytes = (wcslen(dll) + 1) * sizeof(wchar_t);
  LPVOID remote = VirtualAllocEx(proc, NULL, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
  if (!remote) {
    wprintf(L"VirtualAllocEx %lu\n", GetLastError());
    CloseHandle(proc);
    return 0;
  }
  if (!WriteProcessMemory(proc, remote, dll, bytes, NULL)) {
    wprintf(L"WriteProcessMemory %lu\n", GetLastError());
    CloseHandle(proc);
    return 0;
  }
  FARPROC load = GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "LoadLibraryW");
  HANDLE th = CreateRemoteThread(proc, NULL, 0, (LPTHREAD_START_ROUTINE)load, remote, 0, NULL);
  if (!th) {
    wprintf(L"CreateRemoteThread %lu\n", GetLastError());
    CloseHandle(proc);
    return 0;
  }
  WaitForSingleObject(th, 15000);
  DWORD code = 0;
  GetExitCodeThread(th, &code);
  CloseHandle(th);
  CloseHandle(proc);
  wprintf(L"LoadLibraryW remote=%p\n", (void*)(uintptr_t)code);
  return code != 0;
}

static int pipe_cmd(const char* cmd, char* resp, size_t resp_n) {
  for (int attempt = 0; attempt < 40; attempt++) {
    HANDLE pipe = CreateFileA(PIPE_NAME, GENERIC_READ | GENERIC_WRITE, 0, NULL, OPEN_EXISTING, 0, NULL);
    if (pipe != INVALID_HANDLE_VALUE) {
      DWORD mode = PIPE_READMODE_MESSAGE;
      SetNamedPipeHandleState(pipe, &mode, NULL, NULL);
      DWORD w = 0;
      WriteFile(pipe, cmd, (DWORD)strlen(cmd), &w, NULL);
      DWORD r = 0;
      memset(resp, 0, resp_n);
      ReadFile(pipe, resp, (DWORD)resp_n - 1, &r, NULL);
      CloseHandle(pipe);
      return 1;
    }
    Sleep(100);
  }
  snprintf(resp, resp_n, "ERR|pipe timeout");
  return 0;
}

static int launch_stock_app(void) {
  const wchar_t* exe = L"C:\\Program Files\\NVIDIA Corporation\\NVIDIA App\\CEF\\NVIDIA App.exe";
  const wchar_t* cwd = L"C:\\Program Files\\NVIDIA Corporation\\NVIDIA App\\CEF";
  STARTUPINFOW si;
  ZeroMemory(&si, sizeof(si));
  si.cb = sizeof(si);
  si.dwFlags = STARTF_USESHOWWINDOW;
  si.wShowWindow = SW_HIDE;
  PROCESS_INFORMATION pi;
  ZeroMemory(&pi, sizeof(pi));
  wchar_t cmd[1024];
  swprintf(cmd, 1024, L"\"%s\"", exe);
  if (!CreateProcessW(exe, cmd, NULL, NULL, FALSE, 0, NULL, cwd, &si, &pi)) {
    wprintf(L"CreateProcess failed %lu\n", GetLastError());
    return 0;
  }
  wprintf(L"launched hidden pid=%lu\n", pi.dwProcessId);
  CloseHandle(pi.hThread);
  CloseHandle(pi.hProcess);
  return 1;
}

int main(int argc, char** argv) {
  const char* cmd = "ping";
  for (int i = 1; i < argc; i++) {
    if (strcmp(argv[i], "--launch") == 0)
      continue; /* always auto-launch when needed */
    else
      cmd = argv[i];
  }

  wchar_t dll[MAX_PATH];
  resolve_helper(dll, MAX_PATH);
  if (GetFileAttributesW(dll) == INVALID_FILE_ATTRIBUTES) {
    wprintf(L"missing %s\n", dll);
    return 5;
  }

  DWORD pid = find_nvcpl_pid();
  if (!pid) {
    /* Default product path: start stock App hidden (helper keeps windows hidden). */
    if (!launch_stock_app()) return 1;
    for (int i = 0; i < 90 && !pid; i++) {
      Sleep(500);
      pid = find_nvcpl_pid();
    }
  }
  if (!pid) {
    wprintf(L"No NVIDIA App with NvCpl.dll after hidden launch\n");
    return 2;
  }
  wprintf(L"target pid=%lu\n", pid);

  if (!module_loaded(pid, L"dds_native_helper.dll")) {
    if (!inject(pid, dll)) return 3;
    Sleep(300);
  } else {
    wprintf(L"helper already loaded\n");
  }

  char resp[512];
  if (!pipe_cmd(cmd, resp, sizeof(resp))) {
    printf("%s\n", resp);
    return 4;
  }
  printf("RESP %s\n", resp);
  return 0;
}
