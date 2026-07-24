/**
 * Frida: capture NvCplApiSetSetting / GetSetting args during SetDDSState.
 * Usage (after CDP host is up):
 *   frida -n "NVIDIA App.exe" -l frida_capture_setsetting.js
 * Then run: python _hit_setdds.py dgpu
 */
'use strict';

function hexdumpPtr(p, n) {
  try {
    return hexdump(p, { length: n, ansi: false });
  } catch (e) {
    return String(e);
  }
}

function hookExport(modName, expName) {
  const m = Process.findModuleByName(modName);
  if (!m) {
    console.log('module missing ' + modName);
    return;
  }
  const addr = m.findExportByName(expName);
  if (!addr) {
    console.log('export missing ' + expName);
    return;
  }
  console.log('hook ' + modName + '!' + expName + ' @ ' + addr);
  Interceptor.attach(addr, {
    onEnter(args) {
      this.a0 = args[0];
      this.a1 = args[1];
      this.a2 = args[2];
      this.a3 = args[3];
      console.log('\n>>> ' + expName);
      console.log('  a0=' + args[0] + ' a1=' + args[1] + ' a2=' + args[2] + ' a3=' + args[3]);
      // Common guesses: (settingId, void* value, size) or (void* ctx, settingId, void* value)
      try {
        const idA = args[0].toInt32();
        const idB = args[1].toInt32();
        console.log('  as_i32 a0=' + idA + ' a1=' + idB);
      } catch (_) {}
      for (let i = 0; i < 4; i++) {
        try {
          if (!args[i].isNull()) {
            console.log('  dump a' + i + ':\n' + hexdumpPtr(args[i], 64));
          }
        } catch (_) {}
      }
    },
    onLeave(retval) {
      console.log('<<< ' + expName + ' ret=' + retval);
    }
  });
}

function main() {
  hookExport('nvcpl.dll', 'NvCplApiSetSetting');
  hookExport('nvcpl.dll', 'NvCplApiGetSetting');
  hookExport('nvcpl.dll', 'NvCplApiGetSettingType');
  hookExport('nvcpl.dll', 'NvCplApiEnumSettingIDs');
  hookExport('nvxdapix.dll', 'nvapi_QueryInterface');
  // Some builds export differently
  const nx = Process.findModuleByName('nvxdapix.dll');
  if (nx) {
    nx.enumerateExports().forEach(function (e) {
      if (/QueryInterface|SetDisplayMux|DISP_/i.test(e.name)) {
        console.log('nx export ' + e.name);
      }
    });
  }
  console.log('ready — trigger SetDDSState');
}

setImmediate(main);
