'use strict';
function log(s){ send({t:'log', m:String(s)}); }
const k32=Process.getModuleByName('kernel32.dll');
const ole=Process.findModuleByName('ole32.dll')||Process.findModuleByName('combase.dll');
['OpenEventW','CreateEventW','CreateFileW','CreateFileA','WaitForSingleObject'].forEach(n=>{
  const p=k32.findExportByName(n); if(!p) return;
  Interceptor.attach(p,{
    onEnter(a){
      try{
        let s=null;
        if(n.indexOf('W')>=0 && (n.startsWith('Open')||n.startsWith('CreateE')||n.startsWith('CreateF'))) s=a[0].readUtf16String();
        if(n.startsWith('CreateFileA')) s=a[0].readAnsiString();
        if(s && /UXD|Nv|Mux|Message|DDS|Pipe|Sync|Container|nv/i.test(s)) log(n+' '+s);
      }catch(e){}
    }
  });
});
if(ole){
  const cci=ole.findExportByName('CoCreateInstance');
  if(cci) Interceptor.attach(cci,{
    onEnter(a){
      try{
        const g=a[0].readByteArray(16);
        const b=new Uint8Array(g);
        const hex=[...b].map(x=>('0'+x.toString(16)).slice(-2)).join('');
        log('CoCreateInstance '+hex);
      }catch(e){}
    },
    onLeave(r){ log('CoCreateInstance HR='+r); }
  });
}
log('cold hooks installed');
