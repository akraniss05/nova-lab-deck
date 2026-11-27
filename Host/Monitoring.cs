using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace NovaLab;
public sealed class Monitoring {
    [DllImport("kernel32.dll")]static extern bool GetSystemTimes(out long idle,out long kernel,out long user);
    [StructLayout(LayoutKind.Sequential)]struct Memory{public uint length,load;public ulong total,free,page,pageFree,virt,virtFree,extra;}
    [DllImport("kernel32.dll")]static extern bool GlobalMemoryStatusEx(ref Memory status);
    long prevIdle,prevTicks,received,sent,netAt,lastGpu;object? gpu;
    public async Task<object> Read(CancellationToken ct){
        double? cpu=null;if(GetSystemTimes(out var idle,out var kernel,out var user)){var sum=kernel+user;var delta=sum-prevTicks;if(prevTicks>0&&delta>0)cpu=Math.Round(Math.Clamp(100.0*(delta-idle+prevIdle)/delta,0,100),1);prevTicks=sum;prevIdle=idle;}
        var mem=new Memory{length=(uint)Marshal.SizeOf<Memory>()};bool hasMem=GlobalMemoryStatusEx(ref mem);var now=Environment.TickCount64;
        if(now-lastGpu>=8000||lastGpu==0){lastGpu=now;gpu=await Nvidia(ct);}
        long rx=0,tx=0;foreach(var n in NetworkInterface.GetAllNetworkInterfaces().Where(x=>x.OperationalStatus==OperationalStatus.Up&&x.NetworkInterfaceType!=NetworkInterfaceType.Loopback)){var stats=n.GetIPStatistics();rx+=stats.BytesReceived;tx+=stats.BytesSent;}
        var elapsed=(now-netAt)/1000.0;double? down=netAt==0||elapsed<=0?null:Math.Max(0,(rx-received)/elapsed);double? up=netAt==0||elapsed<=0?null:Math.Max(0,(tx-sent)/elapsed);received=rx;sent=tx;netAt=now;
        var processes=new List<(string Name,long Bytes)>();foreach(var p in Process.GetProcesses()){using(p){try{processes.Add((p.ProcessName,p.WorkingSet64));}catch{}}}
        var disks=new List<object>();foreach(var d in DriveInfo.GetDrives().Where(d=>d.DriveType==DriveType.Fixed&&d.IsReady))disks.Add(new{name=d.Name,total=d.TotalSize,free=d.AvailableFreeSpace});
        return new{cpu,ram=hasMem?(long?)mem.load:null,ramTotal=hasMem?(ulong?)mem.total:null,ramUsed=hasMem?(ulong?)(mem.total-mem.free):null,gpu,up,down,disks,uptime=now/1000,processes=processes.OrderByDescending(p=>p.Bytes).Take(25).Select(p=>new{name=p.Name,bytes=p.Bytes}).ToArray()};
    }
    static async Task<object?> Nvidia(CancellationToken ct){
        var path=new[]{Path.Combine(Environment.SystemDirectory,"nvidia-smi.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"NVIDIA Corporation","NVSMI","nvidia-smi.exe")}.FirstOrDefault(File.Exists);
        if(path==null)return null;using var p=Process.Start(new ProcessStartInfo(path,"--query-gpu=name,utilization.gpu,memory.used,memory.total,temperature.gpu --format=csv,noheader,nounits"){UseShellExecute=false,RedirectStandardOutput=true,CreateNoWindow=true});
        if(p==null)return null;using var limit=CancellationTokenSource.CreateLinkedTokenSource(ct);limit.CancelAfter(2500);
        try{var line=await p.StandardOutput.ReadLineAsync(limit.Token);await p.WaitForExitAsync(limit.Token);if(p.ExitCode!=0||line==null)return null;var parts=line.Split(',').Select(x=>x.Trim()).ToArray();if(parts.Length!=5)return null;
            int? N(int i)=>int.TryParse(parts[i],out var n)?n:null;return new{name=parts[0],load=N(1),used=N(2),total=N(3),temp=N(4)};
        }catch(OperationCanceledException){try{p.Kill();}catch{}return null;}
    }
}
