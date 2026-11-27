using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NovaLab;

public sealed class Obs(Func<(int Port,string Password)> config) : IDisposable {
    ClientWebSocket? socket;
    readonly SemaphoreSlim writes=new(1,1);
    readonly ConcurrentDictionary<string,TaskCompletionSource<JsonObject>> pending=new();
    public volatile string Status="OBS offline";
    volatile JsonObject cache=new();
    public object Snapshot()=>new {connected=socket?.State==WebSocketState.Open && Status=="OBS verbunden",status=Status,data=cache};
    static string Hash(string text)=>Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static async Task<JsonObject> Receive(ClientWebSocket ws,CancellationToken ct){
        using var ms=new MemoryStream();var buf=new byte[8192];WebSocketReceiveResult r;
        do{r=await ws.ReceiveAsync(new ArraySegment<byte>(buf),ct);if(r.MessageType!=WebSocketMessageType.Text||ms.Length+r.Count>3_000_000)throw new IOException("OBS-Verbindung beendet");ms.Write(buf,0,r.Count);}while(!r.EndOfMessage);
        return JsonNode.Parse(ms.ToArray())!.AsObject();
    }
    async Task Send(ClientWebSocket ws,object body,CancellationToken ct){var data=JsonSerializer.SerializeToUtf8Bytes(body);await writes.WaitAsync(ct);try{await ws.SendAsync(data,WebSocketMessageType.Text,true,ct);}finally{writes.Release();}}
    public async Task<JsonObject> Call(string type,object? args,CancellationToken ct){
        var ws=socket;if(ws?.State!=WebSocketState.Open)throw new InvalidOperationException("OBS offline");
        var id=Model.Id();var result=new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);pending[id]=result;
        try{await Send(ws,new {op=6,d=new{requestType=type,requestId=id,requestData=args??new {}}},ct);var d=await result.Task.WaitAsync(TimeSpan.FromSeconds(5),ct);
            if(d["requestStatus"]?["result"]?.GetValue<bool>()!=true)throw new InvalidOperationException(d["requestStatus"]?["comment"]?.ToString()??"OBS hat die Aktion abgelehnt");
            return d["responseData"] as JsonObject??new();
        }finally{pending.TryRemove(id,out _);}
    }
    async Task Listen(ClientWebSocket ws,CancellationToken ct){
        while(!ct.IsCancellationRequested){var msg=await Receive(ws,ct);if((int?)msg["op"]==7 && msg["d"] is JsonObject data && pending.TryGetValue(data["requestId"]!.ToString(),out var t))t.TrySetResult(data);}
    }
    public async Task Loop(CancellationToken ct){
        while(!ct.IsCancellationRequested){
            using var ws=new ClientWebSocket();using var live=CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task? reader=null;
            try{
                var cfg=config();using var init=CancellationTokenSource.CreateLinkedTokenSource(ct);init.CancelAfter(5000);
                await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{cfg.Port}"),init.Token);
                var hello=await Receive(ws,init.Token);if((int?)hello["op"]!=0)throw new InvalidDataException("OBS-Protokoll");
                var ident=new JsonObject{["rpcVersion"]=1,["eventSubscriptions"]=0};
                if(hello["d"]?["authentication"] is JsonObject auth)ident["authentication"]=Hash(Hash(cfg.Password+auth["salt"])+auth["challenge"]);
                await Send(ws,new {op=1,d=ident},init.Token);if((int?)(await Receive(ws,init.Token))["op"]!=2)throw new InvalidDataException("OBS-Anmeldung fehlgeschlagen");
                socket=ws;Status="OBS verbunden";reader=Listen(ws,live.Token);
                while(!reader.IsCompleted){
                    var state=new JsonObject();foreach(var pair in new[]{("stream","GetStreamStatus"),("record","GetRecordStatus"),("scene","GetSceneList"),("inputs","GetInputList"),("stats","GetStats"),("camera","GetVirtualCamStatus"),("replay","GetReplayBufferStatus"),("studio","GetStudioModeEnabled"),("transitions","GetSceneTransitionList")}){
                        try{state[pair.Item1]=await Call(pair.Item2,null,ct);}catch(InvalidOperationException){ }
                    }
                    var name=state["scene"]?["currentProgramSceneName"]?.ToString();if(name!=null)state["sources"]=await Call("GetSceneItemList",new{sceneName=name},ct);
                    cache=state;await Task.WhenAny(reader,Task.Delay(2000,ct));ct.ThrowIfCancellationRequested();
                }
                await reader;
            }catch(Exception e){Status=e is WebSocketException?"OBS offline / Anmeldung prüfen":"OBS: "+e.Message;}
            finally{socket=null;cache=new();live.Cancel();ws.Abort();foreach(var t in pending.Values)t.TrySetException(new IOException("OBS getrennt"));if(reader!=null)try{await reader;}catch{}}
            await Task.Delay(4000,ct);
        }
    }
    public async Task<string> Run(Step s,CancellationToken ct){
        if(s.Kind=="obs.screenshot"){
            var name=(await Call("GetCurrentProgramScene",null,ct))["currentProgramSceneName"]!.ToString();
            var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),"NovaLab-"+DateTime.Now.ToString("yyyyMMdd-HHmmssfff")+".png");
            await Call("SaveSourceScreenshot",new{sourceName=name,imageFormat="png",imageFilePath=path},ct);return Path.GetFileName(path);
        }
        string request;object? args=null;
        switch(s.Kind){
        case "obs.startStream":request="StartStream";break;case "obs.stopStream":request="StopStream";break;
        case "obs.startRecord":request="StartRecord";break;case "obs.stopRecord":request="StopRecord";break;
        case "obs.stream":request="ToggleStream";break;case "obs.record":request="ToggleRecord";break;
        case "obs.pause":request="ToggleRecordPause";break;case "obs.camera":request="ToggleVirtualCam";break;
        case "obs.replay":request="ToggleReplayBuffer";break;case "obs.clip":request="SaveReplayBuffer";break;
        case "obs.studio":request="SetStudioModeEnabled";args=new{studioModeEnabled=!(bool)(await Call("GetStudioModeEnabled",null,ct))["studioModeEnabled"]!};break;
        case "obs.scene":
            var name=s.Target;if(name.StartsWith('#')&&int.TryParse(name[1..],out var pos)){
                var scenes=(await Call("GetSceneList",null,ct))["scenes"]!.AsArray();Model.Require(pos>=1&&pos<=scenes.Count,"Szene fehlt");name=scenes[pos-1]!["sceneName"]!.ToString();
            }request="SetCurrentProgramScene";args=new{sceneName=name};break;
        case "obs.source":
            var scene=(await Call("GetCurrentProgramScene",null,ct))["currentProgramSceneName"]!.ToString();var item=(await Call("GetSceneItemId",new{sceneName=scene,sourceName=s.Target},ct))["sceneItemId"]!.GetValue<int>();
            var enabled=(bool)(await Call("GetSceneItemEnabled",new{sceneName=scene,sceneItemId=item},ct))["sceneItemEnabled"]!;
            request="SetSceneItemEnabled";args=new{sceneName=scene,sceneItemId=item,sceneItemEnabled=!enabled};break;
        case "obs.mute":request="ToggleInputMute";args=new{inputName=s.Target};break;
        case "obs.volume":request="SetInputVolume";args=new{inputName=s.Target,inputVolumeMul=Math.Clamp(int.Parse(s.Value),0,100)/100.0};break;
        case "obs.transition":request="SetCurrentSceneTransition";args=new{transitionName=s.Target};break;
        default:throw new ArgumentException("Unbekannte OBS-Aktion");}
        await Call(request,args,ct);return "OBS bestätigt · "+request;
    }
    public void Dispose(){socket?.Abort();}
}
