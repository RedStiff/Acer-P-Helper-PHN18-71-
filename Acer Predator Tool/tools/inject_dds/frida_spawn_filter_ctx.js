/**
 * Spawn NVIDIA App and log exact CLSCTX for SessionFilter CoCreate.
 * frida -f "..." -l frida_spawn_filter_ctx.js --no-pause
 */
'use strict';

function log(s) { send({ t: 'log', m: String(s) }); }

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

function modOff(a) {
  const m = Process.findModuleByAddress(a);
  return m ? (m.name + '+0x' + a.sub(m.base).toString(16)) : String(a);
}

const WATCH = ['3F6374C2', '5387A36B'];

function hit(g) { return WATCH.some(x => g.indexOf(x) >= 0); }

function dumpObj(p) {
  try {
    const vt = p.readPointer();
    log('  obj vt=' + modOff(vt));
    log('  dump:\n' + hexdump(p, { length: 0x50, ansi: false }));
    // QI IUnknown already held; try read +0x30 guid
    try {
      const g = guid(p.add(0x30));
      log('  +0x30 guid=' + g);
    } catch (e) {}
  } catch (e) { log('  dump fail ' + e); }
}

function install() {
  const combase = Process.findModuleByName('combase.dll') || Process.findModuleByName('ole32.dll');
  if (!combase) { setTimeout(install, 100); return; }

  const cci = combase.findExportByName('CoCreateInstance');
  Interceptor.attach(cci, {
    onEnter(a) {
      this.clsid = guid(a[0]);
      this.outer = a[1];
      this.ctx = a[2].toUInt32() >>> 0;
      this.iid = guid(a[3]);
      this.ppv = a[4];
      this.watch = hit(this.clsid);
    },
    onLeave(r) {
      if (!this.watch) return;
      let obj = NULL;
      try { if (r.toInt32() === 0) obj = this.ppv.readPointer(); } catch (e) {}
      log('CCI clsid=' + this.clsid + ' ctx=0x' + this.ctx.toString(16) +
        ' iid=' + this.iid + ' outer=' + this.outer + ' hr=' + r +
        (obj.isNull() ? '' : ' obj=' + obj) +
        '\n  ' + Thread.backtrace(this.context, Backtracer.ACCURATE).slice(0, 12).map(modOff).join('\n  '));
      if (!obj.isNull()) dumpObj(obj);
    }
  });

  const cciex = combase.findExportByName('CoCreateInstanceEx');
  if (cciex) {
    Interceptor.attach(cciex, {
      onEnter(a) {
        this.clsid = guid(a[0]);
        this.ctx = a[2].toUInt32() >>> 0;
        this.watch = hit(this.clsid);
      },
      onLeave(r) {
        if (!this.watch) return;
        log('CCIEX clsid=' + this.clsid + ' ctx=0x' + this.ctx.toString(16) + ' hr=' + r);
      }
    });
  }

  const gco = combase.findExportByName('CoGetClassObject');
  if (gco) {
    Interceptor.attach(gco, {
      onEnter(a) {
        this.clsid = guid(a[0]);
        this.ctx = a[1].toUInt32() >>> 0;
        this.iid = guid(a[3]);
        this.watch = hit(this.clsid);
      },
      onLeave(r) {
        if (!this.watch) return;
        log('GCO clsid=' + this.clsid + ' ctx=0x' + this.ctx.toString(16) +
          ' iid=' + this.iid + ' hr=' + r);
      }
    });
  }

  const reg = combase.findExportByName('CoRegisterClassObject');
  if (reg) {
    Interceptor.attach(reg, {
      onEnter(a) {
        this.clsid = guid(a[0]);
        this.ctx = a[3].toUInt32() >>> 0;
        this.flags = a[4].toUInt32() >>> 0;
      },
      onLeave(r) {
        if (!hit(this.clsid) && this.clsid.indexOf('DCAB0989') < 0) return;
        log('REG clsid=' + this.clsid + ' ctx=0x' + this.ctx.toString(16) +
          ' flags=0x' + this.flags.toString(16) + ' hr=' + r);
      }
    });
  }

  log('filter-ctx hooks ready');
}

setImmediate(install);
