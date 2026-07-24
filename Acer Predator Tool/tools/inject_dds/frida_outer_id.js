/**
 * Identify App SessionFilter outer: recent CoCreates + cache object dump.
 */
'use strict';

function log(s) { send({ t: 'log', m: String(s) }); }
function modOff(a) {
  const m = Process.findModuleByAddress(a);
  return m ? m.name + '+0x' + a.sub(m.base).toString(16) : String(a);
}
function guid(p) {
  try {
    const b = new Uint8Array(p.readByteArray(16));
    const d1 = (b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24)) >>> 0;
    const d2 = b[4] | (b[5] << 8);
    const d3 = b[6] | (b[7] << 8);
    const h = [...b].map(x => ('0' + x.toString(16)).slice(-2)).join('');
    return ('{' + ('00000000' + d1.toString(16)).slice(-8) + '-' +
      ('0000' + d2.toString(16)).slice(-4) + '-' +
      ('0000' + d3.toString(16)).slice(-4) + '-' +
      h.slice(16, 20) + '-' + h.slice(20) + '}').toUpperCase();
  } catch (e) { return '?'; }
}

const FILTER = '3F6374C2';
const recent = [];
const objMap = {};

function describe(p, tag) {
  if (!p || p.isNull()) { log(tag + ' null'); return; }
  try {
    const vt = p.readPointer();
    log(tag + ' ' + p + ' vt=' + modOff(vt));
    log(tag + ' dump:\n' + hexdump(p, { length: 0x80 }));
    const qi = new NativeFunction(vt.readPointer(), 'int', ['pointer', 'pointer', 'pointer']);
    function tryQi(bytes, name) {
      const iid = Memory.alloc(16); iid.writeByteArray(bytes);
      const out = Memory.alloc(8); out.writePointer(ptr(0));
      const hr = qi(p, iid, out) >>> 0;
      const o = out.readPointer();
      log(tag + ' QI ' + name + ' hr=0x' + hr.toString(16) + ' -> ' + o +
        (o.isNull() ? '' : ' vt=' + modOff(o.readPointer())));
      return o;
    }
    tryQi([0,0,0,0,0,0,0,0,0xc0,0,0,0,0,0,0,0x46], 'IUnknown');
    const old = tryQi([0x51,0x79,0x7d,0x62,0x43,0x96,0xe6,0x4d,0x89,0x8f,0x6c,0x6b,0x76,0x6a,0xab,0x39], 'OLD');
    tryQi([0x58,0xb4,0xab,0xe6,0xb8,0x38,0xdf,0x4f,0x85,0xcf,0xad,0xc2,0xe9,0x87,0x09,0x70], 'NEW');
    tryQi([0x1b,0,0,0,0,0,0,0,0xc0,0,0,0,0,0,0,0x46], 'ICallFactory');
    tryQi([0,0,0,0,0,0,0,0,0xc0,0,0,0,0,0,0,0x46], 'IUnknown2');
    // IClientSecurity / IMultiQI
    tryQi([0x09,0x02,0,0,0,0,0,0,0xc0,0,0,0,0,0,0,0x46], 'IMarshal');
    tryQi([0x18,0,0,0,0,0,0,0,0xc0,0,0,0,0,0,0,0x46], 'IMultiQI');
    if (!old.isNull()) {
      log(tag + ' OLD dump:\n' + hexdump(old, { length: 0x60 }));
    }
  } catch (e) {
    log(tag + ' fail ' + e);
  }
}

function hookCom() {
  const combase = Process.findModuleByName('combase.dll');
  Interceptor.attach(combase.findExportByName('CoCreateInstance'), {
    onEnter(a) {
      this.clsid = guid(a[0]);
      this.iid = guid(a[3]);
      this.outer = a[1];
      this.ctx = a[2].toUInt32() >>> 0;
      this.ppv = a[4];
      this.isFilter = this.clsid.indexOf(FILTER) >= 0;
    },
    onLeave(r) {
      if (r.toInt32() !== 0) return;
      const obj = this.ppv.readPointer();
      const rec = {
        clsid: this.clsid, iid: this.iid, ctx: this.ctx,
        outer: this.outer.toString(), obj: obj.toString()
      };
      recent.push(rec);
      if (recent.length > 40) recent.shift();
      objMap[obj.toString()] = rec;
      if (!this.isFilter) return;

      log('=== FILTER CREATE ===');
      log('clsid=' + this.clsid + ' iid=' + this.iid + ' ctx=0x' + this.ctx.toString(16));
      log('outer=' + this.outer + ' obj=' + obj);
      log('--- recent CoCreates ---');
      for (const x of recent) {
        log('  ' + x.clsid + ' iid=' + x.iid + ' ctx=0x' + x.ctx.toString(16) +
          ' outer=' + x.outer + ' obj=' + x.obj);
      }
      // match outer to a previous create
      let match = objMap[this.outer.toString()];
      if (!match) {
        // try IUnknown identity: QI outer to IUnknown and match
        try {
          const vt = this.outer.readPointer();
          const qi = new NativeFunction(vt.readPointer(), 'int', ['pointer', 'pointer', 'pointer']);
          const iid = Memory.alloc(16);
          iid.writeByteArray([0,0,0,0,0,0,0,0,0xc0,0,0,0,0,0,0,0x46]);
          const out = Memory.alloc(8); out.writePointer(ptr(0));
          qi(this.outer, iid, out);
          const unk = out.readPointer();
          log('outer IUnknown=' + unk);
          match = objMap[unk.toString()];
          // scan map for nearby / same
          for (const k of Object.keys(objMap)) {
            if (ptr(k).equals(unk) || ptr(k).equals(this.outer)) {
              match = objMap[k];
              break;
            }
          }
        } catch (e) { log('outer match fail ' + e); }
      }
      if (match) log('OUTER MATCH create ' + JSON.stringify(match));
      else log('OUTER MATCH none');

      describe(this.outer, 'OUTER');
      describe(obj, 'FILTER');
      const cache = obj.add(0x58).readPointer();
      log('cache ptr=' + cache);
      describe(cache, 'CACHE');
      // also dump filter+0x20 controlling unknown field
      try {
        log('filter+0x20=' + obj.add(0x20).readPointer());
        log('filter+0x28=' + obj.add(0x28).readPointer());
        log('filter+0x30=' + obj.add(0x30).readPointer());
        log('filter+0x38=' + obj.add(0x38).readPointer());
        log('filter+0x40=' + obj.add(0x40).readPointer());
        log('filter+0x48=' + obj.add(0x48).readPointer());
        log('filter+0x50=' + obj.add(0x50).readPointer());
        log('filter+0x58=' + obj.add(0x58).readPointer());
      } catch (e) {}
    }
  });

  // Also CoCreateInstanceEx
  const ex = combase.findExportByName('CoCreateInstanceEx');
  if (ex) Interceptor.attach(ex, {
    onEnter(a) {
      this.clsid = guid(a[0]);
      this.outer = a[1];
      this.ctx = a[2].toUInt32() >>> 0;
    },
    onLeave(r) {
      if (r.toInt32() !== 0) return;
      log('CCIEX ' + this.clsid + ' ctx=0x' + this.ctx.toString(16) + ' outer=' + this.outer);
    }
  });
}

setImmediate(() => {
  hookCom();
  log('READY');
});
