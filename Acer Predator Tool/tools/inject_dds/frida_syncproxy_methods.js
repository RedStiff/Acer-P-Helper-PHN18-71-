/**
 * Capture ISyncProxy method calls (ObjectStubless on SyncProxy this) during App life.
 */
'use strict';
function log(s) { send({ t: 'log', m: String(s) }); }
function guid(p) {
  try {
    const b = new Uint8Array(p.readByteArray(16));
    const d1 = b[0]|b[1]<<8|b[2]<<16|b[3]<<24;
    const d2 = b[4]|b[5]<<8, d3 = b[6]|b[7]<<8;
    const h = [...b].map(x => ('0'+x.toString(16)).slice(-2)).join('');
    return ('{'+('00000000'+(d1>>>0).toString(16)).slice(-8)+'-'+
      ('0000'+d2.toString(16)).slice(-4)+'-'+('0000'+d3.toString(16)).slice(-4)+'-'+
      h.slice(16,20)+'-'+h.slice(20)+'}').toUpperCase();
  } catch (e) { return '?'; }
}
function modOff(a) {
  const m = Process.findModuleByAddress(a);
  return m ? m.name+'+0x'+a.sub(m.base).toString(16) : String(a);
}

const SYNC_CLSID = 'DCAB0989';
const syncObjects = {};

function install() {
  const combase = Process.findModuleByName('combase.dll');
  Interceptor.attach(combase.findExportByName('CoCreateInstance'), {
    onEnter(a) {
      this.clsid = guid(a[0]);
      this.ppv = a[4];
      this.watch = this.clsid.indexOf(SYNC_CLSID) >= 0 || this.clsid.indexOf('3F6374C2') >= 0;
      this.ctx = a[2].toUInt32()>>>0;
    },
    onLeave(r) {
      if (!this.watch) return;
      const hr = r.toInt32();
      let obj = ptr(0);
      try { if (hr === 0) obj = this.ppv.readPointer(); } catch (e) {}
      log('CoCreate ' + this.clsid + ' ctx=0x' + this.ctx.toString(16) + ' hr=0x' + (hr>>>0).toString(16) + ' obj=' + obj);
      if (hr === 0 && this.clsid.indexOf(SYNC_CLSID) >= 0 && !obj.isNull()) {
        syncObjects[obj.toString()] = true;
        log('tracked SyncProxy ' + obj);
      }
    }
  });

  // Hook stubless 3..7; log when this looks like our tracked sync OR always with nvxdbat bt
  for (const slot of [3, 4, 5, 6, 7]) {
    const exp = combase.findExportByName('ObjectStublessClient' + slot);
    if (!exp) continue;
    Interceptor.attach(exp, {
      onEnter(args) {
        const th = args[0];
        const key = th.toString();
        // Always log if backtrace has nvxdbat / NvCpl and slot is interesting
        const bt = Thread.backtrace(this.context, Backtracer.ACCURATE).slice(0, 6).map(modOff);
        const interesting = bt.some(x => /nvxd|nvcpl|NvCpl/i.test(x)) || syncObjects[key];
        if (!interesting) return;
        this.log = true;
        this.slot = slot;
        log('Stubless' + slot + ' this=' + th + ' a1=' + args[1] + ' a2=' + args[2] + ' a3=' + args[3] +
          '\n  ' + bt.join('\n  '));
        try {
          if (!args[1].isNull() && args[1].compare(ptr('0x10000')) > 0)
            log('  a1:\n' + hexdump(args[1], { length: 0x40, ansi: false }));
        } catch (e) {}
      },
      onLeave(r) {
        if (this.log) log('  Stubless' + this.slot + ' => ' + r);
      }
    });
  }
  log('sync watch ready');
}
setImmediate(install);
