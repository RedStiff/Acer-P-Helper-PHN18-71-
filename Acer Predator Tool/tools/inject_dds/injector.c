#include <windows.h>
#include <stdio.h>
#include <tlhelp32.h>

static DWORD find_pid(const wchar_t* name) {
  DWORD best = 0;
  HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
  if (snap == INVALID_HANDLE_VALUE) return 0;
  PROCESSENTRY32W pe; pe.dwSize = sizeof(pe);
  if (Process32FirstW(snap, &pe)) {
    do {
      if (_wcsicmp(pe.szExeFile, name) == 0) {
        /* Prefer process with a main window later; take first for now */
        if (!best) best = pe.th32ProcessID;
      }
    } while (Process32NextW(snap, &pe));
  }
  CloseHandle(snap);
  return best;
}

static DWORD find_browser_pid(void) {
  /* NVIDIA App.exe without --type= in command line is browser */
  DWORD pids[64]; int n = 0;
  HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
  PROCESSENTRY32W pe; pe.dwSize = sizeof(pe);
  if (snap == INVALID_HANDLE_VALUE) return 0;
  if (Process32FirstW(snap, &pe)) {
    do {
      if (_wcsicmp(pe.szExeFile, L"NVIDIA App.exe") == 0) {
        if (n < 64) pids[n++] = pe.th32ProcessID;
      }
    } while (Process32NextW(snap, &pe));
  }
  CloseHandle(snap);

  for (int i = 0; i < n; i++) {
    HANDLE h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, FALSE, pids[i]);
    if (!h) continue;
    /* Heuristic: browser has fewer threads of type= ; check modules for NvCplDisplayPlugin */
    CloseHandle(h);
  }

  /* Enumerate modules of each */
  for (int i = 0; i < n; i++) {
    HANDLE snap2 = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pids[i]);
    if (snap2 == INVALID_HANDLE_VALUE) continue;
    MODULEENTRY32W me; me.dwSize = sizeof(me);
    int hasPlugin = 0, hasCef = 0, hasTypeHint = 0;
    if (Module32FirstW(snap2, &me)) {
      do {
        if (_wcsicmp(me.szModule, L"NvCplDisplayPlugin.dll") == 0) hasPlugin = 1;
        if (_wcsicmp(me.szModule, L"libcef.dll") == 0) hasCef = 1;
      } while (Module32NextW(snap2, &me));
    }
    CloseHandle(snap2);
    wprintf(L"pid=%lu plugin=%d cef=%d\n", pids[i], hasPlugin, hasCef);
    if (hasPlugin || (hasCef && i == 0)) {
      /* Prefer plugin-loaded process (browser host) */
      if (hasPlugin) return pids[i];
    }
  }
  /* Fallback: first NVIDIA App pid */
  return n ? pids[0] : 0;
}

int wmain(int argc, wchar_t** argv) {
  const wchar_t* dllPath = L"e:\\Projects\\Acer Predator Tool\\Acer Predator Tool\\tools\\inject_dds\\inject_dds.dll";
  if (argc > 1) dllPath = argv[1];

  DWORD pid = find_browser_pid();
  wprintf(L"target pid=%lu\n", pid);
  if (!pid) return 1;

  HANDLE proc = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
                            PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
                            FALSE, pid);
  if (!proc) {
    wprintf(L"OpenProcess failed %lu\n", GetLastError());
    return 2;
  }

  size_t bytes = (wcslen(dllPath) + 1) * sizeof(wchar_t);
  LPVOID remote = VirtualAllocEx(proc, NULL, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
  if (!remote) {
    wprintf(L"VirtualAllocEx failed %lu\n", GetLastError());
    CloseHandle(proc);
    return 3;
  }
  if (!WriteProcessMemory(proc, remote, dllPath, bytes, NULL)) {
    wprintf(L"WriteProcessMemory failed %lu\n", GetLastError());
    CloseHandle(proc);
    return 4;
  }
  HMODULE k32 = GetModuleHandleW(L"kernel32.dll");
  FARPROC load = GetProcAddress(k32, "LoadLibraryW");
  HANDLE th = CreateRemoteThread(proc, NULL, 0, (LPTHREAD_START_ROUTINE)load, remote, 0, NULL);
  if (!th) {
    wprintf(L"CreateRemoteThread failed %lu\n", GetLastError());
    CloseHandle(proc);
    return 5;
  }
  WaitForSingleObject(th, 15000);
  DWORD code = 0; GetExitCodeThread(th, &code);
  wprintf(L"LoadLibrary remote exit=%p\n", (void*)(uintptr_t)code);
  CloseHandle(th);
  VirtualFreeEx(proc, remote, 0, MEM_RELEASE);
  CloseHandle(proc);
  wprintf(L"done - see %%TEMP%%\\inject_dds.log\n");
  return 0;
}
