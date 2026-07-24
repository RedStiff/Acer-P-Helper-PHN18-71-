/**
 * Capture ProcessGetHandleInfo (ObjectStublessClient4) + filter wrapper vt[4]
 * during NVIDIA App init — before and during DDS.
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

let ghiCount = 0;
const MAX = 80;

function dumpPtr(p, n, tag) {
  if (!p || p.isNull()) { log(tag + ' null'); return; }
  try {
    log(tag + ' ' + p + ':\n' + hexdump(p, { length: n }));
  } catch (e) {
    log(tag + ' dump fail ' + e);
  }
}

function looksInteresting(p) {
  try {
    const b = new Uint8Array(p.readByteArray(0x40));
    // sid 0x7d at +18 of descriptor, or 0x7d 0x00 anywhere
    for (let i = 0; i < b.length - 1; i++) {
      if (b[i] === 0x7d && b[i + 1] === 0x00) return true;
      if (b[i] === 0x30 && b[i + 1] === 0x03) return true; // 0x330
    }
    // GUID-like non-zero at start
    let nz = 0;
    for (let i = 0; i < 16; i++) if (b[i]) nz++;
    return nz > 4;
  } catch (e) {
    return false;
  }
}

function dumpCollish(a1, tag) {
  try {
    const p0 = a1.readPointer();
    const c = a1.add(8).readU64();
    log(tag + ' collish items=' + p0 + ' count=' + c);
    if (!p0.isNull() && Number(c) > 0 && Number(c) < 64) {
      const n = Math.min(Number(c), 6);
      for (let i = 0; i < n; i++) {
        const d = p0.add(i * 0x20);
        log(tag + '[' + i + '] h=' + guid(d) + ' info=' + d.add(16).readU16() +
          ' sid=0x' + d.add(18).readU16().toString(16) + ' flags=' + d.add(20).readU32());
      }
    }
  } catch (e) {}
  // alternate: maybe (guid*, out*) or (sid, out*)
  try {
    log(tag + ' a1 as guid=' + guid(a1));
  } catch (e) {}
}

function hookStub4() {
  const combase = Process.findModuleByName('combase.dll');
  const p = combase.findExportByName('ObjectStublessClient4');
  Interceptor.attach(p, {
    onEnter(args) {
      if (ghiCount >= MAX) return;
      this.keep = false;
      const a1 = args[1];
      const a2 = args[2];
      const a3 = args[3];
      // Filter: only when caller is nvxdbat / nvcpl / interesting
      const bt0 = Thread.backtrace(this.context, Backtracer.ACCURATE).slice(0, 8).map(modOff);
      const fromNv = bt0.some(x => /nvxd|nvcpl|NvCpl/i.test(x));
      if (!fromNv && !looksInteresting(a1)) return;
      this.keep = true;
      ghiCount++;
      log('==== GHI#' + ghiCount + ' this=' + args[0] + ' a1=' + a1 + ' a2=' + a2 + ' a3=' + a3);
      log('bt:\n  ' + bt0.join('\n  '));
      dumpPtr(a1, 0x80, 'a1');
      dumpCollish(a1, 'a1');
      if (a2 && !a2.isNull()) {
        dumpPtr(a2, 0x40, 'a2');
        try { log('a2 guid=' + guid(a2)); } catch (e) {}
      }
      if (a3 && !a3.isNull() && a3.compare(ptr('0x10000')) > 0) {
        dumpPtr(a3, 0x40, 'a3');
      }
      this.a1 = a1;
      this.a2 = a2;
    },
    onLeave(r) {
      if (!this.keep) return;
      log('GHI out hr=0x' + (r.toInt32() >>> 0).toString(16));
      try {
        if (this.a1 && !this.a1.isNull()) {
          dumpPtr(this.a1, 0x80, 'a1_out');
          dumpCollish(this.a1, 'a1_out');
        }
        if (this.a2 && !this.a2.isNull()) {
          dumpPtr(this.a2, 0x80, 'a2_out');
          try { log('a2_out guid=' + guid(this.a2)); } catch (e) {}
        }
      } catch (e) {
        log('out dump ' + e);
      }
    }
  });
  log('stub4 hooked');
}

function hookBatWrapper4() {
  function arm() {
    const bat = Process.findModuleByName('nvxdbat.dll');
    if (!bat) { setTimeout(arm, 200); return; }
    // secondary iface vtable at +0xf00b8 — slot4 = GetHandleInfo wrapper?
    // From earlier: Get=0x7f540 Set=0x7f5c0; slot4 likely between or nearby
    // Dump vtable of secondary and hook slot 4
    const secVt = bat.base.add(0xf00b8);
    try {
      const slot4 = secVt.add(4 * Process.pointerSize).readPointer();
      log('filter sec vt[4]=' + modOff(slot4));
      Interceptor.attach(slot4, {
        onEnter(a) {
          log('WRAP4 enter this=' + a[0] + ' a1=' + a[1] + ' a2=' + a[2]);
          dumpPtr(a[1], 0x80, 'wrap4_a1');
          dumpCollish(a[1], 'wrap4');
        },
        onLeave(r) {
          log('WRAP4 hr=0x' + (r.toInt32() >>> 0).toString(16));
        }
      });
    } catch (e) {
      log('wrap4 hook fail ' + e);
    }
    // Also scan nearby for GetHandleInfo string xref
    const hits = Memory.scanSync(bat.base, bat.size, '47 65 74 48 61 6e 64 6c 65 49 6e 66 6f'); // GetHandleInfo
    log('GetHandleInfo strings: ' + hits.length);
    for (const h of hits.slice(0, 5)) log('  @ ' + h.address);
    log('bat ready');
  }
  arm();
}

setImmediate(() => {
  hookStub4();
  hookBatWrapper4();
  log('READY');
});
