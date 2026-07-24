// Hook cef_initialize to force remote_debugging_port=9333, then caller uses CDP.
'use strict';

function ptrAdd(p, off) {
  return p.add(off);
}

function trySetPort(settings, offset, port) {
  try {
    const before = settings.add(offset).readS32();
    settings.add(offset).writeS32(port);
    const after = settings.add(offset).readS32();
    send({ t: 'set_port', offset: offset, before: before, after: after });
    return true;
  } catch (e) {
    send({ t: 'set_port_err', offset: offset, err: String(e) });
    return false;
  }
}

function hookCefInitialize() {
  const mod = Process.findModuleByName('libcef.dll');
  if (!mod) {
    send({ t: 'wait_libcef' });
    return false;
  }
  const cefInit = Module.findExportByName('libcef.dll', 'cef_initialize');
  if (!cefInit) {
    send({ t: 'no_cef_initialize' });
    return false;
  }
  send({ t: 'hooking', addr: cefInit.toString() });

  Interceptor.attach(cefInit, {
    onEnter(args) {
      // int cef_initialize(const cef_main_args_t* args, const cef_settings_t* settings, cef_app_t* application, void* windows_sandbox_info);
      this.settings = args[1];
      send({ t: 'cef_initialize_enter', settings: this.settings.toString() });
      try {
        const size = this.settings.readU64();
        send({ t: 'settings_size', size: size.toString() });
        // Dump first 0x200 bytes for offline offset calc
        send({ t: 'settings_hex', hex: hexdump(this.settings, { length: 0x200, ansi: false }) });
      } catch (e) {
        send({ t: 'dump_err', err: String(e) });
      }

      // Brute-force plausible int field offsets (aligned) within settings that look like port slots (0).
      // Prefer known CEF128-ish region near end of string fields; also try common offsets.
      const candidates = [];
      for (let off = 8; off < 0x1C0; off += 4) {
        try {
          const v = this.settings.add(off).readS32();
          if (v === 0) candidates.push(off);
        } catch (_) {}
      }
      send({ t: 'zero_int_offsets', offsets: candidates.slice(0, 80) });

      // Force several likely remote_debugging_port offsets used across CEF versions.
      // Safe: setting a wrong int field to 9333 may be ignored if unused; worst case weird setting.
      const likely = [0xA8, 0xB0, 0xB8, 0xC0, 0xC8, 0xD0, 0xD8, 0xE0, 0xE8, 0xF0, 0xF8,
        0x100, 0x108, 0x110, 0x118, 0x120, 0x128, 0x130, 0x138, 0x140, 0x148, 0x150,
        0x158, 0x160, 0x168, 0x170, 0x178, 0x180, 0x188, 0x190];
      for (const off of likely) {
        trySetPort(this.settings, off, 9333);
      }
    },
    onLeave(retval) {
      send({ t: 'cef_initialize_leave', ret: retval.toInt32() });
    }
  });
  return true;
}

// libcef may load after spawn
if (!hookCefInitialize()) {
  const iv = setInterval(() => {
    if (hookCefInitialize()) clearInterval(iv);
  }, 50);
}
