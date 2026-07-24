/*
 * Kill any NVIDIA App, launch stock App with SW_HIDE, inject helper, run cmd.
 * Reports VISIBLE_WINDOWS count — goal is HIT with 0 visible windows.
 *
 * Usage: probe_hidden_host.exe [igpu|auto|dgpu|ping]
 */
#include <windows.h>
#include <tlhelp32.h>
#include <stdio.h>
#include <string.h>

#define PIPE_NAME "\\\\.\\pipe\\AcerPredatorDds"
#define HELPER_NAME L"dds_native_helper.dll"

static DWORD g_hide_pid;
static DWORD g_count_pid;
static int g_count_n;

static BOOL CALLBACK hide_cb(HWND hwnd, LPARAM lp) {
  (void)lp;
  DWORD pid = 0;
  GetWindowThreadProcessId(hwnd, &pid);
  if (pid == g_hide_pid)
    ShowWindow(hwnd, SW_HIDE);
  return TRUE;
}

static BOOL CALLBACK count_cb(HWND hwnd, LPARAM lp) {
  (void)lp;
  DWORD pid = 0;
  GetWindowThreadProcessId(hwnd, &pid);
  if (pid == g_count_pid && IsWindowVisible(hwnd))
    g_count_n++;
  return TRUE;
}

static void hide_pid(DWORD pid) {
  g_hide_pid = pid;
  EnumWindows(hide_cb, 0);
}

static int count_visible(DWORD pid) {
  g_count_pid = pid;
  g_count_n = 0;
  EnumWindows(count_cb, 0);
  return g_count_n;
}

static void resolve_helper(wchar_t* out, size_t n) {
  wchar_t self[MAX_PATH];
  GetModuleFileNameW(NULL, self, MAX_PATH);
  wchar_t* slash = wcsrchr(self, L'\\');
  if (slash)
    *(slash + 1) = 0;
  else
    self[0] = 0;
  swprintf(out, n, L"%s%s", self, HELPER_NAME);
}

static int module_loaded(DWORD pid, const wchar_t* mod) {
  HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
  if (snap == INVALID_HANDLE_VALUE)
    return 0;
  MODULEENTRY32W me;
  me.dwSize = sizeof(me);
  int found = 0;
  if (Module32FirstW(snap, &me)) {
    do {
      if (_wcsicmp(me.szModule, mod) == 0) {
        found = 1;
        break;
      }
    } while (Module32NextW(snap, &me));
  }
  CloseHandle(snap);
  return found;
}

static DWORD find_nvcpl_pid(void) {
  HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
  if (snap == INVALID_HANDLE_VALUE)
    return 0;
  PROCESSENTRY32W pe;
  pe.dwSize = sizeof(pe);
  DWORD found = 0;
  if (Process32FirstW(snap, &pe)) {
    do {
      if (_wcsicmp(pe.szExeFile, L"NVIDIA App.exe") != 0)
        continue;
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

static void hide_all_nvidia_app_windows(void) {
  HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
  if (snap == INVALID_HANDLE_VALUE)
    return;
  PROCESSENTRY32W pe;
  pe.dwSize = sizeof(pe);
  if (Process32FirstW(snap, &pe)) {
    do {
      if (_wcsicmp(pe.szExeFile, L"NVIDIA App.exe") == 0)
        hide_pid(pe.th32ProcessID);
    } while (Process32NextW(snap, &pe));
  }
  CloseHandle(snap);
}

static void kill_nvidia_app(void) {
  HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
  if (snap == INVALID_HANDLE_VALUE)
    return;
  PROCESSENTRY32W pe;
  pe.dwSize = sizeof(pe);
  if (Process32FirstW(snap, &pe)) {
    do {
      if (_wcsicmp(pe.szExeFile, L"NVIDIA App.exe") == 0) {
        HANDLE h = OpenProcess(PROCESS_TERMINATE, FALSE, pe.th32ProcessID);
        if (h) {
          TerminateProcess(h, 0);
          CloseHandle(h);
        }
      }
    } while (Process32NextW(snap, &pe));
  }
  CloseHandle(snap);
}

static DWORD launch_hidden(void) {
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
  return pi.dwProcessId;
}

static int inject(DWORD pid, const wchar_t* dll) {
  HANDLE proc = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
                                PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
                            FALSE, pid);
  if (!proc)
    return 0;
  size_t bytes = (wcslen(dll) + 1) * sizeof(wchar_t);
  LPVOID remote = VirtualAllocEx(proc, NULL, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
  if (!remote) {
    CloseHandle(proc);
    return 0;
  }
  WriteProcessMemory(proc, remote, dll, bytes, NULL);
  FARPROC load = GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "LoadLibraryW");
  HANDLE th = CreateRemoteThread(proc, NULL, 0, (LPTHREAD_START_ROUTINE)load, remote, 0, NULL);
  if (!th) {
    CloseHandle(proc);
    return 0;
  }
  WaitForSingleObject(th, 15000);
  DWORD code = 0;
  GetExitCodeThread(th, &code);
  CloseHandle(th);
  CloseHandle(proc);
  return code != 0;
}

static int pipe_cmd(const char* cmd, char* resp, size_t resp_n) {
  for (int attempt = 0; attempt < 50; attempt++) {
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

int main(int argc, char** argv) {
  const char* cmd = argc > 1 ? argv[1] : "dgpu";
  wchar_t dll[MAX_PATH];
  resolve_helper(dll, MAX_PATH);
  if (GetFileAttributesW(dll) == INVALID_FILE_ATTRIBUTES) {
    wprintf(L"missing helper\n");
    return 5;
  }

  kill_nvidia_app();
  Sleep(1000);
  if (!launch_hidden())
    return 1;

  DWORD pid = 0;
  for (int i = 0; i < 100 && !pid; i++) {
    hide_all_nvidia_app_windows();
    pid = find_nvcpl_pid();
    Sleep(200);
  }
  if (!pid) {
    wprintf(L"NvCpl never loaded\n");
    return 2;
  }
  wprintf(L"nvcpl pid=%lu\n", pid);

  for (int i = 0; i < 30; i++) {
    hide_all_nvidia_app_windows();
    Sleep(50);
  }

  if (!module_loaded(pid, HELPER_NAME)) {
    if (!inject(pid, dll)) {
      wprintf(L"inject failed %lu\n", GetLastError());
      return 3;
    }
    Sleep(500);
  }

  hide_all_nvidia_app_windows();
  char resp[512];
  pipe_cmd(cmd, resp, sizeof(resp));
  hide_all_nvidia_app_windows();
  Sleep(2500);
  hide_all_nvidia_app_windows();

  int vis = 0;
  HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
  if (snap != INVALID_HANDLE_VALUE) {
    PROCESSENTRY32W pe;
    pe.dwSize = sizeof(pe);
    if (Process32FirstW(snap, &pe)) {
      do {
        if (_wcsicmp(pe.szExeFile, L"NVIDIA App.exe") == 0)
          vis += count_visible(pe.th32ProcessID);
      } while (Process32NextW(snap, &pe));
    }
    CloseHandle(snap);
  }

  printf("RESP %s\n", resp);
  printf("VISIBLE_WINDOWS=%d\n", vis);
  return 0;
}
