/*
 * probe_nvapi_setmux.c — find private NvAPI_DISP_SetDisplayMux id via nvxdapix xref,
 * then try calling it from this process (no NVIDIA App).
 *
 * Strategy: scan nvxdapix.dll for the wide string NvAPI_DISP_SetDisplayMux, then
 * look backward for imm32 args to nvapi_QueryInterface (0x?? patterns).
 * Also brute unique IDs near known DISP range if --brute.
 */
#include <windows.h>
#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include <stdlib.h>

typedef void* (*nvapi_QueryInterface_t)(uint32_t id);
typedef int (*nvapi_fn0_t)(void);
typedef int (*nvapi_fn1_t)(void* p);

static void ace(const char* tag) {
  HKEY k;
  if (RegOpenKeyExA(HKEY_LOCAL_MACHINE,
                    "SYSTEM\\CurrentControlSet\\Services\\nvlddmkm\\Global\\NvHybrid\\Persistence\\ACE",
                    0, KEY_READ, &k) != 0) {
    printf("%s ACE=?\n", tag);
    return;
  }
  DWORD state = 0, autom = 0, i2d = 0, sz = sizeof(DWORD), t;
  RegQueryValueExA(k, "InternalMuxState", NULL, &t, (BYTE*)&state, &sz); sz = sizeof(DWORD);
  RegQueryValueExA(k, "InternalMuxIsAutomaticMode", NULL, &t, (BYTE*)&autom, &sz); sz = sizeof(DWORD);
  RegQueryValueExA(k, "ACESwitchedI2D", NULL, &t, (BYTE*)&i2d, &sz);
  RegCloseKey(k);
  printf("%s state=%lu auto=%lu i2d=%lu\n", tag, state, autom, i2d);
}

static BYTE* read_file(const wchar_t* path, DWORD* out_sz) {
  HANDLE h = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL);
  if (h == INVALID_HANDLE_VALUE) return NULL;
  DWORD sz = GetFileSize(h, NULL);
  BYTE* buf = (BYTE*)malloc(sz);
  DWORD rd = 0;
  ReadFile(h, buf, sz, &rd, NULL);
  CloseHandle(h);
  *out_sz = rd;
  return buf;
}

static int find_wide(const BYTE* data, DWORD n, const wchar_t* s, DWORD* out_off) {
  size_t sl = wcslen(s) * 2;
  for (DWORD i = 0; i + sl < n; i++) {
    if (memcmp(data + i, s, sl) == 0) {
      *out_off = i;
      return 1;
    }
  }
  return 0;
}

/* Collect imm32 near offset (typical lea/mov ecx, imm before call QueryInterface). */
static void collect_imm32(const BYTE* data, DWORD n, DWORD center, uint32_t* out, int* out_n, int maxn) {
  DWORD start = center > 0x200 ? center - 0x200 : 0;
  DWORD end = center + 0x40 < n ? center + 0x40 : n;
  *out_n = 0;
  for (DWORD i = start; i + 5 < end; i++) {
    /* mov ecx/edx/r8d, imm32 : B9/BA / 41 B8 */
    if (data[i] == 0xB9 || data[i] == 0xBA || data[i] == 0xB8) {
      uint32_t v;
      memcpy(&v, data + i + 1, 4);
      if (v > 0x01000000 && v < 0xFFFFFFF0) {
        int dup = 0;
        for (int j = 0; j < *out_n; j++) if (out[j] == v) { dup = 1; break; }
        if (!dup && *out_n < maxn) out[(*out_n)++] = v;
      }
    }
  }
}

static int try_call(nvapi_QueryInterface_t qi, uint32_t id, int mux, int autom) {
  void* p = qi(id);
  if (!p) return -1;
  /* try a few layouts */
  unsigned char buf[64];
  memset(buf, 0, sizeof(buf));
  /* NV_DISPLAY_MUX_INFO-ish guesses */
  *(uint32_t*)buf = 0x00010018; /* ver1 size 24 */
  *(uint32_t*)(buf + 4) = (uint32_t)mux;
  *(uint32_t*)(buf + 8) = (uint32_t)autom;

  ace("BEFORE");
  int st = ((nvapi_fn1_t)p)(buf);
  printf("id=0x%08X layoutA st=0x%08X\n", id, (unsigned)st);
  Sleep(1500);
  ace("AFTER");

  memset(buf, 0, sizeof(buf));
  *(uint32_t*)buf = 0x00010010;
  *(uint32_t*)(buf + 4) = 1; /* automatic byte packing like nvcpl */
  *(uint32_t*)(buf + 4) = 0x01u | ((autom ? 1u : 0u) << 8);
  *(uint32_t*)(buf + 8) = (uint32_t)mux;
  st = ((nvapi_fn1_t)p)(buf);
  printf("id=0x%08X layoutB st=0x%08X\n", id, (unsigned)st);
  Sleep(1500);
  ace("AFTER2");
  return st;
}

