/**
 * Capture exact RCX/RDX/R8/R9 for IStateData stubless calls during DDS.
 */
'use strict';
function log(s) { send({ t: 'log', m: String(s) }); }
function hd(p, n) {
  try { return hexdump(p, { length: n, ansi: false }); }
  catch (e) { return String(e); }
}

function install() {
  const nvcpl = Process.findModuleByName('NvCpl.dll') || Process.findModuleByName('nvcpl.dll');
  if (!nvcpl) { setTimeout(install, 500); return; }
  let arm = false;
  Interceptor.attach(nvcpl.findExportByName('NvCplApiSetSetting'), {
    onEnter(a) { if ((a[2].toInt32() >>> 0) === 0x330) { arm = true; log('ARM'); } }
  });
  Interceptor.attach(nvcpl.findExportByName('NvCplApiExecute'), {
    onLeave() { setTimeout(() => { arm = false; }, 2000); }
  });

  const combase = Process.findModuleByName('combase.dll');
  const map = { 3: 'GetSettings', 4: 'GetHandleInfo', 5: 'SetSettings', 6: 'DoOperation' };
  for (const slot of [3, 5, 6]) {
    Interceptor.attach(combase.findExportByName('ObjectStublessClient' + slot), {
      onEnter(args) {
        if (!arm) return;
        const ctx = this.context;
        log('==== ' + map[slot] +
          '\n RCX(this)=' + ctx.rcx +
          '\n RDX(a1)=' + ctx.rdx +
          '\n R8(a2)=' + ctx.r8 +
          '\n R9(a3)=' + ctx.r9 +
          '\n rsp+0x28=' + ctx.rsp.add(0x28).readPointer() +
          '\n rsp+0x30=' + ctx.rsp.add(0x30).readPointer());
        log('RDX:\n' + hd(ptr(ctx.rdx), 0x40));
        if (!ptr(ctx.r8).isNull()) {
          try { log('R8:\n' + hd(ptr(ctx.r8), 0x40)); } catch (e) { log('R8 ' + e); }
        }
        if (slot === 5) {
          try {
            const items = ptr(ctx.rdx).readPointer();
            const count = ptr(ctx.rdx).add(8).readU32();
            log('set count=' + count);
            log('items:\n' + hd(items, count * 0x20));
            for (let i = 0; i < count; i++) {
              const d = items.add(i * 0x20);
              const dp = d.add(24).readPointer();
              log('data[' + i + ']:\n' + hd(dp, 0x40));
            }
          } catch (e) { log('parse ' + e); }
        }
        if (slot === 6) {
          log('op RDX full:\n' + hd(ptr(ctx.rdx), 0x60));
        }
      }
    });
  }
  log('regs ready');
}
setImmediate(install);
