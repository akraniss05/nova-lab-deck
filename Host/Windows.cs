using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NovaLab;
public record Target(string Id,string Name,string Type,string Path,string Args="",string ProcessName="",string Icon="");
public sealed class LocalConfig {
    public string ObsSecret{get;set;}="";public int ObsPort{get;set;}=4455;
    public string Output{get;set;}="";public List<Target> Custom{get;set;}=[];public List<string> Approved{get;set;}=[];
    public string Address{get;set;}="127.0.0.1";public int Port{get;set;}=8785;public string Certificate{get;set;}="";public string CertSecret{get;set;}="";
    public static string Protect(string s)=>s.Length==0?"":Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s),null,DataProtectionScope.CurrentUser));
    public static string Reveal(string s)=>s.Length==0?"":Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s),null,DataProtectionScope.CurrentUser));
}
public sealed class WindowsEngine : IPlatform {
    readonly string dir;public LocalConfig Config;readonly object gate=new();readonly Mixer mixer;readonly Media media=new();readonly SoundEngine sounds=new();
    readonly Monitoring monitor=new();Target[] found=[]; readonly Func<string,Task<bool>> confirm;readonly Func<string,Task<string?>> approve;
    public WindowsEngine(string path,Func<string,Task<bool>> check,Func<string,Task<string?>> pair){
        dir=path;Directory.CreateDirectory(dir);confirm=check;approve=pair;var f=Path.Combine(dir,"host.json");
        Config=File.Exists(f)?JsonSerializer.Deserialize<LocalConfig>(File.ReadAllText(f))??new():new();mixer=new(dir);
    }
    public void Save(){lock(gate){var f=Path.Combine(dir,"host.json");File.WriteAllText(f+".tmp",JsonSerializer.Serialize(Config));File.Move(f+".tmp",f,true);}}
    public void Add(Target target){lock(gate){Config.Custom.Add(target);Config.Approved.Add(target.Id);Save();}}
    static string Stable(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..24];
    static string Exe(string path){try{if(Path.GetExtension(path).Equals(".lnk",StringComparison.OrdinalIgnoreCase)){dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;try{dynamic l=shell.CreateShortcut(path);try{return (string)l.TargetPath;}finally{Marshal.FinalReleaseComObject(l);}}finally{Marshal.FinalReleaseComObject(shell);}}}catch{}return path;}
    static string Extract(string path){try{using var ico=Icon.ExtractAssociatedIcon(path);if(ico==null)return "";using var img=ico.ToBitmap();using var ms=new MemoryStream();img.Save(ms,System.Drawing.Imaging.ImageFormat.Png);return "data:image/png;base64,"+Convert.ToBase64String(ms.ToArray());}catch{return "";}}
    public void Scan(){
        var all=new List<Target>();var options=new EnumerationOptions{RecurseSubdirectories=true,IgnoreInaccessible=true,AttributesToSkip=FileAttributes.ReparsePoint};
        foreach(var folder in new[]{Environment.SpecialFolder.Programs,Environment.SpecialFolder.CommonPrograms}){
            var dir=Environment.GetFolderPath(folder);if(!Directory.Exists(dir))continue;
            foreach(var file in Directory.EnumerateFiles(dir,"*",options).Where(f=>Path.GetExtension(f).ToLowerInvariant() is ".lnk" or ".url")){
                var exe=Exe(file);all.Add(new(Stable(file),Path.GetFileNameWithoutExtension(file),"app",file,"",Path.GetExtension(exe).Equals(".exe",StringComparison.OrdinalIgnoreCase)?Path.GetFileNameWithoutExtension(exe):"",Extract(exe)));}
        }
        try{dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;try{dynamic folder=shell.NameSpace("shell:AppsFolder");try{dynamic entries=folder.Items();try{foreach(dynamic item in entries){try{string id=item.Path;string name=item.Name;if(id.Contains('!')&&!id.Contains('"'))all.Add(new(Stable(id),name,"store",id));}finally{Marshal.FinalReleaseComObject(item);}}}finally{Marshal.FinalReleaseComObject(entries);}}finally{Marshal.FinalReleaseComObject(folder);}}finally{Marshal.FinalReleaseComObject(shell);}}catch(COMException){}
        var steam=Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam","SteamPath",null) as string;
        if(steam!=null){var roots=new List<string>{steam};var vdf=Path.Combine(steam,"steamapps","libraryfolders.vdf");
            if(File.Exists(vdf))foreach(Match m in Regex.Matches(File.ReadAllText(vdf),""" "path"\s+"([^"]+)" """.Trim()))roots.Add(m.Groups[1].Value.Replace(@"\\",@"\"));
            foreach(var root in roots.Distinct()){var apps=Path.Combine(root,"steamapps");if(!Directory.Exists(apps))continue;foreach(var f in Directory.EnumerateFiles(apps,"appmanifest_*.acf")){
                var id=Path.GetFileNameWithoutExtension(f)[12..];if(!uint.TryParse(id,out _))continue;var name=Regex.Match(File.ReadAllText(f),""" "name"\s+"([^"]+)" """.Trim()).Groups[1].Value;if(name.Length>0)all.Add(new(Stable("steam"+id),name,"steam","steam://rungameid/"+id));}}}
        lock(gate)found=all.OrderBy(x=>x.Name).ToArray();
    }
    public IReadOnlyList<CatalogItem> Catalog(){lock(gate)return found.Concat(Config.Custom).Select(t=>new CatalogItem(t.Id,t.Name,t.Type,t.Icon)).ToArray();}
    Target Find(string id){lock(gate){var all=Config.Custom.Concat(found);
        var t=id.StartsWith("find:")?all.FirstOrDefault(t=>t.Name.Equals(id[5..],StringComparison.OrdinalIgnoreCase))??all.FirstOrDefault(t=>t.Name.Contains(id[5..],StringComparison.OrdinalIgnoreCase)):all.FirstOrDefault(t=>t.Id==id);
        return t??throw new InvalidOperationException("Programm oder Freigabe fehlt · "+id);}}
    static Process? Window(Target t){if(t.ProcessName=="")return null;Process? match=null;foreach(var p in Process.GetProcessesByName(t.ProcessName)){try{if(match==null&&p.MainWindowHandle!=IntPtr.Zero){match=p;continue;}}catch{}p.Dispose();}return match;}
    public Task<bool> Confirm(string m)=>confirm(m);public Task<string?> Approve(string n)=>approve(n);
    public async Task<PlatformState> Read(CancellationToken ct){
        object a,m,sys;try{a=mixer.Read();}catch(Exception e){a=new{error=e.Message};}
        try{m=await media.Read().WaitAsync(TimeSpan.FromSeconds(3),ct);}catch(Exception e){m=new{error=e.Message};}
        try{sys=await monitor.Read(ct);}catch(Exception e){sys=new{error=e.Message};}return new(a,m,sys,Native.Foreground());
    }
    public async Task<string> Run(Step s,CancellationToken ct){
        if(s.Kind.StartsWith("audio."))return mixer.Change(s);if(s.Kind.StartsWith("media."))return await media.Run(s).WaitAsync(TimeSpan.FromSeconds(5),ct);
        if(s.Kind=="sound.stopAll"){sounds.Stop(null);return "Sounds werden gestoppt";}
        if(s.Kind=="system.lock"){if(!Native.LockWorkStation())throw new InvalidOperationException("Windows lehnt Sperren ab");return "Sperren angefordert";}
        if(s.Kind is "system.restart" or "system.shutdown"){Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"shutdown.exe"),s.Kind=="system.restart"?"/r /t 30":"/s /t 30"){UseShellExecute=false,CreateNoWindow=true})?.Dispose();return "Windows-Abschaltung in 30 Sekunden angefordert";}
        var t=Find(s.Target);
        if(s.Kind=="hotkey"){Model.Require(t.Type=="hotkey","Lokale Hotkey-Freigabe erforderlich");return await Native.Shortcut(t,ct);}
        if(s.Kind.StartsWith("sound")){Model.Require(t.Type=="sound","Sound fehlt");if(s.Kind=="sound.stop"){sounds.Stop(t.Id);return "Sound wird gestoppt";}
            var options=s.Value.Split(',');int Number(int i,int fallback,int max)=>options.Length>i&&int.TryParse(options[i],out var n)?Math.Clamp(n,0,max):fallback;
            sounds.Play(t,Number(0,80,100),Number(1,0,1)==1,Number(2,0,10000),Number(3,0,10000),Config.Output);return "Wiedergabe gestartet";}
        Model.Require(t.Type is "app" or "store" or "steam" or "file" or "folder" or "web","Unzulässiges Startziel");
        if(s.Kind=="launch"){
            bool trusted;lock(gate)trusted=Config.Approved.Contains(t.Id);
            if(!trusted){if(!await confirm("Dauerhaft als Startziel freigeben?\n"+t.Name+"\n"+t.Path).WaitAsync(ct))throw new UnauthorizedAccessException();lock(gate){Config.Approved.Add(t.Id);Save();}}
            using var proc=Window(t);if(proc!=null && Native.Focus(proc.MainWindowHandle))return "Fenster aktiviert";
            var psi=t.Type=="store"?new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),"shell:AppsFolder\\"+t.Path):new ProcessStartInfo(t.Path,t.Args);
            psi.UseShellExecute=true;Process.Start(psi)?.Dispose();return "Start an Windows übergeben · "+t.Name;
        }
        using var w=Window(t)??throw new InvalidOperationException("Kein zugeordnetes Programmfenster");var h=w.MainWindowHandle;
        switch(s.Kind){case "focus":if(!Native.Focus(h))throw new InvalidOperationException("Windows verweigert Fokuswechsel");break;case "minimize":Native.ShowWindowAsync(h,6);break;case "maximize":Native.ShowWindowAsync(h,3);break;case "close":if(!w.CloseMainWindow())throw new InvalidOperationException("Schließen abgelehnt");break;default:throw new ArgumentException("Aktion unbekannt");}
        return "Fensteraktion angefordert · "+t.Name;
    }
    public void Dispose()=>sounds.Dispose();
}
static class Native {
    [DllImport("user32.dll")]public static extern bool LockWorkStation();
    [DllImport("user32.dll")]public static extern bool ShowWindowAsync(IntPtr h,int cmd);
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    public static string Foreground(){try{GetWindowThreadProcessId(GetForegroundWindow(),out var pid);using var p=Process.GetProcessById((int)pid);return p.ProcessName;}catch{return "";}}
    public static bool Focus(IntPtr h){ShowWindowAsync(h,9);return SetForegroundWindow(h);}
    [StructLayout(LayoutKind.Sequential)]struct KeyInput{public ushort vk,scan;public uint flags,time;public UIntPtr extra;}
    [StructLayout(LayoutKind.Explicit,Size=32)]struct Union{[FieldOffset(0)]public KeyInput key;}
    [StructLayout(LayoutKind.Sequential)]struct Input{public uint type;public Union data;}
    [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint n,Input[] input,int size);
    static void Press(ushort key,bool up){var input=new Input{type=1,data=new Union{key=new KeyInput{vk=key,flags=up?2u:0}}};if(SendInput(1,[input],Marshal.SizeOf<Input>())!=1)throw new InvalidOperationException("Windows hat die Eingabe abgelehnt (Fensterrechte prüfen)");}
    public static async Task<string> Shortcut(Target t,CancellationToken ct){
        if(t.ProcessName.Length>0&&!Foreground().Equals(t.ProcessName,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Zielprogramm zuerst in den Vordergrund holen");
        var keys=t.Path.Split('+').Select(v=>(ushort)int.Parse(v)).ToArray();var held=new Stack<ushort>();
        try{foreach(var k in keys){Press(k,false);held.Push(k);}await Task.Delay(Math.Clamp(int.Parse(t.Args),35,5000),ct);}finally{while(held.TryPop(out var k))Press(k,true);}
        return "Tastenkürzel gesendet · Programmzustand unbekannt";
    }
}