int main(int argc, char** argv) {
  int do_brute = 0;
  int mux = 2, autom = 0;
  for (int i = 1; i < argc; i++) {
    if (strcmp(argv[i], "--brute") == 0) do_brute = 1;
    else if (_stricmp(argv[i], "igpu") == 0) { mux = 1; autom = 0; }
    else if (_stricmp(argv[i], "auto") == 0) { mux = 1; autom = 1; }
    else if (_stricmp(argv[i], "dgpu") == 0) { mux = 2; autom = 0; }
  }

  /* kill App for purity */
  system("taskkill /IM \"NVIDIA App.exe\" /F >nul 2>&1");

  DWORD sz = 0;
  BYTE* data = read_file(L"C:\\Program Files\\NVIDIA Corporation\\NVIDIA App\\NvCpl\\nvxdapix.dll", &sz);
  if (!data) {
    data = read_file(L"C:\\Windows\\System32\\DriverStore\\FileRepository\\nvacsi.inf_amd64_1463ab6df6c1e184\\nvxdapix.dll", &sz);
  }
  if (!data) { printf("nvxdapix missing\n"); return 1; }

  DWORD off = 0;
  if (!find_wide(data, sz, L"NvAPI_DISP_SetDisplayMux", &off)) {
    printf("string NvAPI_DISP_SetDisplayMux not found\n");
  } else {
    printf("SetDisplayMux string at file+0x%X\n", off);
  }

  uint32_t cands[64];
  int nc = 0;
  if (off) collect_imm32(data, sz, off, cands, &nc, 64);
  /* Also scan whole file for the string xref is hard without relocs; collect all imm near ascii name too */
  DWORD aoff = 0;
  for (DWORD i = 0; i + 22 < sz; i++) {
    if (memcmp(data + i, "NvAPI_DISP_SetDisplayMux", 24) == 0) { aoff = i; break; }
  }
  if (aoff) {
    printf("ascii SetDisplayMux at 0x%X\n", aoff);
    int n2 = 0; uint32_t tmp[64];
    collect_imm32(data, sz, aoff, tmp, &n2, 64);
    for (int i = 0; i < n2 && nc < 64; i++) {
      int dup = 0; for (int j = 0; j < nc; j++) if (cands[j] == tmp[i]) { dup = 1; break; }
      if (!dup) cands[nc++] = tmp[i];
    }
  }

  /* Heuristic: scan .text for push imm32 / mov reg,imm32 of values that appear only few times
     near 'QueryInterface' usage — also add IDs from known probe list file if present. */
  printf("candidate IDs from proximity: %d\n", nc);
  for (int i = 0; i < nc; i++) printf("  0x%08X\n", cands[i]);

  HMODULE nvapi = LoadLibraryW(L"C:\\Windows\\System32\\nvapi64.dll");
  if (!nvapi) { printf("nvapi64 load fail %lu\n", GetLastError()); free(data); return 1; }
  nvapi_QueryInterface_t qi = (nvapi_QueryInterface_t)GetProcAddress(nvapi, "nvapi_QueryInterface");
  if (!qi) { printf("QueryInterface missing\n"); return 1; }
  nvapi_fn0_t init = (nvapi_fn0_t)qi(0x0150E828);
  if (init) printf("NvAPI_Initialize -> %d\n", init());

  /* Filter candidates that resolve to unique non-null, non-stub */
  void* stubs[8]; int ns = 0;
  for (int i = 0; i < nc; i++) {
    void* p = qi(cands[i]);
    printf("resolve 0x%08X -> %p\n", cands[i], p);
  }

  for (int i = 0; i < nc; i++) {
    void* p = qi(cands[i]);
    if (!p) continue;
    printf("\n=== try 0x%08X ===\n", cands[i]);
    try_call(qi, cands[i], mux, autom);
  }

  if (do_brute) {
    printf("brute not implemented in this build (too slow); use candidate list\n");
  }

  free(data);
  return 0;
}
