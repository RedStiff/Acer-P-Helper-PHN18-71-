/**
 * Hook RPC / stubless client during DDS Execute to catch marshaled IStateData calls.
 */
'use strict';
function log(s) { send({ t: 'log', m: String(s) }); }

function install() {
  const nvcpl = Process.findModuleByName('NvCpl.dll') || Process.findModuleByName('nvcpl.dll');
  if (!nvcpl) { setTimeout(install, 500); return; }

  let arm = false;
  const set = nvcpl.findExportByName('NvCplApiSetSetting');
  const exec = nvcpl.findExportByName('NvCplApiExecute');
  Interceptor.attach(set, {
    onEnter(a) {
      if ((a[2].toInt32() >>> 0) === 0x330) {
        arm = true;
        log('ARM SetSetting DDS');
      }
    }
  });
  Interceptor.attach(exec, {
    onEnter() { if (arm) log('ARM Execute'); },
    onLeave() { setTimeout(() => { arm = false; }, 2000); }
  });

  function hookExport(modName, exp) {
    const m = Process.findModuleByName(modName);
    if (!m) return;
    const p = m.findExportByName(exp);
    if (!p) return;
    Interceptor.attach(p, {
      onEnter(args) {
        if (!arm) return;
        const bt = Thread.backtrace(this.context, Backtracer.ACCURATE).map(DebugSymbol.fromAddress).slice(0, 8);
        log(exp + '@' + modName + ' a0=' + args[0] + ' a1=' + args[1] + ' a2=' + args[2] + '\n  ' + bt.join('\n  '));
      }
    });
    log('hooked ' + modName + '!' + exp);
  }

  ['combase.dll', 'rpcrt4.dll', 'ole32.dll'].forEach(mod => {
    hookExport(mod, 'NdrClientCall3');
    hookExport(mod, 'NdrClientCall2');
    hookExport(mod, 'Ndr64AsyncClientCall');
    hookExport(mod, 'ObjectStublessClient3');
    hookExport(mod, 'ObjectStublessClient4');
    hookExport(mod, 'ObjectStublessClient5');
    hookExport(mod, 'ObjectStublessClient6');
    hookExport(mod, 'ObjectStublessClient7');
    hookExport(mod, 'CoCreateInstance');
  });

  // Hook NVXDBat if loaded - any export
  const bat = Process.findModuleByName('NVXDBat.dll') || Process.findModuleByName('nvxdbat.dll');
  if (bat) {
    log('bat exports sample:');
    bat.enumerateExports().slice(0, 30).forEach(e => log('  ' + e.type + ' ' + e.name));
  }

  // Also watch CreateFile/CreateNamedPipe for UXD IPC
  const k32 = Process.findModuleByName('kernel32.dll');
  Interceptor.attach(k32.findExportByName('CreateFileW'), {
    onEnter(a) {
      if (!arm) return;
      try {
        const s = a[0].readUtf16String();
        if (s && /nv|uxd|nvidia|pipe|container/i.test(s)) log('CreateFileW ' + s);
      } catch (e) {}
    }
  });

  log('rpc watch ready');
}
setImmediate(install);
