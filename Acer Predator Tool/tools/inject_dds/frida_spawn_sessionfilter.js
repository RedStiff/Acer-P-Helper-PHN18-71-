/**
 * Cold-start: capture how NvAppStateDataSessionFilter / related objects are created.
 * frida -f "NVIDIA App.exe" -l frida_spawn_sessionfilter.js --no-pause
 * or attach early after launch.
 */
'use strict';

function log(s) { send({ t: 'log', m: String(s) }); }

function guid(p) {
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

function modOff(a) {
  const m = Process.findModuleByAddress(a);
  return m ? (m.name + '+0x' + a.sub(m.base).toString(16)) : String(a);
}

const WATCH = [
  '3F6374C2', // NvAppStateDataSessionFilter
  '5387A36B', // StateDataSessionFilter
  'DCAB0989', // SyncProxy
  '1DC715B2', // NvXDBatchEngine
  '33C89616', // OperationInterceptor
  '4FC7F090', // NvXDSerializer
  'E6AB4158', // IStateData
  'DC09760E', // ISyncProxy / proxy stub
];

function interesting(g) {
  return WATCH.some(x => g.indexOf(x) >= 0);
}

function bt() {
  try {
    return Thread.backtrace(this.context, Backtracer.ACCURATE).slice(0, 12).map(modOff).join('\n  ');
  } catch (e) {
    return '(no bt)';
  }
}

function hookCom() {
  const combase = Process.findModuleByName('combase.dll') || Process.findModuleByName('ole32.dll');
  if (!combase) { setTimeout(hookCom, 200); return; }

  function hook(name) {
    const p = combase.findExportByName(name);
    if (!p) return;
    Interceptor.attach(p, {
      onEnter(args) {
        this.name = name;
        if (name.indexOf('CoCreate') >= 0 || name === 'CoGetClassObject') {
          this.clsid = guid(args[0]);
          this.iid = guid(args[name === 'CoGetClassObject' ? 3 : 3]);
          // CoCreateInstance(clsid, outer, ctx, iid, ppv) — iid is args[3]
          if (name === 'CoCreateInstanceEx') this.iid = '?';
          this.watch = interesting(this.clsid) || interesting(this.iid || '');
        } else if (name === 'CoGetClassObject') {
          this.clsid = guid(args[0]);
          this.iid = guid(args[3]);
          this.watch = interesting(this.clsid);
        }
      },
      onLeave(retval) {
        if (!this.watch) return;
        log(this.name + ' clsid=' + this.clsid + ' iid=' + this.iid + ' hr=' + retval +
          '\n  ' + Thread.backtrace(this.context, Backtracer.ACCURATE).slice(0, 10).map(modOff).join('\n  '));
      }
    });
    log('hooked ' + name);
  }

  hook('CoCreateInstance');
  hook('CoCreateInstanceEx');
  hook('CoGetClassObject');

  // Watch LoadLibrary for nvxdbat
  const k32 = Process.findModuleByName('kernel32.dll');
  Interceptor.attach(k32.findExportByName('LoadLibraryW'), {
    onEnter(a) {
      try {
        const s = a[0].readUtf16String();
        if (s && /nvxd|nvcpl|uxd/i.test(s)) this.path = s;
      } catch (e) {}
    },
    onLeave(r) {
      if (this.path) log('LoadLibraryW ' + this.path + ' -> ' + r);
    }
  });

  log('com hooks ready');
}

function hookBatWhenLoaded() {
  const bat = Process.findModuleByName('nvxdbat.dll') || Process.findModuleByName('NVXDBat.dll');
  if (!bat) { setTimeout(hookBatWhenLoaded, 300); return; }
  log('nvxdbat @ ' + bat.base);
  const gco = bat.findExportByName('DllGetClassObject');
  if (gco) {
    Interceptor.attach(gco, {
      onEnter(a) { this.clsid = guid(a[0]); this.iid = guid(a[1]); },
      onLeave(r) {
        log('nvxdbat!DllGetClassObject clsid=' + this.clsid + ' iid=' + this.iid + ' hr=' + r);
      }
    });
  }
}

setImmediate(() => { hookCom(); hookBatWhenLoaded(); });
