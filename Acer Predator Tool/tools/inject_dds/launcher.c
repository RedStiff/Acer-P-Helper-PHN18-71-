#include <windows.h>
#include <stdio.h>

/* Launch NVIDIA App with CDP hook on the *interactive* desktop.
 * Do NOT use a private desktop — that steals the single-instance App from the user.
 * Window is started hidden; user tray/open can still activate it later. */

static void resolve_hook_dll(wchar_t* out, size_t out_chars) {
  wchar_t self[MAX_PATH];
  DWORD n = GetModuleFileNameW(NULL, self, MAX_PATH);
  if (n == 0 || n >= MAX_PATH) {
    out[0] = L'\0';
    return;
  }

  wchar_t* slash = wcsrchr(self, L'\\');
  if (!slash)
    slash = wcsrchr(self, L'/');
  if (slash)
    *(slash + 1) = L'\0';
  else
    self[0] = L'\0';

  swprintf(out, out_chars, L"%shook_cef_port.dll", self);
}

int wmain(void) {
  const wchar_t* exe = L"C:\\Program Files\\NVIDIA Corporation\\NVIDIA App\\CEF\\NVIDIA App.exe";
  const wchar_t* cwd = L"C:\\Program Files\\NVIDIA Corporation\\NVIDIA App\\CEF";
  wchar_t dll[MAX_PATH];
  resolve_hook_dll(dll, MAX_PATH);
  if (dll[0] == L'\0' || GetFileAttributesW(dll) == INVALID_FILE_ATTRIBUTES) {
    wprintf(L"hook_cef_port.dll missing next to launcher\n");
    return 5;
  }

  STARTUPINFOW si;
  ZeroMemory(&si, sizeof(si));
  si.cb = sizeof(si);
  si.dwFlags = STARTF_USESHOWWINDOW;
  si.wShowWindow = SW_HIDE;

  PROCESS_INFORMATION pi;
  ZeroMemory(&pi, sizeof(pi));
  wchar_t cmd[1024];
  swprintf(cmd, 1024, L"\"%s\"", exe);

  if (!CreateProcessW(exe, cmd, NULL, NULL, FALSE, CREATE_SUSPENDED, NULL, cwd, &si, &pi)) {
    wprintf(L"CreateProcess failed %lu\n", GetLastError());
    return 1;
  }
  wprintf(L"pid=%lu suspended dll=%s\n", pi.dwProcessId, dll);

  size_t bytes = (wcslen(dll) + 1) * sizeof(wchar_t);
  LPVOID remote = VirtualAllocEx(pi.hProcess, NULL, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
  if (!remote) {
    wprintf(L"VirtualAllocEx %lu\n", GetLastError());
    return 2;
  }
  if (!WriteProcessMemory(pi.hProcess, remote, dll, bytes, NULL)) {
    wprintf(L"WriteProcessMemory %lu\n", GetLastError());
    return 3;
  }
  FARPROC load = GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "LoadLibraryW");
  HANDLE th = CreateRemoteThread(pi.hProcess, NULL, 0, (LPTHREAD_START_ROUTINE)load, remote, 0, NULL);
  if (!th) {
    wprintf(L"CreateRemoteThread %lu\n", GetLastError());
    return 4;
  }
  WaitForSingleObject(th, 15000);
  DWORD code = 0;
  GetExitCodeThread(th, &code);
  wprintf(L"LoadLibraryW remote=%p\n", (void*)(uintptr_t)code);
  CloseHandle(th);

  ResumeThread(pi.hThread);
  wprintf(L"resumed (interactive desktop, SW_HIDE)\n");
  CloseHandle(pi.hThread);
  CloseHandle(pi.hProcess);
  return 0;
}
