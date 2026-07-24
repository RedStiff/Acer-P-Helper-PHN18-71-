/**
 * On SessionFilter CoCreate success (ctx 0x402), QI interfaces and keep global ref.
 * Then after UXD up, call IStateData SetSettings via SyncProxy using that filter.
 */
'use strict';
function log(s) { send({ t: 'log', m: String(s) }); }
function guid(p) {
  const b = new Uint8Array(p.readByteArray(16));
  const d1 = b[0]|b[1]<<8|b[2]<<16|b[3]<<24;
  const d2 = b[4]|b[5]<<8, d3 = b[6]|b[7]<<8;
  const h = [...b].map(x => ('0'+x.toString(16)).slice(-2)).join('');
  return ('{'+('00000000'+(d1>>>0).toString(16)).slice(-8)+'-'+
    ('0000'+d2.toString(16)).slice(-4)+'-'+('0000'+d3.toString(16)).slice(-4)+'-'+
    h.slice(16,20)+'-'+h.slice(20)+'}').toUpperCase();
}
function writeGuid(p, s) {
  const hex = s.replace(/-/g, '');
  p.writeU32(parseInt(hex.slice(0, 8), 16));
  p.add(4).writeU16(parseInt(hex.slice(8, 12), 16));
  p.add(6).writeU16(parseInt(hex.slice(12, 16), 16));
  const bytes = [];
  for (let i = 16; i < 32; i += 2) bytes.push(parseInt(hex.slice(i, i + 2), 16));
  p.add(8).writeByteArray(bytes);
}
function modOff(a) {
  const m = Process.findModuleByAddress(a);
  return m ? m.name + '+0x' + a.sub(m.base).toString(16) : String(a);
}

let filterObj = null;
let stateDataObj = null;

function qi(obj, iidStr) {
  const vt = obj.readPointer();
  const qiFn = new NativeFunction(vt.readPointer(), 'int', ['pointer', 'pointer', 'pointer']);
  const iid = Memory.alloc(16);
  writeGuid(iid, iidStr);
  const out = Memory.alloc(Process.pointerSize);
  out.writePointer(ptr(0));
  const hr = qiFn(obj, iid, out);
  return { hr: hr, ptr: out.readPointer() };
}

function install() {
  const combase = Process.findModuleByName('combase.dll');
  Interceptor.attach(combase.findExportByName('CoCreateInstance'), {
    onEnter(a) {
      this.clsid = guid(a[0]);
      this.ctx = a[2].toUInt32() >>> 0;
      this.ppv = a[4];
      this.watch = this.clsid.indexOf('3F6374C2') >= 0 || this.clsid.indexOf('DCAB0989') >= 0;
    },
    onLeave(r) {
      if (!this.watch) return;
      const hr = r.toInt32();
      let obj = ptr(0);
      try { if (hr === 0) obj = this.ppv.readPointer(); } catch (e) {}
      log('CoCreate ' + this.clsid.slice(0, 9) + ' ctx=0x' + this.ctx.toString(16) +
        ' hr=0x' + (hr >>> 0).toString(16) + ' obj=' + obj);
      if (hr !== 0 || obj.isNull()) return;

      if (this.clsid.indexOf('3F6374C2') >= 0) {
        filterObj = obj;
        // AddRef to keep
        try {
          const add = new NativeFunction(obj.readPointer().add(Process.pointerSize).readPointer(), 'uint', ['pointer']);
          add(obj);
        } catch (e) {}
        log('FILTER kept ' + obj + ' vt=' + modOff(obj.readPointer()));
        const tests = [
          '00000000-0000-0000-C000-000000000046',
          'E6AB4158-38B8-4FDF-85CF-ADC2E9870970',
          'A3116D99-0A9B-400D-851E-84B3E387DBCC',
          '3F6374C2-3540-476A-A123-D1DA2B6DDF86',
          'DC09760E-9FDA-454A-B9D2-7E663E58C39D',
          '4473E3A7-C2AD-4075-A1F8-935A584740A9',
        ];
        for (const t of tests) {
          try {
            const rqi = qi(obj, t);
            log('  QI ' + t.slice(0, 8) + ' hr=0x' + (rqi.hr >>> 0).toString(16) + ' p=' + rqi.ptr);
            if (rqi.hr === 0 && !rqi.ptr.isNull() && t.indexOf('E6AB4158') >= 0)
              stateDataObj = rqi.ptr;
          } catch (e) { log('  QI ex ' + e); }
        }
      }
    }
  });

  // Capture live IStateData this from SetSettings
  Interceptor.attach(combase.findExportByName('ObjectStublessClient5'), {
    onEnter(args) {
      if (!filterObj) return;
      // if a2 matches filter or any SetSettings during armed window
      stateDataObj = args[0];
      log('live IStateData this=' + args[0] + ' a2=' + args[1] + '/' + args[2]);
    }
  });

  rpc.exports = {
    status() {
      return { filter: filterObj ? filterObj.toString() : null, sd: stateDataObj ? stateDataObj.toString() : null };
    },
    trySet(mux, automatic) {
      if (!stateDataObj) return 'no statedata';
      if (!filterObj) return 'no filter';
      const HANDLE = 'AFE3D677-141F-424B-808D-340D9EC4ACD6';
      const arena = Memory.alloc(0x800);
      const items = arena.add(0x40);
      const dataMux = arena.add(0x100);
      const dataAuto = arena.add(0x140);
      dataMux.writeU32(3); dataMux.add(4).writeU32(4); dataMux.add(8).writeU32(mux);
      dataAuto.writeU32(5); dataAuto.add(4).writeU32(1); dataAuto.add(8).writeU32(automatic);
      writeGuid(items, HANDLE);
      items.add(16).writeU16(1); items.add(18).writeU16(0x7d);
      items.add(20).writeU32(4); items.add(24).writePointer(dataMux);
      writeGuid(items.add(0x20), HANDLE);
      items.add(0x20 + 16).writeU16(3); items.add(0x20 + 18).writeU16(0x7d);
      items.add(0x20 + 20).writeU32(4); items.add(0x20 + 24).writePointer(dataAuto);
      arena.writePointer(items); arena.add(8).writeU64(2);

      const vt = stateDataObj.readPointer();
      const set = new NativeFunction(vt.add(5 * Process.pointerSize).readPointer(), 'int', ['pointer', 'pointer', 'pointer']);
      const hr = set(stateDataObj, arena, filterObj);
      log('manual SetSettings hr=0x' + (hr >>> 0).toString(16));

      // DoOperation
      const op = arena.add(0x400);
      writeGuid(op, 'D812F4FF-2E38-4AFB-BEC9-DA365AB6ECDD');
      op.add(16).writeU16(9); op.add(18).writeU16(2);
      const dop = new NativeFunction(vt.add(6 * Process.pointerSize).readPointer(), 'int', ['pointer', 'pointer', 'pointer']);
      const hr2 = dop(stateDataObj, op, filterObj);
      log('manual DoOperation hr=0x' + (hr2 >>> 0).toString(16));
      return { set: hr, dop: hr2 };
    }
  };
  log('ready');
}
setImmediate(install);
