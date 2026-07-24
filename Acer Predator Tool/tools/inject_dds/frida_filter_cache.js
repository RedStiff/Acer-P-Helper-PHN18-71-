/**
 * Watch SessionFilter iface+0x40 / filter+0x58 writes + identify create outer.
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

const FILTER_CLSID = '3F6374C2';
const IID_OLD = '627D7951';
const watched = {};

function describeObj(p, tag) {
  if (p.isNull()) { log(tag + ' null'); return; }
  try {
    const vt = p.readPointer();
    log(tag + ' ' + p + ' vt=' + modOff(vt));
    log(tag + ' dump:\n' + hexdump(p, { length: 0x40 }));
    // QI IUnknown / OLD / NEW
    const qi = new NativeFunction(vt.readPointer(), 'int', ['pointer', 'pointer', 'pointer']);
    function tryQi(bytes, name) {
      const iid = Memory.alloc(16); iid.writeByteArray(bytes);
      const out = Memory.alloc(8); out.writePointer(ptr(0));
      const hr = qi(p, iid, out);
      log(tag + ' QI ' + name + ' hr=0x' + (hr >>> 0).toString(16) + ' -> ' + out.readPointer());
    }
    tryQi([0,0,0,0,0,0,0,0,0xc0,0,0,0,0,0,0,0x46], 'IUnknown');
    tryQi([0x51,0x79,0x7d,0x62,0x43,0x96,0xe6,0x4d,0x89,0x8f,0x6c,0x6b,0x76,0x6a,0xab,0x39], 'OLD');
    tryQi([0x58,0xb4,0xab,0xe6,0xb8,0x38,0xdf,0x4f,0x85,0xcf,0xad,0xc2,0xe9,0x87,0x09,0x70], 'NEW');
    try {
      const g = guid(p.add(0x30));
      if (g.indexOf('00000000') < 0) log(tag + ' +0x30=' + g);
    } catch (e) {}
  } catch (e) {
    log(tag + ' describe fail ' + e);
  }
}

function watchFilter(filter, outer) {
  const key = filter.toString();
  if (watched[key]) return;
  watched[key] = true;
  const iface = filter.add(0x18);
  const cacheSlot = iface.add(0x40); // filter+0x58
  log('WATCH filter=' + filter + ' iface=' + iface + ' outer=' + outer);
  describeObj(outer, 'OUTER');
  describeObj(filter, 'FILTER0');
  log('cache0=' + cacheSlot.readPointer());

  // Memory access monitor on cache slot
  MemoryAccessMonitor.enable([{ base: cacheSlot, size: 8 }], {
    onAccess(details) {
      if (details.operation !== 'write') return;
      const val = cacheSlot.readPointer();
      log('CACHE WRITE ' + val + ' from ' + modOff(details.from));
      log('  bt:\n  ' + Thread.backtrace(details.context, Backtracer.ACCURATE)
        .slice(0, 12).map(modOff).join('\n  '));
      describeObj(val, 'CACHEVAL');
    }
  });

  // Also poll cache periodically (MemoryAccessMonitor can miss stores)
  let last = cacheSlot.readPointer();
  const iv = setInterval(() => {
    try {
      const cur = cacheSlot.readPointer();
      if (!cur.equals(last)) {
        log('CACHE POLL ' + last + ' -> ' + cur);
        describeObj(cur, 'CACHEPOLL');
        last = cur;
      }
    } catch (e) {
      clearInterval(iv);
    }
  }, 50);
}

function hookBat() {
  const bat = Process.findModuleByName('nvxdbat.dll');
  if (!bat) { setTimeout(hookBat, 200); return; }
  log('nvxdbat ' + bat.base);

  Interceptor.attach(bat.base.add(0x7f780), {
    onEnter(a) { this.outer = a[0]; this.ppv = a[2]; },
    onLeave(r) {
      if (r.toInt32() !== 0) return;
      const obj = this.ppv.readPointer();
      log('CREATE real outer=' + this.outer + ' obj=' + obj);
      watchFilter(obj, this.outer);
    }
  });

  // resolve / validate for diagnostics when DDS arms
  Interceptor.attach(bat.base.add(0x80900), {
    onEnter(a) {
      this.iface = a[0];
      this.cache = a[0].add(0x40).readPointer();
    },
    onLeave(r) {
      if ((r.toInt32() >>> 0) === 0x80004002 || r.toInt32() === 0)
        log('RESOLVE hr=0x' + (r.toInt32() >>> 0).toString(16) + ' cache=' + this.cache);
    }
  });

  log('bat hooks ready');
}

function hookCom() {
  const combase = Process.findModuleByName('combase.dll');
  Interceptor.attach(combase.findExportByName('CoCreateInstance'), {
    onEnter(a) {
      this.clsid = guid(a[0]);
      this.outer = a[1];
      this.ctx = a[2].toUInt32() >>> 0;
      this.ppv = a[4];
      this.watch = this.clsid.indexOf(FILTER_CLSID) >= 0;
    },
    onLeave(r) {
      if (!this.watch || r.toInt32() !== 0) return;
      const obj = this.ppv.readPointer();
      log('CCI FILTER ctx=0x' + this.ctx.toString(16) + ' outer=' + this.outer + ' obj=' + obj);
      watchFilter(obj, this.outer);
    }
  });
}

function hookNvcpl() {
  function arm() {
    const nvcpl = Process.findModuleByName('NvCpl.dll') || Process.findModuleByName('nvcpl.dll');
    if (!nvcpl) { setTimeout(arm, 400); return; }
    const set = nvcpl.findExportByName('NvCplApiSetSetting');
    if (set) Interceptor.attach(set, {
      onEnter(a) {
        if ((a[2].toInt32() >>> 0) === 0x330) {
          log('ARM DDS');
          // dump all watched caches
          for (const k of Object.keys(watched)) {
            try {
              const filter = ptr(k);
              const cache = filter.add(0x58).readPointer();
              log('ARM cache filter=' + filter + ' cache=' + cache);
              describeObj(cache, 'ARM_CACHE');
              describeObj(filter.add(0x20).readPointer(), 'ARM_OUTER');
            } catch (e) { log('ARM dump ' + e); }
          }
        }
      }
    });
    log('nvcpl ready');
  }
  arm();
}

setImmediate(() => {
  hookCom();
  hookBat();
  hookNvcpl();
  send({ t: 'log', m: 'READY' });
});
