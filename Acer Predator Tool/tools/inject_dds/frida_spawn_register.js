/**
 * Spawn-time capture: CoRegisterClassObject + successful SessionFilter CoCreate.
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

function install() {
  const combase = Process.findModuleByName('combase.dll') || Process.findModuleByName('ole32.dll');
  if (!combase) { setTimeout(install, 50); return; }

  const reg = combase.findExportByName('CoRegisterClassObject');
  if (reg) Interceptor.attach(reg, {
    onEnter(a) {
      this.clsid = guid(a[0]);
      this.ctx = a[3].toUInt32() >>> 0;
      this.flags = a[4].toUInt32() >>> 0;
    },
    onLeave(r) {
      log('CoRegisterClassObject clsid=' + this.clsid +
        ' ctx=0x' + this.ctx.toString(16) +
        ' flags=0x' + this.flags.toString(16) +
        ' hr=' + r +
        '\n  ' + Thread.backtrace(this.context, Backtracer.ACCURATE).slice(0, 8).map(modOff).join('\n  '));
    }
  });

  Interceptor.attach(combase.findExportByName('CoCreateInstance'), {
    onEnter(a) {
      this.clsid = guid(a[0]);
      this.ctx = a[2].toUInt32() >>> 0;
      this.iid = guid(a[3]);
      this.ppv = a[4];
      this.watch = this.clsid.indexOf('3F6374C2') >= 0 || this.clsid.indexOf('5387A36B') >= 0
        || this.clsid.indexOf('DCAB0989') >= 0 || this.clsid.indexOf('1DC715B2') >= 0;
    },
    onLeave(r) {
      if (!this.watch) return;
      const hr = r.toInt32();
      let obj = ptr(0);
      try { if (hr === 0) obj = this.ppv.readPointer(); } catch (e) {}
      log('CoCreateInstance clsid=' + this.clsid + ' iid=' + this.iid +
        ' ctx=0x' + this.ctx.toString(16) +
        ' hr=0x' + (hr >>> 0).toString(16) +
        ' obj=' + obj +
        '\n  ' + Thread.backtrace(this.context, Backtracer.ACCURATE).slice(0, 12).map(modOff).join('\n  '));
      if (hr === 0 && !obj.isNull()) {
        try {
          log('SUCCESS obj dump:\n' + hexdump(obj, { length: 0x80, ansi: false }));
          const vt = obj.readPointer();
          log('vt=' + vt + ' ' + modOff(vt));
        } catch (e) { log('dump err ' + e); }
      }
    }
  });

  // Also CoGetClassObject
  const cgco = combase.findExportByName('CoGetClassObject');
  if (cgco) Interceptor.attach(cgco, {
    onEnter(a) {
      this.clsid = guid(a[0]);
      this.ctx = a[1].toUInt32() >>> 0;
      this.watch = this.clsid.indexOf('3F6374C2') >= 0 || this.clsid.indexOf('5387A36B') >= 0;
    },
    onLeave(r) {
      if (this.watch)
        log('CoGetClassObject clsid=' + this.clsid + ' ctx=0x' + this.ctx.toString(16) + ' hr=0x' + (r.toInt32()>>>0).toString(16));
    }
  });

  log('spawn hooks ready');
}
setImmediate(install);
