/**
 * Trace DDS handle mint: UuidCreate in nvxd*, first sid=0x7d descriptor,
 * and SetExistingDataSource / StoreDataItem in nvxdplcy+nvxdbat.
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
function bt(ctx, n) {
  try {
    return Thread.backtrace(ctx, Backtracer.ACCURATE).slice(0, n || 12).map(modOff).join('\n  ');
  } catch (e) { return '?'; }
}

const seen7d = {};
const uuidFromNv = [];
let dataSrcHooks = 0;

function hookUuid() {
  const rpcrt4 = Process.findModuleByName('rpcrt4.dll');
  const uuid = rpcrt4 && rpcrt4.findExportByName('UuidCreate');
  if (uuid) Interceptor.attach(uuid, {
    onEnter(a) { this.out = a[0]; },
    onLeave(r) {
      if (r.toInt32() !== 0) return;
      const stack = bt(this.context, 8);
      if (!/nvxd|NvCpl|nvcpl/i.test(stack)) return;
      const g = guid(this.out);
      uuidFromNv.push(g);
      log('UUID ' + g + '\n  ' + stack);
    }
  });
  const combase = Process.findModuleByName('combase.dll');
  const ccg = combase && combase.findExportByName('CoCreateGuid');
  if (ccg) Interceptor.attach(ccg, {
    onEnter(a) { this.out = a[0]; },
    onLeave(r) {
      if (r.toInt32() !== 0) return;
      const stack = bt(this.context, 8);
      if (!/nvxd|NvCpl|nvcpl/i.test(stack)) return;
      log('CoCreateGuid ' + guid(this.out) + '\n  ' + stack);
    }
  });
  log('uuid hooks');
}

function hookStringRefs(mod, asciiNeedle, label, maxRefs) {
  const bytes = [];
  for (let i = 0; i < asciiNeedle.length; i++)
    bytes.push(('0' + asciiNeedle.charCodeAt(i).toString(16)).slice(-2));
  const hits = Memory.scanSync(mod.base, mod.size, bytes.join(' '));
  log(label + ' strHits=' + hits.length);
  if (!hits.length) return;
  const str = hits[0].address;
  let refs = 0;
  for (const r of mod.enumerateRanges('r-x')) {
    Memory.scan(r.base, r.size, '48 8D ?? ?? ?? ?? ??', {
      onMatch(addr) {
        try {
          const modrm = addr.add(2).readU8();
          if ((modrm & 0xC7) !== 0x05) return 'continue';
          const disp = addr.add(3).readS32();
          const target = addr.add(7).add(disp);
          if (!target.equals(str)) return 'continue';
          refs++;
          // find function start via INT3 padding
          let start = addr;
          for (let i = 0; i < 0x600; i++) {
            const p = addr.sub(i);
            if (i > 8 && p.readU8() === 0xCC && p.add(1).readU8() !== 0xCC) {
              start = p.add(1);
              break;
            }
          }
          log(label + ' LEA@' + modOff(addr) + ' fn~' + modOff(start));
          try {
            Interceptor.attach(start, {
              onEnter(a) {
                log(label + '_FN ' + modOff(start) + ' a0=' + a[0] + ' a1=' + a[1] + ' a2=' + a[2] + ' a3=' + a[3]);
                try {
                  if (a[1] && !a[1].isNull() && a[1].compare(ptr('0x10000')) > 0)
                    log('  a1guid=' + guid(a[1]));
                  if (a[2] && !a[2].isNull() && a[2].compare(ptr('0x10000')) > 0)
                    log('  a2guid=' + guid(a[2]));
                } catch (e) {}
                log('  bt:\n  ' + bt(this.context, 10));
              }
            });
            dataSrcHooks++;
          } catch (e) {
            log(label + ' attach fail ' + e);
          }
        } catch (e) {}
        return refs >= (maxRefs || 4) ? 'stop' : 'continue';
      },
      onComplete() { log(label + ' leaDone refs=' + refs); }
    });
  }
}

function onDesc(tag, coll, ctx) {
  try {
    const items = coll.readPointer();
    const count = Number(coll.add(8).readU64());
    const n = Math.min(count || 0, 8);
    for (let i = 0; i < n; i++) {
      const d = items.add(i * 0x20);
      const sid = d.add(18).readU16();
      if (sid !== 0x7d) continue;
      const h = guid(d);
      const info = d.add(16).readU16();
      if (!seen7d[h]) {
        seen7d[h] = true;
        log('FIRST7d ' + tag + ' h=' + h + ' info=' + info);
        log('  bt:\n  ' + bt(ctx, 14));
        // Was this UUID minted in-process?
        if (uuidFromNv.indexOf(h) >= 0) log('  UUID_MATCH in-process mint');
        else log('  UUID_NOT_LOCAL (server/persisted?)');
      }
    }
  } catch (e) {}
}

function armBat() {
  const bat = Process.findModuleByName('nvxdbat.dll');
  if (!bat) { setTimeout(armBat, 200); return; }
  log('nvxdbat ' + bat.base);
  Interceptor.attach(bat.base.add(0x7f540), {
    onEnter(a) { onDesc('GET', a[1], this.context); }
  });
  Interceptor.attach(bat.base.add(0x7f5c0), {
    onEnter(a) { onDesc('SET', a[1], this.context); }
  });
  // DoOp wrapper ~0x7f6a4 from earlier capture
  try {
    Interceptor.attach(bat.base.add(0x7f6a4), {
      onEnter(a) {
        try {
          const g = guid(a[1]);
          const u16a = a[1].add(16).readU16();
          const u16b = a[1].add(18).readU16();
          if (u16b === 0x7d || g.indexOf('747D8BF5') >= 0 || g.indexOf('D812F4FF') >= 0)
            log('DOP_WRAP h=' + g + ' ' + u16a + ',' + u16b + '\n  ' + bt(this.context, 10));
        } catch (e) {}
      }
    });
  } catch (e) {}
  hookStringRefs(bat, 'SetExistingDataSource', 'BAT_SetExisting', 3);
  hookStringRefs(bat, 'StoreDataItem', 'BAT_StoreData', 2);
  log('bat ready');
}

function armPlcy() {
  function go() {
    const plcy = Process.findModuleByName('nvxdplcy.dll');
    if (!plcy) { setTimeout(go, 300); return; }
    log('nvxdplcy ' + plcy.base);
    hookStringRefs(plcy, 'SetExistingDataSource', 'PLCY_SetExisting', 3);
    hookStringRefs(plcy, 'StoreDataItem', 'PLCY_StoreData', 3);
    hookStringRefs(plcy, 'FetchHandleInfo', 'PLCY_FetchHI', 2);
    hookStringRefs(plcy, 'GetVector', 'PLCY_GetVector', 2);
    // also "Processing GetHandleInfo"
    hookStringRefs(plcy, 'Processing GetHandleInfo', 'PLCY_GHI', 2);
    log('plcy ready');
  }
  go();
}

setImmediate(() => {
  hookUuid();
  armBat();
  armPlcy();
  log('READY');
});
