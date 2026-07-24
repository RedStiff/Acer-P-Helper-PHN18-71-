/**
 * Pause-safe: hook LoadLibrary + CoCreate before any App code runs.
 * Parent must: spawn → attach → load script → wait ready → resume.
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
let batHooked = false;

function hookCreateFromBat(bat) {
  if (batHooked) return;
  batHooked = true;
  log('hook bat creates @ ' + bat.base);

  const createWrap = bat.base.add(0x6acd0);
  const createReal = bat.base.add(0x7f780);
  const createAlt = bat.base.add(0x7d1b0); // seen on other coclass

  function attachCreate(addr, tag) {
    try {
      Interceptor.attach(addr, {
        onEnter(a) {
          this.tag = tag;
          this.self = a[0]; this.outer = a[1]; this.iid = a[2]; this.ppv = a[3];
          // For REAL path ABI differs: rcx=outer, rdx=iid, r8=ppv
          if (tag === 'REAL') {
            log(tag + ' rcx=' + a[0] + ' rdx=' + a[1] + ' r8=' + a[2]);
            try { log(tag + ' iid=' + guid(a[1])); } catch (e) {}
          } else {
            log(tag + ' self=' + a[0] + ' outer=' + a[1] + ' iid=' + guid(a[2]));
            try {
              // Detect SessionFilter factory by +0x40 pointing to REAL
              const f40 = a[0].add(0x40).readPointer();
              log(tag + ' +0x40=' + f40 + ' ' + modOff(f40));
              if (f40.equals(createReal) || f40.equals(createAlt)) {
                log(tag + ' factory dump:\n' + hexdump(a[0], { length: 0x80 }));
              }
            } catch (e) {}
          }
        },
        onLeave(r) {
          const hr = r.toInt32() >>> 0;
          let obj = ptr(0);
          try {
            if (this.tag !== 'REAL' && hr === 0) obj = this.ppv.readPointer();
          } catch (e) {}
          log(tag + ' leave hr=0x' + hr.toString(16) + (obj.isNull() ? '' : ' obj=' + obj));
          if (!obj.isNull()) {
            try {
              log('  obj vt=' + modOff(obj.readPointer()));
              log('  obj:\n' + hexdump(obj, { length: 0x50 }));
            } catch (e) {}
          }
        }
      });
      log('attached ' + tag + ' ' + addr);
    } catch (e) {
      log('attach fail ' + tag + ' ' + e);
    }
  }

  attachCreate(createWrap, 'WRAP');
  attachCreate(createReal, 'REAL');
  attachCreate(createAlt, 'ALT');

  const gco = bat.findExportByName('DllGetClassObject');
  if (gco) {
    Interceptor.attach(gco, {
      onEnter(a) { this.clsid = guid(a[0]); this.watch = this.clsid.indexOf(FILTER) >= 0; },
      onLeave(r) {
        if (!this.watch) return;
        log('DllGetClassObject FILTER hr=0x' + (r.toInt32() >>> 0).toString(16));
      }
    });
  }
}

function install() {
  const k32 = Process.findModuleByName('kernel32.dll');
  for (const name of ['LoadLibraryW', 'LoadLibraryExW']) {
    const p = k32.findExportByName(name);
    if (!p) continue;
    Interceptor.attach(p, {
      onEnter(a) {
        try {
          const s = a[0].readUtf16String();
          if (s && /nvxdbat/i.test(s)) this.hit = s;
        } catch (e) {}
      },
      onLeave(r) {
        if (!this.hit) return;
        log('Load ' + this.hit + ' -> ' + r);
        const bat = Process.findModuleByName('nvxdbat.dll');
        if (bat) hookCreateFromBat(bat);
      }
    });
  }

  // Already loaded?
  const bat0 = Process.findModuleByName('nvxdbat.dll');
  if (bat0) hookCreateFromBat(bat0);

  const combase = Process.findModuleByName('combase.dll') || Process.findModuleByName('ole32.dll');
  Interceptor.attach(combase.findExportByName('CoCreateInstance'), {
    onEnter(a) {
      this.clsid = guid(a[0]);
      this.outer = a[1];
      this.ctx = a[2].toUInt32() >>> 0;
      this.iid = guid(a[3]);
      this.ppv = a[4];
      this.watch = this.clsid.indexOf(FILTER) >= 0 || this.clsid.indexOf('5387A36B') >= 0;
    },
    onLeave(r) {
      if (!this.watch) return;
      let obj = ptr(0);
      try { if (r.toInt32() === 0) obj = this.ppv.readPointer(); } catch (e) {}
      log('CCI ' + this.clsid.substring(0, 9) + ' ctx=0x' + this.ctx.toString(16) +
        ' outer=' + this.outer + ' iid=' + this.iid +
        ' hr=0x' + (r.toInt32() >>> 0).toString(16) + ' obj=' + obj);
      if (!obj.isNull()) {
        try {
          log('  early obj vt=' + modOff(obj.readPointer()));
          log('  early obj:\n' + hexdump(obj, { length: 0x50 }));
        } catch (e) { log('  early dump ' + e); }
      }
      log('  bt:\n  ' + Thread.backtrace(this.context, Backtracer.ACCURATE).slice(0, 12).map(modOff).join('\n  '));
    }
  });

  const gco = combase.findExportByName('CoGetClassObject');
  if (gco) {
    Interceptor.attach(gco, {
      onEnter(a) {
        this.clsid = guid(a[0]);
        this.ctx = a[1].toUInt32() >>> 0;
        this.watch = this.clsid.indexOf(FILTER) >= 0;
      },
      onLeave(r) {
        if (!this.watch) return;
        log('GCO FILTER ctx=0x' + this.ctx.toString(16) + ' hr=0x' + (r.toInt32() >>> 0).toString(16));
      }
    });
  }

  send({ t: 'log', m: 'READY' });
}

setImmediate(install);
