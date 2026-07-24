/**
 * Capture SessionFilter lifecycle: Create → all method calls → DDS Set.
 * Parent: spawn → load → wait READY → resume.
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
const hookedFilters = {};
let ddsArm = false;
let callSeq = 0;

function hookFilterObj(obj, tag) {
  const key = obj.toString();
  if (hookedFilters[key]) return;
  hookedFilters[key] = true;
  log('HOOK FILTER ' + tag + ' ' + obj);
  try {
    log('  dump:\n' + hexdump(obj, { length: 0x60 }));
    log('  vt=' + modOff(obj.readPointer()));
  } catch (e) { log('  dump fail ' + e); }

  // Primary vtable methods 3..12 (skip QI/AddRef/Release noise partially)
  const vt = obj.readPointer();
  for (let slot = 0; slot <= 12; slot++) {
    let fn;
    try { fn = vt.add(slot * Process.pointerSize).readPointer(); } catch (e) { continue; }
    if (fn.isNull()) continue;
    (function (slot, fn) {
      try {
        Interceptor.attach(fn, {
          onEnter(a) {
            // Only log if this == filter (or nearby for MI)
            const self = a[0];
            const delta = self.compare(obj);
            if (delta !== 0 && (delta > 0x40 || delta < -0x40))
              return;
            this.slot = slot;
            this.self = self;
            callSeq++;
            const n = callSeq;
            let extra = '';
            try {
              if (slot >= 3) {
                extra = ' a1=' + a[1] + ' a2=' + a[2] + ' a3=' + a[3];
                // If a1 looks like collection, peek
                if (!a[1].isNull() && a[1].compare(ptr(0x10000)) > 0) {
                  const cnt = a[1].add(8).readU64();
                  if (cnt > 0 && cnt < 32) {
                    const items = a[1].readPointer();
                    extra += ' coll.count=' + cnt + ' handle=' + guid(items);
                    extra += ' info=' + items.add(16).readU16() + ' sid=0x' + items.add(18).readU16().toString(16);
                    extra += ' flags=' + items.add(20).readU32();
                  }
                }
              }
            } catch (e) {}
            log('#' + n + (ddsArm ? ' [DDS] ' : ' ') + 'filt.vt[' + slot + '] ' + modOff(fn) +
              ' self=' + self + extra);
            if (ddsArm || slot >= 3) {
              try {
                log('  bt:\n  ' + Thread.backtrace(this.context, Backtracer.ACCURATE)
                  .slice(0, 8).map(modOff).join('\n  '));
              } catch (e) {}
            }
          },
          onLeave(r) {
            if (this.slot === undefined) return;
            log('  vt[' + this.slot + '] => 0x' + (r.toInt32() >>> 0).toString(16));
          }
        });
      } catch (e) {
        // some slots may be data not code
      }
    })(slot, fn);
  }

  // Secondary iface at +0x18 if present (seen earlier)
  try {
    const p2 = obj.add(0x18).readPointer();
    if (!p2.isNull() && modOff(p2).indexOf('nvxdbat') >= 0) {
      log('  secondary vt@+0x18=' + modOff(p2));
    }
  } catch (e) {}
}

function hookBat() {
  const bat = Process.findModuleByName('nvxdbat.dll');
  if (!bat) { setTimeout(hookBat, 200); return; }
  log('nvxdbat ' + bat.base);

  // CreateInstance wrapper for SessionFilter factory (+0x6acd0)
  const createWrap = bat.base.add(0x6acd0);
  Interceptor.attach(createWrap, {
    onEnter(a) {
      this.self = a[0];
      this.outer = a[1];
      this.iid = guid(a[2]);
      this.ppv = a[3];
      try {
        const f40 = a[0].add(0x40).readPointer();
        this.isFilter = f40.equals(bat.base.add(0x7f780));
      } catch (e) { this.isFilter = false; }
    },
    onLeave(r) {
      if (!this.isFilter && this.iid.indexOf('00000000') < 0) return;
      if (!this.isFilter) {
        // still check if SessionFilter by outer path from CCI
      }
      let obj = ptr(0);
      try { if (r.toInt32() === 0) obj = this.ppv.readPointer(); } catch (e) {}
      if (this.isFilter || (!obj.isNull() && this.outer && !this.outer.isNull())) {
        // Confirm filter: vt at +0xf0100 region or outer was set with filter factory
        try {
          if (this.isFilter || modOff(obj.readPointer()).indexOf('nvxdbat') >= 0) {
            log('CREATE wrap iid=' + this.iid + ' outer=' + this.outer +
              ' hr=0x' + (r.toInt32() >>> 0).toString(16) + ' obj=' + obj +
              ' isFilterFactory=' + this.isFilter);
            if (!obj.isNull()) hookFilterObj(obj, 'create');
          }
        } catch (e) {}
      }
    }
  });

  // Also hook REAL create at +0x7f780
  Interceptor.attach(bat.base.add(0x7f780), {
    onEnter(a) {
      this.outer = a[0];
      this.iid = guid(a[1]);
      this.ppv = a[2];
    },
    onLeave(r) {
      if (r.toInt32() !== 0) return;
      let obj = ptr(0);
      try { obj = this.ppv.readPointer(); } catch (e) {}
      log('CREATE real outer=' + this.outer + ' iid=' + this.iid + ' obj=' + obj);
      if (!obj.isNull()) hookFilterObj(obj, 'real');
    }
  });

  log('bat create hooks ready');
}

function hookCom() {
  const combase = Process.findModuleByName('combase.dll') || Process.findModuleByName('ole32.dll');
  Interceptor.attach(combase.findExportByName('CoCreateInstance'), {
    onEnter(a) {
      this.clsid = guid(a[0]);
      this.outer = a[1];
      this.ctx = a[2].toUInt32() >>> 0;
      this.ppv = a[4];
      this.watch = this.clsid.indexOf(FILTER_CLSID) >= 0;
    },
    onLeave(r) {
      if (!this.watch) return;
      let obj = ptr(0);
      try { if (r.toInt32() === 0) obj = this.ppv.readPointer(); } catch (e) {}
      log('CCI FILTER ctx=0x' + this.ctx.toString(16) + ' outer=' + this.outer +
        ' hr=0x' + (r.toInt32() >>> 0).toString(16) + ' obj=' + obj);
      if (!obj.isNull()) hookFilterObj(obj, 'cci');
    }
  });

  // Stubless Get/Set — capture live handle + filter a2
  for (const [name, slot] of [['ObjectStublessClient3', 3], ['ObjectStublessClient5', 5], ['ObjectStublessClient6', 6]]) {
    const p = combase.findExportByName(name);
    if (!p) continue;
    Interceptor.attach(p, {
      onEnter(a) {
        if (!ddsArm && slot !== 5) return;
        // Heuristic: a1 looks like collection with sid 0x7d
        try {
          const items = a[1].readPointer();
          const sid = items.add(18).readU16();
          if (sid !== 0x7d && !ddsArm) return;
          log('Stubless' + slot + ' this=' + a[0] + ' a2=' + a[2] +
            ' handle=' + guid(items) +
            ' info=' + items.add(16).readU16() +
            ' flags=' + items.add(20).readU32());
          if (!a[2].isNull()) {
            log('  a2 vt=' + modOff(a[2].readPointer()));
            try { log('  a2+30=' + guid(a[2].add(0x30))); } catch (e) {}
            hookFilterObj(a[2], 'stubless-a2');
            // If a2 is proxy, try find local object - dump more
            try { log('  a2:\n' + hexdump(a[2], { length: 0x50 })); } catch (e) {}
          }
        } catch (e) {}
      }
    });
  }
}

function hookNvcpl() {
  function arm() {
    const nvcpl = Process.findModuleByName('NvCpl.dll') || Process.findModuleByName('nvcpl.dll');
    if (!nvcpl) { setTimeout(arm, 500); return; }
    const set = nvcpl.findExportByName('NvCplApiSetSetting');
    const exe = nvcpl.findExportByName('NvCplApiExecute');
    if (set) {
      Interceptor.attach(set, {
        onEnter(a) {
          const id = a[2].toInt32() >>> 0;
          if (id === 0x330) {
            ddsArm = true;
            log('ARM DDS SetSetting');
            try { log('  val:\n' + hexdump(a[3], { length: 0x10 })); } catch (e) {}
          }
        }
      });
    }
    if (exe) {
      Interceptor.attach(exe, {
        onEnter(a) {
          if ((a[0].toInt32() >>> 0) === 0x10000) log('ARM Execute 0x10000');
        },
        onLeave(r) {
          if (ddsArm) {
            log('Execute => 0x' + (r.toInt32() >>> 0).toString(16));
            setTimeout(() => { ddsArm = false; }, 2000);
          }
        }
      });
    }
    log('nvcpl hooks ready');
  }
  arm();
}

setImmediate(() => {
  hookCom();
  hookBat();
  hookNvcpl();
  send({ t: 'log', m: 'READY' });
});
