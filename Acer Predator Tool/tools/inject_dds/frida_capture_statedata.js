/**
 * Capture IStateData ProcessSetSettings / ProcessDoOperation args during DDS HIT.
 * Attach to NVIDIA App while inject_native_dds runs, OR we hook after CoCreate in-process.
 *
 * Usage:
 *   frida -n "NVIDIA App.exe" -l frida_capture_statedata.js
 *   then: inject_native_dds.exe dgpu
 */
'use strict';

function log(s) { send({ t: 'log', m: String(s) }); }

function hexdumpPtr(p, n) {
  try {
    if (p.isNull()) return '(null)';
    return hexdump(p, { length: n, ansi: false });
  } catch (e) {
    return 'dump-err ' + e;
  }
}

function guidStr(p) {
  try {
    const b = new Uint8Array(p.readByteArray(16));
    const d1 = b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24);
    const d2 = b[4] | (b[5] << 8);
    const d3 = b[6] | (b[7] << 8);
    const h = [...b].map(x => ('0' + x.toString(16)).slice(-2)).join('');
    return ('{' + ('00000000' + (d1 >>> 0).toString(16)).slice(-8) + '-' +
      ('0000' + d2.toString(16)).slice(-4) + '-' +
      ('0000' + d3.toString(16)).slice(-4) + '-' +
      h.slice(16, 20) + '-' + h.slice(20) + '}').toUpperCase();
  } catch (e) { return '?'; }
}

const IID_IStateData = '{E6AB4158-38B8-4FDF-85CF-ADC2E9870970}';
const hookedObjs = {};

function hookStateData(iface, tag) {
  const key = iface.toString();
  if (hookedObjs[key]) return;
  hookedObjs[key] = true;
  const vt = iface.readPointer();
  const names = ['QI', 'AddRef', 'Release', 'ProcessGetSettings', 'ProcessGetHandleInfo', 'ProcessSetSettings', 'ProcessDoOperation'];
  for (let i = 3; i <= 6; i++) {
    const fn = vt.add(i * Process.pointerSize).readPointer();
    Interceptor.attach(fn, {
      onEnter(args) {
        this.slot = i;
        this.name = names[i];
        this.a1 = args[1];
        this.a2 = args[2];
        log('>>> ' + tag + ' ' + names[i] + ' this=' + args[0] + ' a1=' + args[1] + ' a2=' + args[2]);
        if (i === 3 || i === 5) {
          // DescriptorCollectionRaw*
          log('collection:\n' + hexdumpPtr(args[1], 0x80));
          try {
            const p0 = args[1].readPointer();
            const p1 = args[1].add(8).readPointer();
            const u0 = args[1].readU64();
            const u1 = args[1].add(8).readU64();
            log('coll fields p0=' + p0 + ' p1=' + p1 + ' u0=' + u0 + ' u1=' + u1);
            if (!p0.isNull()) log('p0:\n' + hexdumpPtr(p0, 0x100));
            if (!p1.isNull() && !p1.equals(p0)) log('p1:\n' + hexdumpPtr(p1, 0x100));
            // try interpret as {count, descriptors*}
            const c32 = args[1].readU32();
            const c64 = args[1].readU64();
            log('as count32=' + c32 + ' count64=' + c64);
          } catch (e) { log('coll parse ' + e); }
        }
        if (i === 6) {
          log('opdesc a1:\n' + hexdumpPtr(args[1], 0x80));
          log('a2:\n' + hexdumpPtr(args[2], 0x40));
        }
        if (i === 4) {
          log('handle=' + args[1] + ' info*\n' + hexdumpPtr(args[2], 0x40));
        }
      },
      onLeave(retval) {
        log('<<< ' + this.name + ' hr=' + retval);
      }
    });
  }
  log('hooked IStateData vt=' + vt + ' tag=' + tag);
}

function tryHookFromUnknown(punk, tag) {
  try {
    const qi = punk.readPointer().readPointer(); // QI
    const iidBuf = Memory.alloc(16);
    // E6AB4158-38B8-4FDF-85CF-ADC2E9870970
    iidBuf.writeByteArray([0x58,0x41,0xAB,0xE6, 0xB8,0x38, 0xDF,0x4F, 0x85,0xCF, 0xAD,0xC2,0xE9,0x87,0x09,0x70]);
    const out = Memory.alloc(Process.pointerSize);
    const hr = new NativeFunction(qi, 'int', ['pointer', 'pointer', 'pointer'])(punk, iidBuf, out);
    if (hr === 0) {
      const iface = out.readPointer();
      log('QI IStateData OK from ' + tag + ' -> ' + iface);
      hookStateData(iface, tag);
    }
  } catch (e) {
    log('tryHook err ' + e);
  }
}

function install() {
  const nvcpl = Process.findModuleByName('NvCpl.dll') || Process.findModuleByName('nvcpl.dll');
  if (!nvcpl) { log('wait nvcpl...'); setTimeout(install, 1000); return; }

  const set = nvcpl.findExportByName('NvCplApiSetSetting');
  const exec = nvcpl.findExportByName('NvCplApiExecute');
  if (set) Interceptor.attach(set, {
    onEnter(a) {
      const id = a[2].toInt32() >>> 0;
      if (id === 0x330) {
        log('NvCplApiSetSetting DDS hdr=\n' + hexdumpPtr(a[1], 0x40) + '\nval=\n' + hexdumpPtr(a[3], 0x20));
      }
    }
  });
  if (exec) Interceptor.attach(exec, {
    onEnter(a) { log('NvCplApiExecute a0=' + a[0] + ' a1=' + a[1] + ' a2=' + a[2]); }
  });

  // CoCreateInstance SyncProxy → then QI IStateData
  const combase = Process.findModuleByName('combase.dll') || Process.findModuleByName('ole32.dll');
  const cci = combase.findExportByName('CoCreateInstance');
  Interceptor.attach(cci, {
    onEnter(a) {
      this.clsid = guidStr(a[0]);
      this.ppv = a[4];
    },
    onLeave(r) {
      if (this.clsid.indexOf('DCAB0989') >= 0 && r.toInt32() === 0) {
        const punk = this.ppv.readPointer();
        log('CoCreate SyncProxy punk=' + punk);
        tryHookFromUnknown(punk, 'SyncProxy');
      }
    }
  });

  // Also: when any QI asks for IStateData, hook result
  // Hook ole32 ObjectStublessClient? Too heavy.
  // Scan NvXDCore for ProcessSetSettings symbol-ish via export — none.
  // Hook by finding SyncProxy if already alive: skip.

  // Fallback: stalker-free — hook NVXDBat proxy stub ProcessSetSettings if present
  const bat = Process.findModuleByName('NVXDBat.dll') || Process.findModuleByName('nvxdbat.dll');
  if (bat) {
    log('NVXDBat base=' + bat.base + ' size=' + bat.size);
  }

  log('capture ready — trigger DDS HIT');
}

setImmediate(install);
