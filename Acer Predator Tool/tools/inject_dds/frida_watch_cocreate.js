/**
 * Watch CoCreate for SessionFilter / related during DDS.
 */
'use strict';
function log(s) { send({ t: 'log', m: String(s) }); }
function guid(p) {
  try {
    const b = new Uint8Array(p.readByteArray(16));
    const d1 = b[0]|b[1]<<8|b[2]<<16|b[3]<<24;
    const d2 = b[4]|b[5]<<8, d3 = b[6]|b[7]<<8;
    const h = [...b].map(x => ('0'+x.toString(16)).slice(-2)).join('');
    return ('{'+('00000000'+(d1>>>0).toString(16)).slice(-8)+'-'+
      ('0000'+d2.toString(16)).slice(-4)+'-'+('0000'+d3.toString(16)).slice(-4)+'-'+
      h.slice(16,20)+'-'+h.slice(20)+'}').toUpperCase();
  } catch (e) { return '?'; }
}
function install() {
  const combase = Process.findModuleByName('combase.dll');
  const interesting = [
    '3F6374C2', '5387A36B', 'DCAB0989', '1DC715B2', '33C89616', '4FC7F090'
  ];
  Interceptor.attach(combase.findExportByName('CoCreateInstance'), {
    onEnter(a) {
      this.clsid = guid(a[0]);
      this.iid = guid(a[3]);
      this.hit = interesting.some(x => this.clsid.indexOf(x) >= 0 || this.iid.indexOf(x) >= 0);
    },
    onLeave(r) {
      if (this.hit) log('CoCreate clsid=' + this.clsid + ' iid=' + this.iid + ' hr=' + r);
    }
  });
  // Also DllGetClassObject on nvxdbat
  const bat = Process.findModuleByName('nvxdbat.dll') || Process.findModuleByName('NVXDBat.dll');
  if (bat) {
    const gco = bat.findExportByName('DllGetClassObject');
    if (gco) Interceptor.attach(gco, {
      onEnter(a) { this.clsid = guid(a[0]); },
      onLeave(r) { log('DllGCO ' + this.clsid + ' hr=' + r); }
    });
  }
  log('watch ready');
}
setImmediate(install);
