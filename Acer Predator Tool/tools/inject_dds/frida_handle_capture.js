/**
 * Capture live DDS handle from App Get/Set on filter wrapper / stubless.
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

function dumpColl(coll, tag) {
  try {
    const items = coll.readPointer();
    const count = coll.add(8).readU64();
    log(tag + ' count=' + count + ' items=' + items);
    const n = Math.min(Number(count), 4);
    for (let i = 0; i < n; i++) {
      const d = items.add(i * 0x20);
      const h = guid(d);
      const info = d.add(16).readU16();
      const sid = d.add(18).readU16();
      const flags = d.add(20).readU32();
      const data = d.add(24).readPointer();
      let val = '';
      if (!data.isNull()) {
        try {
          val = ' type=' + data.readU32() + ' size=' + data.add(4).readU32() +
            ' v=' + data.add(8).readU32();
        } catch (e) {}
      }
      log(tag + '[' + i + '] h=' + h + ' info=' + info + ' sid=0x' + sid.toString(16) +
        ' flags=' + flags + val);
    }
  } catch (e) {
    log(tag + ' dump fail ' + e);
  }
}

function hookBat() {
  const bat = Process.findModuleByName('nvxdbat.dll');
  if (!bat) { setTimeout(hookBat, 200); return; }
  log('nvxdbat ' + bat.base);

  // wrapper Get / Set
  Interceptor.attach(bat.base.add(0x7f540), {
    onEnter(a) {
      this.coll = a[1];
      dumpColl(this.coll, 'GET_IN');
    },
    onLeave(r) {
      log('GET_OUT hr=0x' + (r.toInt32() >>> 0).toString(16));
      dumpColl(this.coll, 'GET_OUT');
    }
  });
  Interceptor.attach(bat.base.add(0x7f5c0), {
    onEnter(a) {
      this.coll = a[1];
      dumpColl(this.coll, 'SET_IN');
    },
    onLeave(r) {
      log('SET_OUT hr=0x' + (r.toInt32() >>> 0).toString(16));
    }
  });

  // GetHandleInfo if exported via same iface - hook vt slot by watching calls with sid 0x7d
  log('bat hooks ready');
}

function hookNvcpl() {
  function arm() {
    const nvcpl = Process.findModuleByName('NvCpl.dll') || Process.findModuleByName('nvcpl.dll');
    if (!nvcpl) { setTimeout(arm, 400); return; }
    const set = nvcpl.findExportByName('NvCplApiSetSetting');
    const get = nvcpl.findExportByName('NvCplApiGetSetting');
    if (set) Interceptor.attach(set, {
      onEnter(a) {
        if ((a[2].toInt32() >>> 0) === 0x330) log('NVCPL SET 0x330');
      }
    });
    if (get) Interceptor.attach(get, {
      onEnter(a) {
        if ((a[2].toInt32() >>> 0) === 0x330) log('NVCPL GET 0x330');
      }
    });
    log('nvcpl ready');
  }
  arm();
}

setImmediate(() => {
  hookBat();
  hookNvcpl();
  log('READY');
});
