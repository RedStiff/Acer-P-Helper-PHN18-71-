/**
 * Focused dump of ProcessSetSettings + ProcessDoOperation descriptor payloads.
 */
'use strict';
function log(s) { send({ t: 'log', m: String(s) }); }
function hd(p, n) {
  try { return hexdump(p, { length: n, ansi: false }); }
  catch (e) { return 'err ' + e; }
}
function scan330(p, n) {
  try {
    const b = new Uint8Array(p.readByteArray(n));
    const hits = [];
    for (let i = 0; i < b.length - 3; i++) {
      if (b[i] === 0x30 && b[i+1] === 0x03 && b[i+2] === 0 && b[i+3] === 0) hits.push(i);
      // mux val patterns
      if (b[i] === 1 && b[i+1] === 0 && b[i+2] === 0 && b[i+3] === 0 &&
          (b[i+4] === 1 || b[i+4] === 2) && b[i+5] === 0 && b[i+6] === 0 && b[i+7] === 0)
        hits.push('mux@' + i);
      if (b[i] === 1 && b[i+1] === 1 && b[i+2] === 0 && b[i+3] === 0)
        hits.push('auto@' + i);
    }
    return hits;
  } catch (e) { return ['scanerr']; }
}

function dumpCollection(a1, tag) {
  log('COLLECTION ' + tag + ' @ ' + a1);
  log(hd(a1, 0x20));
  const items = a1.readPointer();
  const count = a1.add(8).readU32();
  log('items=' + items + ' count=' + count);
  if (items.isNull() || count === 0 || count > 32) return;
  // each DescriptorRaw seems 0x20+ bytes; dump first few as 0x28 stride candidates
  for (let i = 0; i < count; i++) {
    for (const stride of [0x20, 0x28, 0x30, 0x38]) {
      const d = items.add(i * stride);
      log('desc[' + i + '] stride=0x' + stride.toString(16) + '\n' + hd(d, stride));
      const hits = scan330(d, stride);
      if (hits.length) log('  hits in desc: ' + hits);
      // try ptr at +0x18
      try {
        const dp = d.add(0x18).readPointer();
        if (!dp.isNull() && dp.compare(ptr('0x10000')) > 0) {
          log('  data@+18=' + dp + ' hits=' + scan330(dp, 0x80) + '\n' + hd(dp, 0x80));
        }
      } catch (e) {}
      try {
        const dp = d.add(0x20).readPointer();
        if (!dp.isNull() && dp.compare(ptr('0x10000')) > 0) {
          log('  data@+20=' + dp + ' hits=' + scan330(dp, 0x80) + '\n' + hd(dp, 0x80));
        }
      } catch (e) {}
    }
  }
  // also scan whole items block
  log('items block hits=' + scan330(items, 0x200) + '\n' + hd(items, Math.min(0x200, count * 0x40)));
}

function install() {
  const nvcpl = Process.findModuleByName('NvCpl.dll') || Process.findModuleByName('nvcpl.dll');
  if (!nvcpl) { setTimeout(install, 500); return; }
  let arm = false;
  Interceptor.attach(nvcpl.findExportByName('NvCplApiSetSetting'), {
    onEnter(a) {
      if ((a[2].toInt32() >>> 0) === 0x330) {
        arm = true;
        log('ARM SetSetting val=\n' + hd(a[3], 0x20));
      }
    }
  });
  Interceptor.attach(nvcpl.findExportByName('NvCplApiExecute'), {
    onEnter(a) { if (arm) log('ARM Execute ' + a[0]); },
    onLeave() { setTimeout(() => { arm = false; }, 2500); }
  });

  const combase = Process.findModuleByName('combase.dll');
  Interceptor.attach(combase.findExportByName('ObjectStublessClient5'), {
    onEnter(args) {
      if (!arm) return;
      log('===== ProcessSetSettings =====');
      dumpCollection(args[1], 'set');
    }
  });
  Interceptor.attach(combase.findExportByName('ObjectStublessClient6'), {
    onEnter(args) {
      if (!arm) return;
      log('===== ProcessDoOperation =====');
      log('a1=\n' + hd(args[1], 0x80));
      log('a1 hits=' + scan330(args[1], 0x80));
      try {
        const p = args[1].readPointer();
        log('a1->\n' + hd(p, 0x100) + ' hits=' + scan330(p, 0x100));
      } catch (e) {}
      log('a2=\n' + hd(args[2], 0x40));
    }
  });
  log('focused dump ready');
}
setImmediate(install);
