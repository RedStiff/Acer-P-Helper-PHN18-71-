/**
 * Find origin of IStateData second-arg context (R8).
 */
'use strict';
function log(s) { send({ t: 'log', m: String(s) }); }
function modOff(a) {
  const m = Process.findModuleByAddress(a);
  return m ? m.name + '+0x' + a.sub(m.base).toString(16) : String(a);
}

function install() {
  const nvcpl = Process.findModuleByName('NvCpl.dll') || Process.findModuleByName('nvcpl.dll');
  if (!nvcpl) { setTimeout(install, 500); return; }
  let arm = false;
  let ctxPtr = null;
  Interceptor.attach(nvcpl.findExportByName('NvCplApiSetSetting'), {
    onEnter(a) { if ((a[2].toInt32() >>> 0) === 0x330) arm = true; }
  });

  const combase = Process.findModuleByName('combase.dll');
  Interceptor.attach(combase.findExportByName('ObjectStublessClient5'), {
    onEnter(args) {
      if (!arm) return;
      ctxPtr = this.context.r8;
      log('SetSettings ctx=' + ctxPtr);
      // dump object
      try {
        const p = ptr(ctxPtr);
        log('ctx dump:\n' + hexdump(p, { length: 0x80, ansi: false }));
        const vt = p.readPointer();
        log('ctx vt=' + vt + ' = ' + modOff(vt));
        // try read GUID at +0x30
        const g = p.add(0x30).readByteArray(16);
        log('ctx+30 guid bytes ' + Array.from(new Uint8Array(g)).map(x=>('0'+x.toString(16)).slice(-2)).join(''));
      } catch (e) { log('ctx ' + e); }

      // backtrace to see who set up the call
      const bt = Thread.backtrace(this.context, Backtracer.ACCURATE).slice(0, 15).map(modOff);
      log('bt:\n  ' + bt.join('\n  '));
      arm = false;
    }
  });

  // Also watch ISyncProxy stubless 3/4/5 for any returns that match ctx
  for (const slot of [3, 4, 5]) {
    Interceptor.attach(combase.findExportByName('ObjectStublessClient' + slot), {
      onLeave(retval) {
        if (!ctxPtr) return;
        // can't easily filter to SyncProxy only
      }
    });
  }

  log('ctx origin ready');
}
setImmediate(install);
