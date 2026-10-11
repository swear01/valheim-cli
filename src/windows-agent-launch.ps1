param([Parameter(Mandatory=$true)][string]$Game,[Parameter(Mandatory=$true)][string]$Profile,[Parameter(Mandatory=$true)][string]$Cli)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
try {
    $Game=(Resolve-Path -LiteralPath $Game).ProviderPath
    $Profile=(Resolve-Path -LiteralPath $Profile).ProviderPath
    if([IO.Path]::GetFileName($Game) -ine 'valheim.exe'){throw 'Choose valheim.exe'}
    $preloader=Join-Path $Profile 'BepInEx\core\BepInEx.Preloader.dll'
    if(!(Test-Path -LiteralPath $preloader -PathType Leaf)){throw 'BepInEx preloader missing'}
    $config=Get-Content -LiteralPath (Join-Path $Profile 'BepInEx\config\swear01.ValheimCliBridge.cfg') -Raw
    if($config -match '(?m)^[ \t]*Enabled\s*=\s*false\s*$' -or $config -notmatch '(?m)^[ \t]*AllowControl\s*=\s*true\s*$') {throw 'Enable the bridge and AllowControl before launching in an isolated desktop'}
    if(($Game+$preloader) -match '["\x00-\x1f]'){throw 'Invalid launch path'}
    Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
public static class ValheimAgentLaunch {
    public static string DesktopName {get;private set;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Startup {
        public int cb;public string reserved,desktop,title;public int x,y,width,height,xchars,ychars,fill,flags;
        public short show,reserved2;public IntPtr reservedPtr,input,output,error;
    }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo {public IntPtr process,thread;public uint pid,tid;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateDesktop(string name,IntPtr device,IntPtr mode,uint flags,uint access,IntPtr security);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr handle);
    [DllImport("user32.dll")] static extern IntPtr GetProcessWindowStation();
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool GetUserObjectInformation(IntPtr handle,int index,StringBuilder value,int length,out int needed);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcess(string app,StringBuilder args,IntPtr processAttributes,IntPtr threadAttributes,bool inherit,uint flags,IntPtr environment,string directory,ref Startup startup,out ProcessInfo info);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] static extern uint WaitForInputIdle(IntPtr process,uint milliseconds);
    public static uint Start(string exe,string preloader,string cli) {
        using(var gate=new Mutex(false,"Local\\ValheimCliAgentLaunch")) {
            bool entered;
            try {entered=gate.WaitOne(0);} catch(AbandonedMutexException) {entered=true;}
            if(!entered)throw new InvalidOperationException("Agent launch already in progress");
            try {
                foreach(var process in Process.GetProcessesByName("valheim")) {
                    using(process) {
                        string path;
                        try {path=process.MainModule.FileName;}
                        catch(System.ComponentModel.Win32Exception) {if(process.HasExited)continue;throw;}
                        catch(InvalidOperationException) {if(process.HasExited)continue;throw;}
                        if(!string.Equals(path,cli,StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Valheim is already running; preserve that session");
                    }
                }
                var station=new StringBuilder(256);int needed;
                if(!GetUserObjectInformation(GetProcessWindowStation(),2,station,station.Capacity * 2,out needed) || !string.Equals(station.ToString(),"WinSta0",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Run launch-agent in the signed-in Windows desktop session");
                var name="ValheimCliAgent_"+Guid.NewGuid().ToString("N");
                DesktopName=name;
                var desktop=CreateDesktop(name,IntPtr.Zero,IntPtr.Zero,0,0x83,IntPtr.Zero);
                if(desktop==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                try {
                    var startup=new Startup{cb=Marshal.SizeOf(typeof(Startup)),desktop="winsta0\\"+name};ProcessInfo info;
                    var args=new StringBuilder("\""+exe+"\" --doorstop-enabled true --doorstop-target-assembly \""+preloader+"\" --valheim-cli-agent -console -screen-fullscreen 0 -screen-width 1600 -screen-height 900 -popupwindow");
                    if(!CreateProcess(exe,args,IntPtr.Zero,IntPtr.Zero,false,0,IntPtr.Zero,System.IO.Path.GetDirectoryName(exe),ref startup,out info))
                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    try {WaitForInputIdle(info.process,30000);return info.pid;}
                    finally {CloseHandle(info.thread);CloseHandle(info.process);}
                } finally {CloseDesktop(desktop);}
            } finally {gate.ReleaseMutex();}
        }
    }
}
'@
    $gameId=[ValheimAgentLaunch]::Start($Game,$preloader,$Cli)
    @{ok=$true;state='launched';pid=$gameId;desktop=[ValheimAgentLaunch]::DesktopName;agentMode=$true;next='Poll status and ui-list; launched does not mean the bridge is ready.'}|ConvertTo-Json -Compress
} catch {
    [Console]::Error.WriteLine($_.Exception.GetBaseException().Message)
    exit 1
}
