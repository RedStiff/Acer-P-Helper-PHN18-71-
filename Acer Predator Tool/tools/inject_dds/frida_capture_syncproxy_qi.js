/**
 * Capture QueryInterface IIDs on SyncProxy / UXD objects during DDS SetSetting.
 * One-shot RE only — product path must not use NVIDIA App.
 *
 * frida -n "NVIDIA App.exe" -l frida_capture_syncproxy_qi.js
 * then trigger inject_native_dds.exe dgpu
 */
'use strict';
function log(s) { send({ t: 'log', m: String(s) }); }

function guidFromPtr(p) {
  try {
    const b = new Uint8Array(p.readByteArray(16));
    const d1 = b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24);
    const d2 = b[4] | (b[5] << 8);
    const d3 = b[6] | (b[7] << 8);
    const hex = [...b].map(x => ('0' + x.toString(16)).slice(-2)).join('');
    return (
      '{' +
      ('00000000' + (d1 >>> 0).toString(16)).slice(-8) + '-' +
      ('0000' + d2.toString(16)).slice(-4) + '-' +
      ('0000' + d3.toString(16)).slice(-4) + '-' +
      hex.slice(16, 20) + '-' + hex.slice(20) + '}'
    ).toUpperCase();
  } catch (e) {
    return 'err';
  }
}

const syncClsid = '{DCAB0989-1301-4319-BE5F-ADE89F88581C}'.toUpperCase();
const seen = {};

function hookQi() {
  const ole = Process.findModuleByName('combase.dll') || Process.findModuleByName('ole32.dll');
  if (!ole) { log('no combase'); return; }
  const qi = ole.findExportByName('CoCreateInstance');
  if (qi) {
    Interceptor.attach(qi, {
      onEnter(args) {
        this.clsid = guidFromPtr(args[0]);
        this.iid = guidFromPtr(args[3]);
      },
      onLeave(retval) {
        if (this.clsid.indexOf('DCAB0989') >= 0 || this.clsid.indexOf('49E6B51C') >= 0 ||
            this.clsid.indexOf('7331B944') >= 0 || this.clsid.indexOf('5DF4E7C5') >= 0) {
          log('CoCreate clsid=' + this.clsid + ' iid=' + this.iid + ' hr=' + retval);
        }
      }
    });
  }

  // Hook IUnknown::QueryInterface via COM — hard globally; instead hook nvcpl SetSetting window
  const nvcpl = Process.findModuleByName('NvCpl.dll') || Process.findModuleByName('nvcpl.dll');
  if (nvcpl) {
    const set = nvcpl.findExportByName('NvCplApiSetSetting');
    const exec = nvcpl.findExportByName('NvCplApiExecute');
    if (set) Interceptor.attach(set, {
      onEnter(a) {
        const id = a[2].toInt32() >>> 0;
        if (id === 0x330) log('DDS SetSetting begin');
      }
    });
    if (exec) Interceptor.attach(exec, {
      onEnter() { log('Execute begin — watch QI'); this.watch = true; },
      onLeave(r) { log('Execute end ' + r); this.watch = false; }
    });
  }
  log('hooks ready');
}

// Brute: intercept all QueryInterface by replacing vtable is too heavy.
// Instead enumerate modules and hook known proxy stubs if present.

setImmediate(hookQi);
