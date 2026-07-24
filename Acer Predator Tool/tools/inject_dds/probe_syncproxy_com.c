/*
 * probe_syncproxy_com.c — CoCreate NvXDCore.SyncProxy WITHOUT NVIDIA App.
 */
#include <windows.h>
#include <stdio.h>

static const GUID CLSID_SyncProxy =
{ 0xDCAB0989, 0x1301, 0x4319, { 0xBE, 0x5F, 0xAD, 0xE8, 0x9F, 0x88, 0x58, 0x1C } };
static const GUID IID_StateEventsGuess =
{ 0x4473e3a7, 0xc2ad, 0x4075, { 0xa1, 0xf8, 0x93, 0x5a, 0x58, 0x47, 0x40, 0xa9 } };

static void ace(void) {
  HKEY k;
  if (RegOpenKeyExA(HKEY_LOCAL_MACHINE,
                    "SYSTEM\\CurrentControlSet\\Services\\nvlddmkm\\Global\\NvHybrid\\Persistence\\ACE",
                    0, KEY_READ, &k) != 0) { printf("ACE=?\n"); return; }
  DWORD state=0,autom=0,i2d=0,sz=sizeof(DWORD),t;
  RegQueryValueExA(k,"InternalMuxState",NULL,&t,(BYTE*)&state,&sz); sz=sizeof(DWORD);
  RegQueryValueExA(k,"InternalMuxIsAutomaticMode",NULL,&t,(BYTE*)&autom,&sz); sz=sizeof(DWORD);
  RegQueryValueExA(k,"ACESwitchedI2D",NULL,&t,(BYTE*)&i2d,&sz);
  RegCloseKey(k);
  printf("ACE state=%lu auto=%lu i2d=%lu\n", state, autom, i2d);
}

int main(void) {
  HRESULT hr = CoInitializeEx(NULL, COINIT_MULTITHREADED);
  printf("CoInitializeEx=0x%08lX\n", (unsigned long)hr);
  system("taskkill /IM \"NVIDIA App.exe\" /F >nul 2>&1");
  Sleep(500);

  HANDLE uxd = OpenEventW(SYNCHRONIZE, FALSE,
                          L"Local\\UXDServiceStarted-D40E81C4-06EF-454A-9E81-1F4D55CEBD57");
  printf("UXDServiceStarted %s\n", uxd ? "OK" : "MISSING");
  if (uxd) CloseHandle(uxd);
  ace();

  IUnknown* unk = NULL;
  hr = CoCreateInstance(&CLSID_SyncProxy, NULL, CLSCTX_LOCAL_SERVER, &IID_IUnknown, (void**)&unk);
  printf("CoCreate SyncProxy HR=0x%08lX ptr=%p\n", (unsigned long)hr, (void*)unk);
  if (FAILED(hr) || !unk) { CoUninitialize(); return 1; }

  void** vt = *(void***)unk;
  printf("vtable=%p\n", (void*)vt);
  for (int i = 0; i < 16; i++) printf("  vt[%d]=%p\n", i, vt[i]);

  IUnknown* ev = NULL;
  hr = unk->lpVtbl->QueryInterface(unk, &IID_StateEventsGuess, (void**)&ev);
  printf("QI IStateEvents-guess HR=0x%08lX ptr=%p\n", (unsigned long)hr, (void*)ev);
  if (ev) ev->lpVtbl->Release(ev);

  unk->lpVtbl->Release(unk);
  CoUninitialize();
  printf("SyncProxy reachable without NVIDIA App.\n");
  return 0;
}
