/**
 * Dump CoCreateInstance dwClsContext for SessionFilter.
 */
'use strict';
function log(s) { send({ t: 'log', m: String(s) }); }
function guid(p) {
  const b = new Uint8Array(p.readByteArray(16));
  const d1 = b[0]|b[1]<<8|b[2]<<16|b[3]<<24;
  const d2 = b[4]|b[5]<<8, d3 = b[6]|b[7]<<8;
  const h = [...b].map(x => ('0'+x.toString(16)).slice(-2)).join('');
  return ('{'+('00000000'+(d1>>>0).toString(16)).slice(-8)+'-'+
    ('0000'+d2.toString(16)).slice(-4)+'-'+('0000'+d3.toString(16)).slice(-4)+'-'+
    h.slice(16,20)+'-'+h.slice(20)+'}').toUpperCase();
}
function modOff(a) {
  const m = Process.findModuleByAddress(a);
  return m ? m.name+'+0x'+a.sub(m.base).toString(16) : String(a);
}

const FILTER = '3F6374C2';

function install() {
  const combase = Process.findModuleByName('combase.dll');
  Interceptor.attach(combase.findExportByName('CoCreateInstance'), {
    onEnter(a) {
      this.clsid = guid(a[0]);
      this.outer = a[1];
      this.ctx = a[2].toUInt32() >>> 0;
      this.iid = guid(a[3]);
      this.ppv = a[4];
      this.watch = this.clsid.indexOf(FILTER) >= 0 || this.clsid.indexOf('5387A36B') >= 0
        || this.clsid.indexOf('DCAB0989') >= 0;
    },
    onLeave(r) {
      if (!this.watch) return;
      let obj = NULL;
      try { if (r.toInt32() === 0) obj = this.ppv.readPointer(); } catch (e) {}
      log('CoCreateInstance clsid=' + this.clsid +
        ' iid=' + this.iid +
        ' ctx=0x' + this.ctx.toString(16) +
        ' outer=' + this.outer +
        ' hr=' + r +
        ' obj=' + obj +
        '\n  ' + Thread.backtrace(this.context, Backtracer.ACCURATE).slice(0, 14).map(modOff).join('\n  '));
      if (obj && !obj.isNull()) {
        try {
          log('obj dump:\n' + hexdump(obj, { length: 0x80, ansi: false }));
        } catch (e) {}
      }
    }
  });
  log('ctx dump ready');
}
setImmediate(install);
