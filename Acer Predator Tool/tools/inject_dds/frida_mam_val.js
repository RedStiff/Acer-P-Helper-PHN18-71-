/**
 * Track who reads the NvCplApiSetSetting value buffer during DDS apply.
 */
'use strict';
function log(s) { send({ t: 'log', m: String(s) }); }
function modOff(addr) {
  const m = Process.findModuleByAddress(addr);
  return m ? (m.name + '+0x' + addr.sub(m.base).toString(16)) : addr.toString();
}

function install() {
  const nvcpl = Process.findModuleByName('NvCpl.dll') || Process.findModuleByName('nvcpl.dll');
  if (!nvcpl) { setTimeout(install, 500); return; }

  Interceptor.attach(nvcpl.findExportByName('NvCplApiSetSetting'), {
    onEnter(a) {
      if ((a[2].toInt32() >>> 0) !== 0x330) return;
      const val = a[3];
      log('SetSetting val@' + val + ' = ' + hexdump(val, { length: 16, ansi: false }));
      try {
        MemoryAccessMonitor.enable({ base: val, size: 16 }, {
          onAccess(details) {
            if (details.operation !== 'read') return;
            const bt = Thread.backtrace(details.context, Backtracer.ACCURATE)
              .slice(0, 10).map(modOff);
            log('READ val from ' + modOff(details.from) + '\n  ' + bt.join('\n  '));
          }
        });
      } catch (e) {
        log('MAM err ' + e + ' — fallback Interceptor copy watch');
      }
    }
  });

  Interceptor.attach(nvcpl.findExportByName('NvCplApiExecute'), {
    onEnter(a) { log('Execute ' + a[0]); },
    onLeave() {
      try { MemoryAccessMonitor.disable(); } catch (e) {}
      setTimeout(() => { try { MemoryAccessMonitor.disable(); } catch (e) {} }, 100);
      log('Execute done');
    }
  });

  // Also deep-follow ProcessSetSettings data pointers one level
  const combase = Process.findModuleByName('combase.dll');
  let arm = false;
  Interceptor.attach(nvcpl.findExportByName('NvCplApiSetSetting'), {
    onEnter(a) { if ((a[2].toInt32() >>> 0) === 0x330) arm = true; }
  });
  Interceptor.attach(combase.findExportByName('ObjectStublessClient5'), {
    onEnter(args) {
      if (!arm) return;
      const items = args[1].readPointer();
      const count = args[1].add(8).readU32();
      log('SetSettings count=' + count);
      for (let i = 0; i < count; i++) {
        const d = items.add(i * 0x20);
        const info = d.add(16).readU16();
        const sid = d.add(18).readU16();
        const flags = d.add(20).readU32();
        const dp = d.add(24).readPointer();
        log('desc[' + i + '] info=' + info + ' sid=0x' + sid.toString(16) + ' flags=' + flags + ' data=' + dp);
        log(hexdump(dp, { length: 0x40, ansi: false }));
        // follow any ptrs in first 0x40
        for (let off = 0; off < 0x40; off += 8) {
          try {
            const p = dp.add(off).readPointer();
            if (p.isNull() || p.compare(ptr('0x100000')) < 0) continue;
            if (p.compare(ptr('0x7FFFFFFFFFFF')) > 0) continue;
            const b = p.readByteArray(16);
            if (!b) continue;
            const u = new Uint8Array(b);
            // look for mux pattern or printable
            log('  nested+'+off.toString(16)+'=' + p + '\n' + hexdump(p, { length: 0x30, ansi: false }));
          } catch (e) {}
        }
      }
      arm = false;
    }
  });

  log('mam ready');
}
setImmediate(install);
