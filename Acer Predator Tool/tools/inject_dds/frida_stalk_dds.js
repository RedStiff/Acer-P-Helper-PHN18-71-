/**
 * Follow NvCplApiSetSetting/Execute into UXD calls via stalker call summaries.
 * frida -n "NVIDIA App.exe" -l frida_stalk_dds.js
 */
'use strict';
function log(s) { send({ t: 'log', m: String(s) }); }

function modName(addr) {
  const m = Process.findModuleByAddress(addr);
  return m ? (m.name + '+0x' + addr.sub(m.base).toString(16)) : addr.toString();
}

function install() {
  const nvcpl = Process.findModuleByName('NvCpl.dll') || Process.findModuleByName('nvcpl.dll');
  if (!nvcpl) { setTimeout(install, 500); return; }
  const set = nvcpl.findExportByName('NvCplApiSetSetting');
  const exec = nvcpl.findExportByName('NvCplApiExecute');
  const interesting = /nvxd|nvcpl|nvcontainer|combase|rpcrt|ole32|nvapi/i;
  const counts = {};

  function stalkOnce(target, label, pred) {
    Interceptor.attach(target, {
      onEnter(args) {
        if (!pred(args)) return;
        this.active = true;
        this.label = label;
        const tid = Process.getCurrentThreadId();
        Stalker.follow(tid, {
          events: { call: true },
          onCallSummary(summary) {
            for (const [addr, n] of Object.entries(summary)) {
              const a = ptr(addr);
              const name = modName(a);
              if (interesting.test(name)) {
                counts[name] = (counts[name] || 0) + n;
              }
            }
          }
        });
      },
      onLeave() {
        if (!this.active) return;
        Stalker.unfollow(Process.getCurrentThreadId());
        Stalker.flush();
        const top = Object.entries(counts).sort((a,b)=>b[1]-a[1]).slice(0,40);
        log('STALK ' + this.label + ' top:\n' + top.map(x => x[1] + ' ' + x[0]).join('\n'));
        for (const k of Object.keys(counts)) delete counts[k];
        this.active = false;
      }
    });
  }

  stalkOnce(set, 'SetSetting', (a) => (a[2].toInt32() >>> 0) === 0x330);
  stalkOnce(exec, 'Execute', () => true);
  log('stalk ready');
}
setImmediate(install);
