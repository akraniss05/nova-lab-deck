using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NovaLab;

public record Device(string Id,string Name,string Role,string Hash,long Until=0);
public record Msg(string Op, JsonElement Data);
public sealed class Server : IAsyncDisposable {
    public BoardStore Store {get;}
    public string BaseUrl{get;}
    readonly string dir;
    readonly IPlatform platform;
    readonly Obs obs;
    readonly WebApplication web;
    static readonly Dictionary<string,string> Resources=typeof(Server).Assembly.GetManifestResourceNames()
        .ToDictionary(name=>name.Replace('\\','/'),name=>name,StringComparer.Ordinal);
    readonly CancellationTokenSource end=new();
    readonly ConcurrentDictionary<string,Device> devices=new();
    readonly object security=new();
    readonly Dictionary<string,Queue<long>> rates=new();
    readonly ConcurrentDictionary<Guid,Peer> peers=new();
    readonly ConcurrentQueue<string> log=new();
    readonly ConcurrentDictionary<string,CancellationTokenSource> jobs=new();
    readonly SemaphoreSlim approval=new(1),operations=new(1);
    readonly Device admin=new("host","Windows Host","host","");
    string bootstrap="",pairCode="";
    DateTime expires,bootExpires;
    Task[] loops=[];
    volatile PlatformState? latest;
    public static string Secret()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    static string Hash(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    public IReadOnlyList<Device> Devices=>devices.Values.Where(x=>x.Role!="host").ToList();
    public static List<IPAddress> Addresses()=>NetworkInterface.GetAllNetworkInterfaces()
        .Where(n=>n.OperationalStatus==OperationalStatus.Up).SelectMany(n=>n.GetIPProperties().UnicastAddresses).Select(a=>a.Address)
        .Where(a=>a.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork && Private(a)).Distinct().ToList();
    static bool Private(IPAddress ip){if(ip.IsIPv4MappedToIPv6)ip=ip.MapToIPv4();if(IPAddress.IsLoopback(ip))return true;var a=ip.GetAddressBytes();return a.Length==4&&(a[0]==10||a[0]==192&&a[1]==168||a[0]==172&&a[1]>=16&&a[1]<=31);}
    public Server(string path,IPlatform engine,Obs obsClient,int port,IPAddress address,X509Certificate2? cert=null){
        dir=path;platform=engine;obs=obsClient;Store=new(path);BaseUrl=$"{(cert==null?"http":"https")}://{address}:{port}";
        var f=Path.Combine(dir,"devices.json");if(File.Exists(f))foreach(var d in JsonSerializer.Deserialize<Device[]>(File.ReadAllText(f))??[])if((d.Role is "edit" or "control")&&d.Until>DateTimeOffset.UtcNow.ToUnixTimeSeconds())devices[d.Hash]=d;
        var b=WebApplication.CreateSlimBuilder();b.Logging.ClearProviders();b.WebHost.ConfigureKestrel(k=>{k.Limits.MaxRequestBodySize=8_500_000;k.Limits.MaxConcurrentConnections=64;k.AddServerHeader=false;
            void Bind(IPAddress a)=>k.Listen(a,port,o=>{if(cert!=null)o.UseHttps(cert);});Bind(IPAddress.Loopback);if(!IPAddress.IsLoopback(address)){Model.Require(Private(address),"Private LAN-IP erforderlich");Bind(address);}});
        web=b.Build();web.UseWebSockets(new WebSocketOptions{KeepAliveInterval=TimeSpan.FromSeconds(20)});
        web.Use(async(ctx,next)=>{
            ctx.Response.Headers["X-Content-Type-Options"]="nosniff";ctx.Response.Headers["Referrer-Policy"]="no-referrer";
            ctx.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; object-src 'none'; base-uri 'none'; form-action 'self'";
            var h=ctx.Request.Host;var isLocal=h.Host=="127.0.0.1"||h.Host=="localhost"||h.Host==address.ToString();
            if(!isLocal||h.Port!=port||ctx.Connection.RemoteIpAddress is not {} ip||!Private(ip)){ctx.Response.StatusCode=403;return;}
            bool command=ctx.Request.Method!="GET"||ctx.Request.Path=="/ws";
            if(command&&(ctx.Request.Headers.Origin.ToString()!=$"{ctx.Request.Scheme}://{h}"||ctx.Request.Method=="POST"&&!ctx.Request.HasJsonContentType())){ctx.Response.StatusCode=403;return;}
            try{await next();}catch(Exception ex) when(!ctx.Response.HasStarted){ctx.Response.StatusCode=ex is UnauthorizedAccessException?403:ex is InvalidOperationException?409:400;await ctx.Response.WriteAsJsonAsync(new{error=Error(ex)});}
        });
        web.MapPost("/pair",(Delegate)Pair);
        web.MapPost("/cmd",async(HttpContext c)=>{var d=Auth(c);Limit(d.Id,180,10);var m=await c.Request.ReadFromJsonAsync<Msg>()??throw new ArgumentException();return Results.Json(await Dispatch(d,m,c.RequestAborted),Model.Json);});
        web.MapGet("/ws",Socket);
        web.MapGet("/{**path}",(HttpContext c,string? path)=>{
            var key="web/"+(string.IsNullOrWhiteSpace(path)?"index.html":path);if(!Resources.TryGetValue(key,out var resource))return Results.NotFound();
            var stream=typeof(Server).Assembly.GetManifestResourceStream(resource);
            if(stream==null)return Results.NotFound();var ext=Path.GetExtension(key);return Results.Stream(stream,ext switch{".js"=>"text/javascript",".css"=>"text/css",".json"=>"application/json",".svg"=>"image/svg+xml",".webp"=>"image/webp",".png"=>"image/png",".webmanifest"=>"application/manifest+json",_=>"text/html"});});
    }
    static string Error(Exception ex)=>ex is UnauthorizedAccessException?"Zugriff verweigert":ex.GetBaseException().Message;
    public void Note(string s){log.Enqueue(DateTime.Now.ToString("HH:mm:ss")+"  "+s);while(log.Count>100)log.TryDequeue(out _);}
    public async Task Start(){await web.StartAsync();loops=[Task.Run(()=>obs.Loop(end.Token)),Task.Run(Monitor)];}
    public string HostLink(){lock(security){bootstrap=Secret();bootExpires=DateTime.UtcNow.AddMinutes(1);return BaseUrl.Replace(new Uri(BaseUrl).Host,"127.0.0.1")+"/#host="+bootstrap;}}
    public string PairLink(){lock(security){pairCode=Convert.ToHexString(RandomNumberGenerator.GetBytes(12));expires=DateTime.UtcNow.AddMinutes(2);return BaseUrl+"/#pair="+pairCode;}}
    void PersistDevices(){var p=Path.Combine(dir,"devices.json");File.WriteAllText(p+".tmp",JsonSerializer.Serialize(Devices));File.Move(p+".tmp",p,true);}
    public void Revoke(string id){lock(security){foreach(var kv in devices.Where(x=>x.Value.Id==id))devices.TryRemove(kv.Key,out _);PersistDevices();}foreach(var p in peers.Values.Where(x=>x.Identity.Id==id))p.Abort();if(jobs.TryGetValue(id,out var job))job.Cancel();}
    void Limit(string key,int count,int seconds){lock(security){var now=Environment.TickCount64;if(!rates.TryGetValue(key,out var q))rates[key]=q=new();while(q.Count>0&&q.Peek()<now-seconds*1000)q.Dequeue();Model.Require(q.Count<count,"Zu viele Anfragen");q.Enqueue(now);}}
    async Task<IResult> Pair(HttpContext ctx){
        Limit("pair:"+ctx.Connection.RemoteIpAddress,8,60);var data=await ctx.Request.ReadFromJsonAsync<JsonObject>()??throw new ArgumentException();
        var code=data["code"]?.ToString()??"";var name=data["name"]?.ToString()?.Trim()??"Remote";Model.Require(name.Length is >0 and <=60,"Gerätename ungültig");
        Device d;var token=Secret();bool host;
        lock(security){host=IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress!)&&code.Length==64&&code==bootstrap&&bootExpires>DateTime.UtcNow;
            if(host)bootstrap="";else{if(code.Length!=24||code!=pairCode||expires<DateTime.UtcNow)throw new UnauthorizedAccessException();pairCode="";}}
        if(host)d=admin with{Hash=Hash(token)};
        else{if(!await approval.WaitAsync(0))throw new InvalidOperationException("Kopplung läuft");try{
            var role=await platform.Approve(name+" · "+ctx.Connection.RemoteIpAddress).WaitAsync(TimeSpan.FromMinutes(2),ctx.RequestAborted);
            if(role is not ("control" or "edit"))throw new UnauthorizedAccessException();d=new(Model.Id(),name,role,Hash(token),DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds());
        }finally{approval.Release();}}
        lock(security){devices[d.Hash]=d;if(!host)PersistDevices();}
        ctx.Response.Cookies.Append("nova",token,new CookieOptions{HttpOnly=true,SameSite=SameSiteMode.Strict,Secure=ctx.Request.IsHttps,MaxAge=host?null:TimeSpan.FromDays(30),Path="/"});
        return Results.Json(new {role=d.Role});
    }
    Device Auth(HttpContext c){var token=c.Request.Cookies["nova"];if(token?.Length!=64||!devices.TryGetValue(Hash(token),out var d)||d.Role!="host"&&d.Until<=DateTimeOffset.UtcNow.ToUnixTimeSeconds()||d.Role=="host"&&!IPAddress.IsLoopback(c.Connection.RemoteIpAddress!))throw new UnauthorizedAccessException();return d;}
    sealed class Peer(WebSocket ws,Device d):IDisposable{
        readonly SemaphoreSlim gate=new(1);public Device Identity=>d;public void Abort()=>ws.Abort();
        public async Task Send(object msg,CancellationToken ct){using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(3500);await gate.WaitAsync(timeout.Token);try{await ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(msg,Model.Json),WebSocketMessageType.Text,true,timeout.Token);}finally{gate.Release();}}
        public void Dispose()=>ws.Dispose();
    }
    async Task Socket(HttpContext c){
        var d=Auth(c);Limit("ws:"+d.Id,12,30);if(!c.WebSockets.IsWebSocketRequest||peers.Count>=32){c.Response.StatusCode=400;return;}
        using var ws=await c.WebSockets.AcceptWebSocketAsync();using var p=new Peer(ws,d);var id=Guid.NewGuid();peers[id]=p;var buf=new byte[8192];
        CancellationTokenSource? held=null;Task? holding=null;
        async Task Hold(Step step,CancellationToken token){
            try{if(!await operations.WaitAsync(0,token))throw new InvalidOperationException("Aktion läuft");
                try{await platform.Run(step,token);}finally{operations.Release();}
            }catch(OperationCanceledException){}catch(Exception ex){try{await p.Send(new{type="error",error=Error(ex)},end.Token);}catch{}}
        }
        try{
            await p.Send(new{type="hello",role=d.Role,board=Store.Current,state=latest,obs=obs.Snapshot(),catalog=platform.Catalog(),actions=Model.Kinds},c.RequestAborted);
            while(ws.State==WebSocketState.Open){
                using var ms=new MemoryStream();WebSocketReceiveResult r;
                do{r=await ws.ReceiveAsync(new ArraySegment<byte>(buf),c.RequestAborted);if(r.MessageType!=WebSocketMessageType.Text||ms.Length+r.Count>100000) return;ms.Write(buf,0,r.Count);}while(!r.EndOfMessage);
                var m=JsonSerializer.Deserialize<Msg>(ms.ToArray(),Model.Json)??throw new ArgumentException();object result;
                try{Auth(c);Limit(d.Id,180,10);
                    if(m.Op=="release"){held?.Cancel();continue;}
                    if(m.Op=="press"){
                        if(holding is {IsCompleted:false})continue;
                        var b=Store.Current;Model.Require(m.Data.GetProperty("revision").GetInt64()==b.Revision,"Deck geändert");
                        var pg=b.Profiles.Single(x=>x.Id==b.Active).Pages.Single(x=>x.Id==Str(m.Data,"page"));
                        var slot=m.Data.GetProperty("slot").GetInt32();Model.Require(slot is >=0 and <15,"Slot ungültig");
                        var binding=pg.Keys[slot].Action;if(binding is not {Kind:"hotkey",Value:"ptt"})throw new UnauthorizedAccessException();
                        held?.Dispose();held=CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted,end.Token);held.CancelAfter(30000);
                        holding=Hold(binding,held.Token);continue;
                    }
                    result=await Dispatch(d,m,c.RequestAborted);}
                catch(Exception ex){await p.Send(new{type="error",error=Error(ex)},c.RequestAborted);continue;}
                await p.Send(new{type="result",result},c.RequestAborted);
            }
        }catch(Exception ex)when(ex is WebSocketException or IOException or OperationCanceledException or JsonException or ArgumentException){}finally{held?.Cancel();if(holding!=null)await holding;held?.Dispose();peers.TryRemove(id,out _);}
    }
    async Task Broadcast(object msg){await Task.WhenAll(peers.Values.Select(async p=>{try{await p.Send(msg,end.Token);}catch{p.Abort();}}));}
    async Task Monitor(){while(!end.IsCancellationRequested){try{if(!peers.IsEmpty){
        latest=await platform.Read(end.Token);var b=Store.Current;if(b.Settings.AutoProfile){var p=b.Profiles.FirstOrDefault(x=>x.Match.Length>0&&x.Match.Equals(latest.Foreground,StringComparison.OrdinalIgnoreCase));if(p!=null&&p.Id!=b.Active){var updated=Store.Save(b with{Active=p.Id});await Broadcast(new{type="board",board=updated});}}
        await Broadcast(new{type="state",state=latest,obs=obs.Snapshot()});
    }}catch(Exception ex){Note(Error(ex));}await Task.Delay(1000*Store.Current.Settings.Interval,end.Token);}}
    static string Str(JsonElement el,string prop)=>el.TryGetProperty(prop,out var v)?v.GetString()??"":"";
    async Task<object> Dispatch(Device d,Msg m,CancellationToken ct){
        var data=m.Data;var b=Store.Current;
        switch(m.Op){
        case "host.devices":
            if(d.Role!="host")throw new UnauthorizedAccessException();
            return new{address=BaseUrl,devices=Devices.Select(x=>new{x.Id,x.Name,x.Role,online=peers.Values.Any(p=>p.Identity.Id==x.Id)}).ToArray()};
        case "host.qr":
            if(d.Role!="host")throw new UnauthorizedAccessException();
            return new{image=platform.QR(PairLink()),address=BaseUrl,expires=DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds()};
        case "host.revoke":
            if(d.Role!="host")throw new UnauthorizedAccessException();
            Revoke(Str(data,"id"));return "Zugriff entzogen";
        case "info":return new{role=d.Role,board=b,catalog=platform.Catalog(),actions=Model.Kinds};
        case "logs":return log.ToArray();
        case "preview":
            Limit("preview:"+d.Id,6,10);
            var scene=(await obs.Call("GetCurrentProgramScene",null,ct))["currentProgramSceneName"]!.ToString();
            return (await obs.Call("GetSourceScreenshot",new{sourceName=scene,imageFormat="jpeg",imageWidth=640,imageCompressionQuality=75},ct))["imageData"]!.ToString();
        case "save":case "restore":
            if(d.Role=="control")throw new UnauthorizedAccessException();
            var next=m.Op=="save"?Store.Save(data.Deserialize<Board>(Model.Json)??throw new ArgumentException()):Store.Restore();await Broadcast(new{type="board",board=next});return new{revision=next.Revision};
        case "cancel":if(jobs.TryGetValue(d.Id,out var j))j.Cancel();return "Abbruch angefordert";
        case "run":case "key":case "dry":case "test":break;
        default:throw new ArgumentException("Befehl unbekannt");}
        List<Step> steps;
        if(m.Op=="test"){if(d.Role=="control")throw new UnauthorizedAccessException();steps=data.Deserialize<List<Step>>(Model.Json)??throw new ArgumentException();Model.Require(steps.Count is >0 and <=32,"1–32 Schritte erforderlich");foreach(var s in steps)Model.Check(s);}
        else if(m.Op=="key"||m.Op=="dry"){
            Model.Require(data.GetProperty("revision").GetInt64()==b.Revision,"Deck geändert. Erneut auswählen.");
            var page=b.Profiles.Single(p=>p.Id==b.Active).Pages.Single(p=>p.Id==Str(data,"page"));var index=data.GetProperty("slot").GetInt32();Model.Require(index is >=0 and <15,"Slot ungültig");
            var k=page.Keys[index];var s=Str(data,"gesture") switch{"hold"=>k.Hold,"double"=>k.Double,_=>k.Action};steps=s?.Kind=="macro"?k.Macro??[]:s==null?[]:[s];
        }else{var s=data.Deserialize<Step>(Model.Json)??throw new ArgumentException();Model.Check(s);
            if(d.Role=="control" && !(s.Kind.StartsWith("audio.")||s.Kind.StartsWith("media.")||s.Kind is "profile" or "sound" or "sound.stop" or "sound.stopAll"))throw new UnauthorizedAccessException();steps=[s];}
        if(m.Op=="dry")return steps.Select(s=>s.Kind+" · "+s.Target+" "+s.Value).ToArray();
        using var job=CancellationTokenSource.CreateLinkedTokenSource(ct,end.Token);job.CancelAfter(TimeSpan.FromMinutes(3));
        if(!jobs.TryAdd(d.Id,job))throw new InvalidOperationException("Aktion läuft");var results=new List<string>();
        try{await operations.WaitAsync(job.Token);try{foreach(var s in steps){for(int i=0;i<s.Repeat;i++){
            job.Token.ThrowIfCancellationRequested();if(!devices.ContainsKey(d.Hash))throw new UnauthorizedAccessException();if(s.When.Length>0&&!string.Equals(latest?.Foreground,s.When,StringComparison.OrdinalIgnoreCase)){results.Add("Bedingung nicht erfüllt");break;}
            if(s.Kind is "obs.stream" or "obs.startStream" or "obs.stopStream" or "system.lock" or "system.restart" or "system.shutdown" or "close"){if(!await platform.Confirm(s.Kind+" · "+s.Target).WaitAsync(job.Token))throw new OperationCanceledException("Am PC abgelehnt");}
            string reply;switch(s.Kind){
                case "none":throw new ArgumentException("Slot noch frei");case "delay":await Task.Delay(Math.Clamp(int.Parse(s.Value),0,30000),job.Token);reply="Verzögerung beendet";break;
                case "profile":var current=Store.Current;var changed=Store.Save(current with{Active=s.Target});await Broadcast(new{type="board",board=changed});reply="Profil gewechselt";break;
                case "page":case "view":case "timer":await Broadcast(new{type="navigate",step=s});reply=s.Kind;break;
                default:reply=s.Kind.StartsWith("obs.")?await obs.Run(s,job.Token):await platform.Run(s,job.Token);break;}
            results.Add(reply);Note(d.Name+" · "+reply);
        }}}finally{operations.Release();}return string.Join(" · ",results);}
        catch(Exception ex){Note(d.Name+" · "+Error(ex));throw;}finally{jobs.TryRemove(d.Id,out _);}
    }
    public async ValueTask DisposeAsync(){end.Cancel();foreach(var peer in peers.Values)peer.Abort();obs.Dispose();try{await Task.WhenAll(loops);}catch(OperationCanceledException){}await web.StopAsync();await web.DisposeAsync();end.Dispose();}
}
