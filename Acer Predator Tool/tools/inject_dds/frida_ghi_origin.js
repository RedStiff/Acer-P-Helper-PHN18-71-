/**
 * Hook real GetHandleInfo by string xref + capture first DDS handle origin.
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

function dump(p, n, tag) {
  if (!p || p.isNull()) { log(tag + ' null'); return; }
  try { log(tag + '\n' + hexdump(p, { length: n })); } catch (e) { log(tag + ' ' + e); }
}

function hookByString(mod, needle, label) {
  const hits = Memory.scanSync(mod.base, mod.size, needle);
  log(label + ' string hits ' + hits.length);
  for (const h of hits.slice(0, 3)) {
    // find code refs: scan for lea/rip-relative is hard; use DebugSymbol / Frida Stalker alternative:
    // pattern: xref via Memory.scan for pointer to string
    const addrBytes = [];
    const a = h.address;
    // search absolute pointer references (x64 often RIP-rel — use Instruction scan near known ProcessGetHandleInfo exports)
    log('  str@ ' + a);
  }
  return hits;
}

function findFuncsReferencing(mod, strAddr, maxScan) {
  // Naive: disassemble whole .text looking for rip-relative refs is expensive.
  // Instead: Frida DebugSymbol.fromName if exported; else scan for call targets near string.
  const results = [];
  // Use Module.enumerateSymbols if available
  try {
    const syms = mod.enumerateSymbols().filter(s => /GetHandleInfo|HandleInfo/i.test(s.name));
    log('syms ' + syms.length);
    for (const s of syms.slice(0, 20)) log('  ' + s.name + ' ' + s.address);
  } catch (e) {}
  return results;
}

function armBat() {
  const bat = Process.findModuleByName('nvxdbat.dll');
  if (!bat) { setTimeout(armBat, 200); return; }
  log('nvxdbat ' + bat.base);

  const needle = '50 72 6f 63 65 73 73 69 6e 67 20 47 65 74 48 61 6e 64 6c 65 49 6e 66 6f'; // Processing GetHandleInfo
  const hits = Memory.scanSync(bat.base, bat.size, needle);
  log('Processing GetHandleInfo str hits=' + hits.length);
  if (hits.length) {
    const str = hits[0].address;
    // Scan executable for RIP-relative LEA/MOV referencing this string.
    // Pattern for lea rcx,[rip+disp32]: 48 8D 0D xx xx xx xx where rip+7+disp = str
    const ranges = bat.enumerateRanges('r-x');
    let refs = 0;
    for (const r of ranges) {
      const code = r.base;
      const size = r.size;
      // scan lea r??,[rip+disp]
      Memory.scan(code, size, '48 8D ?? ?? ?? ?? ??', {
        onMatch(addr, sz) {
          try {
            const modrm = addr.add(2).readU8();
            // RIP-relative: modrm & 0xC7 == 0x05
            if ((modrm & 0xC7) !== 0x05) return 'continue';
            const disp = addr.add(3).readS32();
            const target = addr.add(7).add(disp);
            if (target.equals(str)) {
              refs++;
              log('LEA ref to GHI string @ ' + modOff(addr));
              // walk back to function start (up to 0x200 for CC/CC or prologue)
              let f = addr;
              for (let i = 0; i < 0x300; i++) {
                const b = f.sub(1).readU8();
                if (b === 0xCC || b === 0xC3) { f = f.sub(1); break; }
                // look for typical prologue push rbx / mov [rsp+..]
                f = f.sub(1);
              }
              // better: align to previous int3 padding
              let start = addr;
              for (let i = 0; i < 0x400; i++) {
                const p = addr.sub(i);
                if (p.readU8() === 0xCC && p.add(1).readU8() !== 0xCC) {
                  start = p.add(1);
                  break;
                }
                if (i > 16 && p.readU8() === 0x40 && (p.add(1).readU8() & 0xF0) === 0x50) {
                  // rex push
                  start = p;
                  break;
                }
              }
              log('candidate fn ~ ' + modOff(start) + ' (from lea)');
              try {
                Interceptor.attach(start, {
                  onEnter(a) {
                    log('GHI_FN enter ' + modOff(start) + ' a0=' + a[0] + ' a1=' + a[1] + ' a2=' + a[2] + ' a3=' + a[3]);
                    try { log('a1guid=' + guid(a[1])); } catch (e) {}
                    dump(a[1], 0x30, 'a1');
                    dump(a[2], 0x40, 'a2');
                    this.a1 = a[1]; this.a2 = a[2];
                  },
                  onLeave(r) {
                    log('GHI_FN leave hr/bool=' + r + ' (u=0x' + (r.toInt32() >>> 0).toString(16) + ')');
                    dump(this.a2, 0x60, 'a2_out');
                  }
                });
              } catch (e) { log('attach fail ' + e); }
            }
          } catch (e) {}
          return refs > 8 ? 'stop' : 'continue';
        },
        onComplete() { log('LEA scan done refs=' + refs); }
      });
    }
  }

  // Hook filter Get (0x7f540) and watch first sid 0x7d with non-empty handle — already known.
  // Hook UuidCreate / CoCreateGuid in process to see handle minting
  const rpcrt4 = Process.findModuleByName('rpcrt4.dll');
  if (rpcrt4) {
    const uuid = rpcrt4.findExportByName('UuidCreate');
    if (uuid) Interceptor.attach(uuid, {
      onEnter(a) { this.out = a[0]; },
      onLeave(r) {
        if (r.toInt32() !== 0) return;
        const g = guid(this.out);
        const bt = Thread.backtrace(this.context, Backtracer.ACCURATE).slice(0, 6).map(modOff);
        if (bt.some(x => /nvxd|nvcpl|NvCpl/i.test(x))) {
          log('UuidCreate ' + g + '\n  ' + bt.join('\n  '));
        }
      }
    });
  }
  const ole32 = Process.findModuleByName('combase.dll') || Process.findModuleByName('ole32.dll');
  const ccg = ole32 && ole32.findExportByName('CoCreateGuid');
  if (ccg) Interceptor.attach(ccg, {
    onEnter(a) { this.out = a[0]; },
    onLeave(r) {
      if (r.toInt32() !== 0) return;
      const bt = Thread.backtrace(this.context, Backtracer.ACCURATE).slice(0, 6).map(modOff);
      if (bt.some(x => /nvxd|nvcpl|NvCpl/i.test(x))) {
        log('CoCreateGuid ' + guid(this.out) + '\n  ' + bt.join('\n  '));
      }
    }
  });

  // Wrapper Get — log when sid==0x7d
  Interceptor.attach(bat.base.add(0x7f540), {
    onEnter(a) {
      try {
        const items = a[1].readPointer();
        const sid = items.add(18).readU16();
        if (sid !== 0x7d) return;
        log('GET7d h=' + guid(items) + ' info=' + items.add(16).readU16());
        log('  bt:\n  ' + Thread.backtrace(this.context, Backtracer.ACCURATE).slice(0, 10).map(modOff).join('\n  '));
      } catch (e) {}
    }
  });

  log('bat hooks ready');
}

setImmediate(() => {
  armBat();
  log('READY');
});
