/**
 * Dump DescriptorCollectionRaw / OperationDescriptor from IStateData COM calls.
 */
'use strict';
function log(s) { send({ t: 'log', m: String(s) }); }

function hd(p, n) {
  try {
    if (!p || p.isNull()) return '(null)';
    return hexdump(p, { length: n, ansi: false });
  } catch (e) { return 'err ' + e; }
}

function install() {
  const nvcpl = Process.findModuleByName('NvCpl.dll') || Process.findModuleByName('nvcpl.dll');
  if (!nvcpl) { setTimeout(install, 500); return; }

  let arm = false;
  let dumpBudget = 0;
  Interceptor.attach(nvcpl.findExportByName('NvCplApiSetSetting'), {
    onEnter(a) {
      if ((a[2].toInt32() >>> 0) === 0x330) {
        arm = true;
        dumpBudget = 12;
        log('ARM DDS SetSetting');
      }
    }
  });
  Interceptor.attach(nvcpl.findExportByName('NvCplApiExecute'), {
    onLeave() { setTimeout(() => { arm = false; }, 3000); }
  });

  const combase = Process.findModuleByName('combase.dll');
  const names = {
    3: 'ProcessGetSettings',
    4: 'ProcessGetHandleInfo',
    5: 'ProcessSetSettings',
    6: 'ProcessDoOperation'
  };

  for (const slot of [3, 4, 5, 6]) {
    const exp = 'ObjectStublessClient' + slot;
    const p = combase.findExportByName(exp);
    Interceptor.attach(p, {
      onEnter(args) {
        if (!arm || dumpBudget <= 0) return;
        dumpBudget--;
        const name = names[slot];
        log('==== ' + name + ' this=' + args[0] + ' a1=' + args[1] + ' a2=' + args[2]);
        log('a1 raw:\n' + hd(args[1], 0x100));
        // Heuristics for DescriptorCollectionRaw
        try {
          const a1 = args[1];
          const u32 = [];
          for (let i = 0; i < 16; i++) u32.push(a1.add(i * 4).readU32().toString(16));
          log('a1 u32: ' + u32.join(' '));
          // follow pointers
          for (let off = 0; off < 0x40; off += 8) {
            const q = a1.add(off).readPointer();
            if (q.isNull()) continue;
            // only dump if looks like user heap (not kernel)
            const qi = q.compare(ptr('0x10000'));
            if (qi < 0) continue;
            try {
              const peek = q.readU32();
              // if contains 0x330 nearby dump
              const blob = new Uint8Array(q.readByteArray(0x80));
              let hit = false;
              for (let i = 0; i < blob.length - 4; i++) {
                if (blob[i] === 0x30 && blob[i + 1] === 0x03 && blob[i + 2] === 0 && blob[i + 3] === 0)
                  hit = true;
              }
              if (hit || slot === 5 || slot === 6 || slot === 3) {
                log('follow +' + off.toString(16) + ' -> ' + q + (hit ? ' HAS_0x330' : '') + '\n' + hd(q, 0xC0));
              }
            } catch (e) {}
          }
        } catch (e) { log('parse ' + e); }

        if (slot === 6) {
          log('a2:\n' + hd(args[2], 0x80));
        }
      },
      onLeave(retval) {
        if (!arm) return;
        // only log for our dumps - skip noise
      }
    });
    log('hook ' + exp);
  }

  // Confirm this QI IStateData
  const qiHook = combase.findExportByName('ObjectStublessClient0'); // may not exist; QI is custom
  log('dump ready');
}
setImmediate(install);
