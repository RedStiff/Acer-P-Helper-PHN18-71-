/**
 * Spawn App: hook SessionFilter CreateInstance body; dump factory; log CLSCTX.
 */
'use strict';

function log(s) { send({ t: 'log', m: String(s) }); }
function modOff(a) {
  const m = Process.findModuleByAddress(a);
  return m ? m.name + '+0x' + a.sub(m.base).toString(16) : String(a);
}
function guid(p) {
  try {
    const b = new Uint8Array(p.readByteArray(16));
    const d1 = (b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24)) >>> 0;
    const d2 = b[4] | (b[5] << 8);
    const d3 = b[6] | (b[7] << 8);
    const h = [...b].map(x => ('0' + x.toString(16)).slice(-2)).join('');
    return ('{' + ('00000000' + d1.toString(16)).slice(-8) + '-' +
      ('0000' + d2.toString(16)).slice(-4) + '-' +
      ('0000' + d3.toString(16)).slice(-4) + '-' +
      h.slice(16, 20) + '-' + h.slice(20) + '}').toUpperCase();
  } catch (e) { return '?'; }
}

const FILTER = '3F6374C2';

function hookBat() {
  const bat = Process.findModuleByName('nvxdbat.dll');
  if (!bat) { setTimeout(hookBat, 200); return; }
  log('nvxdbat ' + bat.base);

  const createWrap = bat.base.add(0x6acd0);
  const createReal = bat.base.add(0x7f780);

  Interceptor.attach(createWrap, {
    onEnter(a) {
      this.self = a[0]; this.outer = a[1]; this.iid = a[2]; this.ppv = a[3];
      log('WRAP Create self=' + a[0] + ' outer=' + a[1] + ' iid=' + guid(a[2]));
      try { log('  factory:\n' + hexdump(a[0], { length: 0x80 })); } catch (e) {}
      log('  +0x40=' + a[0].add(0x40).readPointer() + ' ' + modOff(a[0].add(0x40).readPointer()));
      log('  bt:\n  ' + Thread.backtrace(this.context, Backtracer.ACCURATE).slice(0, 10).map(modOff).join('\n  '));
    },
    onLeave(r) {
      let obj = ptr(0);
      try { if (r.toInt32() === 0) obj = this.ppv.readPointer(); } catch (e) {}
      log('WRAP leave hr=0x' + (r.toInt32() >>> 0).toString(16) + ' obj=' + obj);
      if (!obj.isNull()) {
        try {
          log('  obj vt=' + modOff(obj.readPointer()));
          log('  obj:\n' + hexdump(obj, { length: 0x50 }));
        } catch (e) {}
      }
    }
  });

  Interceptor.attach(createReal, {
    onEnter(a) {
      log('REAL Create rcx=' + a[0] + ' rdx=' + a[1] + ' r8=' + a[2] + ' rax=' + this.context.rax);
      try { if (!a[1].isNull()) log('  iid=' + guid(a[1])); } catch (e) {}
    },
    onLeave(r) {
      log('REAL leave hr=0x' + (r.toInt32() >>> 0).toString(16));
    }
  });

  log('create hooks ready');
}

function hookCom() {
  const combase = Process.findModuleByName('combase.dll') || Process.findModuleByName('ole32.dll');
  if (!combase) { setTimeout(hookCom, 100); return; }
  Interceptor.attach(combase.findExportByName('CoCreateInstance'), {
    onEnter(a) {
      this.clsid = guid(a[0]);
      this.ctx = a[2].toUInt32() >>> 0;
      this.iid = guid(a[3]);
      this.ppv = a[4];
      this.watch = this.clsid.indexOf(FILTER) >= 0;
    },
    onLeave(r) {
      if (!this.watch) return;
      let obj = ptr(0);
      try { if (r.toInt32() === 0) obj = this.ppv.readPointer(); } catch (e) {}
      log('CCI FILTER ctx=0x' + this.ctx.toString(16) + ' iid=' + this.iid +
        ' hr=0x' + (r.toInt32() >>> 0).toString(16) + ' obj=' + obj);
    }
  });
  log('com hooks ready');
}

setImmediate(() => { hookCom(); hookBat(); });
