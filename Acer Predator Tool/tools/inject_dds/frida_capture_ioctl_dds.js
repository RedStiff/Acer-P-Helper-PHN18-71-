/**
 * Capture NVIDIA driver ioctls during NvCplApiExecute(DDS).
 * Focus: code 0x8801b and 0x12xxx family + handle path.
 */
'use strict';
function log(s){ send({t:'log', m:String(s)}); }

const ntdll = Process.getModuleByName('ntdll.dll');
const ntIoctl = ntdll.getExportByName('NtDeviceIoControlFile');
const ntQuery = ntdll.getExportByName('NtQueryObject');

function handlePath(h) {
  try {
    const buf = Memory.alloc(1024);
    // ObjectNameInformation = 1
    const st = new NativeFunction(ntQuery, 'int', ['pointer','int','pointer','uint32','pointer']);
    const retlen = Memory.alloc(4);
    const status = st(h, 1, buf, 1000, retlen);
    if (status !== 0) return 'status=0x' + (status>>>0).toString(16);
    // UNICODE_STRING at start: Length, MaxLength, Buffer*
    const len = buf.readU16();
    const ptr = buf.add(Process.pointerSize === 8 ? 8 : 4).readPointer();
    return ptr.readUtf16String(len/2);
  } catch (e) {
    return 'err:' + e;
  }
}

let enable = false;
let seen = 0;
Interceptor.attach(ntIoctl, {
  onEnter(args) {
    if (!enable) return;
    this.h = args[0];
    this.code = args[5].toUInt32 ? args[5].toUInt32() : (args[5].toInt32() >>> 0);
    this.inBuf = args[6];
    this.inLen = args[7].toInt32();
    this.outBuf = args[8];
    this.outLen = args[9].toInt32();
  },
  onLeave(retval) {
    if (!enable) return;
    const code = this.code >>> 0;
    // interesting codes from prior capture
    if (!(code === 0x8801b || (code & 0xffff0000) === 0x00010000 || code === 0x1208b || code === 0x12017 || code === 0x1203b || code === 0x12087 || (code >= 0x12000 && code <= 0x13000))) {
      // still keep 0x8801b and 0x12xxx
      if (code !== 0x8801b && (code < 0x12000 || code > 0x13fff)) return;
    }
    seen++;
    if (seen > 40) return;
    let path = handlePath(this.h);
    let head = '';
    try {
      if (this.inLen > 0 && !this.inBuf.isNull()) {
        const n = Math.min(this.inLen, 128);
        const bytes = [];
        for (let i = 0; i < n; i++) bytes.push(('0' + this.inBuf.add(i).readU8().toString(16)).slice(-2));
        head = bytes.join(' ');
      }
    } catch (e) { head = 'readerr'; }
    log('IOCTL 0x' + code.toString(16) + ' in=' + this.inLen + ' out=' + this.outLen + ' ret=0x' + (retval.toInt32()>>>0).toString(16) + ' path=' + path + ' head=' + head);

    // dump full 0x8801b buffers to file via send
    if (code === 0x8801b && this.inLen > 0 && this.inLen < 20000) {
      try {
        send({ t: 'buf', code: code, len: this.inLen }, this.inBuf.readByteArray(this.inLen));
      } catch (e) { log('sendbuf ' + e); }
    }
  }
});

const nvcpl = Process.findModuleByName('NvCpl.dll') || Process.findModuleByName('nvcpl.dll');
Interceptor.attach(nvcpl.findExportByName('NvCplApiExecute'), {
  onEnter() { enable = true; seen = 0; log('Execute START'); },
  onLeave(r) { log('Execute END ' + r + ' captured=' + seen); enable = false; }
});
log('ready');
