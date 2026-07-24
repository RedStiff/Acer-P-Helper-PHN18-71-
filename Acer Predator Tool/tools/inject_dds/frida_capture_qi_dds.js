/**
 * Capture nvapi_QueryInterface IDs during a live DDS SetSetting HIT inside NVIDIA App.
 * Run: frida -p <pid> -l frida_capture_qi_dds.js
 * Then trigger: inject_native_dds.exe dgpu
 */
'use strict';

function log(s) { send({ t: 'log', m: String(s) }); }

const seen = {};
function hookQi(modName) {
  const m = Process.findModuleByName(modName);
  if (!m) return false;
  const exp = m.findExportByName('nvapi_QueryInterface');
  if (!exp) {
    log('no nvapi_QueryInterface in ' + modName);
    return false;
  }
  Interceptor.attach(exp, {
    onEnter(args) {
      this.id = args[0].toUInt32();
    },
    onLeave(retval) {
      const id = this.id >>> 0;
      const key = '0x' + id.toString(16);
      if (!seen[key]) {
        seen[key] = { count: 0, ptr: retval.toString() };
        log('QI NEW ' + key + ' -> ' + retval);
      }
      seen[key].count++;
    }
  });
  log('hooked QI in ' + modName + ' @ ' + exp);
  return true;
}

function hookSetSetting() {
  const m = Process.findModuleByName('NvCpl.dll') || Process.findModuleByName('nvcpl.dll');
  if (!m) { log('nvcpl not loaded yet'); return; }
  const set = m.findExportByName('NvCplApiSetSetting');
  const exec = m.findExportByName('NvCplApiExecute');
  if (set) {
    Interceptor.attach(set, {
      onEnter(args) {
        const sid = args[2].toInt32();
        log('SetSetting settingId=0x' + (sid >>> 0).toString(16) + ' a0=' + args[0] + ' val=' + args[3]);
        if ((sid >>> 0) === 0x330) {
          try {
            const p = args[3];
            const bytes = [];
            for (let i = 0; i < 16; i++) bytes.push(('0' + p.add(i).readU8().toString(16)).slice(-2));
            log('DDS val16=' + bytes.join(' '));
          } catch (e) { log('val read ' + e); }
        }
      },
      onLeave(retval) { log('SetSetting -> ' + retval); }
    });
  }
  if (exec) {
    Interceptor.attach(exec, {
      onEnter(args) { log('Execute a0=0x' + (args[0].toInt32() >>> 0).toString(16) + ' a1=' + args[1] + ' a2=' + args[2]); },
      onLeave(retval) { log('Execute -> ' + retval); }
    });
  }
  log('hooked SetSetting/Execute');
}

rpc.exports = {
  summary() { return seen; }
};

setImmediate(() => {
  hookQi('nvapi64.dll');
  hookQi('nvapi.dll');
  // also modules that re-export
  Process.enumerateModules().forEach(m => {
    if (/nvapi|NvUI|nvxdapix/i.test(m.name)) {
      try { hookQi(m.name); } catch (e) {}
    }
  });
  hookSetSetting();
  log('ready — trigger DDS now');
});
