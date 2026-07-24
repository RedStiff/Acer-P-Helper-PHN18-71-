/**
 * Capture DoOperation + any call that first introduces sid 0x7d handle.
 * Also hook NvCpl get/set 0x330 and dump nearby handle cache if found.
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

const seen7d = {};
let dopCount = 0;

function armBat() {
  const bat = Process.findModuleByName('nvxdbat.dll');
  if (!bat) { setTimeout(armBat, 200); return; }
  log('nvxdbat ' + bat.base);

  // Get/Set wrappers — record first 0x7d handle
  function onColl(tag, coll) {
    try {
      const items = coll.readPointer();
      const count = Number(coll.add(8).readU64());
      const n = Math.min(count, 8);
      for (let i = 0; i < n; i++) {
        const d = items.add(i * 0x20);
        const sid = d.add(18).readU16();
        if (sid !== 0x7d) continue;
        const h = guid(d);
        if (!seen7d[h]) {
          seen7d[h] = true;
          log('FIRST7d ' + tag + ' h=' + h + ' info=' + d.add(16).readU16());
          log('  bt:\n  ' + Thread.backtrace(this.context || {}, Backtracer.ACCURATE)
            .slice(0, 12).map(modOff).join('\n  '));
        } else {
          log('AGAIN7d ' + tag + ' h=' + h + ' info=' + d.add(16).readU16());
        }
      }
    } catch (e) {}
  }

  Interceptor.attach(bat.base.add(0x7f540), {
    onEnter(a) { this.coll = a[1]; onColl.call(this, 'GET', a[1]); },
  });
  Interceptor.attach(bat.base.add(0x7f5c0), {
    onEnter(a) { onColl.call(this, 'SET', a[1]); },
  });

  // Hook IStateData DoOperation via resolving OLD iface calls: stubless 6 is noisy.
  // Instead hook string "ProcessDoOperation" LEA refs — skip; hook known sync path.
  // Watch ObjectStublessClient6 only when a1 starts with D812F4FF or has 0x7d
  const combase = Process.findModuleByName('combase.dll');
  Interceptor.attach(combase.findExportByName('ObjectStublessClient6'), {
    onEnter(a) {
      try {
        const g = guid(a[1]);
        if (g.indexOf('D812F4FF') >= 0 || g.indexOf('00000000') < 0) {
          if (dopCount++ > 40) return;
          log('DOP#' + dopCount + ' h/op=' + g + ' +16=' + a[1].add(16).readU16() +
            ',' + a[1].add(18).readU16() + ' a2=' + a[2]);
          log('  bt:\n  ' + Thread.backtrace(this.context, Backtracer.ACCURATE)
            .slice(0, 8).map(modOff).join('\n  '));
        }
      } catch (e) {}
    }
  });

  log('bat ready');
}

function armNvcpl() {
  function go() {
    const nvcpl = Process.findModuleByName('NvCpl.dll') || Process.findModuleByName('nvcpl.dll');
    if (!nvcpl) { setTimeout(go, 400); return; }
    for (const name of ['NvCplApiGetSetting', 'NvCplApiSetSetting', 'NvCplApiExecute']) {
      const p = nvcpl.findExportByName(name);
      if (!p) continue;
      Interceptor.attach(p, {
        onEnter(a) {
          const sid = a[2].toInt32() >>> 0;
          if (sid !== 0x330) return;
          log(name + ' 0x330 a0=' + a[0] + ' a1=' + a[1] + ' a3=' + a[3]);
          try {
            log('  bt:\n  ' + Thread.backtrace(this.context, Backtracer.ACCURATE)
              .slice(0, 10).map(modOff).join('\n  '));
            // dump a1/a3 heuristically
            if (!a[1].isNull()) log('a1:\n' + hexdump(a[1], { length: 0x40 }));
            if (a[3] && !a[3].isNull() && a[3].compare(ptr('0x10000')) > 0)
              log('a3:\n' + hexdump(a[3], { length: 0x80 }));
          } catch (e) {}
        }
      });
    }
    log('nvcpl ready');
  }
  go();
}

setImmediate(() => {
  armBat();
  armNvcpl();
  log('READY');
});
