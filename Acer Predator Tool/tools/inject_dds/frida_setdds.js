// Attach to running NVIDIA App browser process (has NvCplDisplayPlugin.dll).
// Find GXP/command registration for SetDDSState and attempt invoke; also try
// to force-enable debugging port via DevToolsActivePort write + existing APIs.
'use strict';

function ascii(p, n) {
  try { return p.readUtf8String(n); } catch (_) { return null; }
}

function findModule(name) {
  return Process.findModuleByName(name);
}

function scanSetDDSHandlers() {
  const plug = findModule('NvCplDisplayPlugin.dll');
  if (!plug) {
    send({ t: 'no_plugin' });
    return;
  }
  send({ t: 'plugin', base: plug.base.toString(), size: plug.size });

  // Locate "SetDDSState" ASCII in plugin
  const matches = Memory.scanSync(plug.base, plug.size, '53 65 74 44 44 53 53 74 61 74 65 00'); // SetDDSState\0
  send({ t: 'setdds_strings', count: matches.length, addrs: matches.slice(0, 8).map(m => m.address.toString()) });

  // For each string, find pointer refs in plugin (qword == abs addr)
  const handlers = [];
  for (const m of matches.slice(0, 5)) {
    const abs = m.address;
    // scan data sections for pointer to this string
    const ptrPat = abs.toMatchPattern(); // not available
    // manual scan .rdata-ish: whole module for qword
    const step = Process.pointerSize;
    for (let off = 0; off + step < plug.size; off += step) {
      try {
        const q = plug.base.add(off).readPointer();
        if (q.equals(abs)) {
          // possible table entry: [ptr to name][ptr to handler] or [handler][name]
          const around = [];
          for (let k = -4; k <= 4; k++) {
            try {
              around.push(plug.base.add(off + k * step).readPointer().toString());
            } catch (_) { around.push('?'); }
          }
          handlers.push({ at: plug.base.add(off).toString(), around: around });
          if (handlers.length > 20) break;
        }
      } catch (_) {}
    }
  }
  send({ t: 'ptr_refs', handlers: handlers.slice(0, 15) });
}

function tryCefQueryViaRendererNote() {
  send({ t: 'hint', msg: 'Use companion CDP once port open' });
}

scanSetDDSHandlers();
tryCefQueryViaRendererNote();
