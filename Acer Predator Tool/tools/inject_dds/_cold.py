import frida, time, subprocess, winreg
# find nvcpl host by trying attach + script that checks module
device=frida.get_local_device()
host=None
for p in device.enumerate_processes():
    if p.name!='NVIDIA App.exe': continue
    try:
        s=device.attach(p.pid)
        sc=s.create_script("rpc.exports={has:function(){return !!(Process.findModuleByName('NvCpl.dll')||Process.findModuleByName('nvcpl.dll'));}};")
        sc.load()
        if sc.exports_sync.has():
            host=p.pid; print('host', host); 
            # reload cold hooks
            js=open(r'E:\Projects\Acer Predator Tool\Acer Predator Tool\tools\inject_dds\frida_cold_uxd.js',encoding='utf-8').read()
            sc2=s.create_script(js)
            sc2.on('message', lambda m,d: print(m.get('payload',m)))
            sc2.load(); time.sleep(0.5)
            subprocess.run([r'E:\Projects\Acer Predator Tool\Acer Predator Tool\tools\inject_dds\inject_native_dds.exe','dgpu'])
            time.sleep(2)
            s.detach()
            break
        s.detach()
    except Exception as e:
        print('skip', p.pid, e)
print('done ACE', {n: winreg.QueryValueEx(winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r'SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE'),n)[0] for n in ('InternalMuxState','InternalMuxIsAutomaticMode','ACESwitchedI2D')})
