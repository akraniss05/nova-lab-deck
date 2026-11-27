using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using NovaLab;
// Protocol fixture only. The production host never includes this assembly.
static class ObsProtocol {
 public static async Task Test(){
    var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
    var b=WebApplication.CreateSlimBuilder();b.Logging.ClearProviders();b.WebHost.ConfigureKestrel(k=>k.Listen(IPAddress.Loopback,port));await using var server=b.Build();server.UseWebSockets();
    var identified=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var scene=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    static string Hash(string s)=>Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    server.MapGet("/",async(HttpContext c)=>{
        using var ws=await c.WebSockets.AcceptWebSocketAsync();
        async Task Send(object msg)=>await ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(msg),WebSocketMessageType.Text,true,c.RequestAborted);
        async Task<JsonNode> Read(){using var message=new MemoryStream();var bytes=new byte[8192];WebSocketReceiveResult r;do{r=await ws.ReceiveAsync(new ArraySegment<byte>(bytes),c.RequestAborted);if(r.MessageType==WebSocketMessageType.Close)throw new IOException();message.Write(bytes,0,r.Count);}while(!r.EndOfMessage);return JsonNode.Parse(message.ToArray())!;}
        try{
            await Send(new{op=0,d=new{rpcVersion=1,authentication=new{salt="salt",challenge="challenge"}}});var msg=await Read();
            if((int?)msg["op"]!=1||msg["d"]?["authentication"]?.ToString()!=Hash(Hash("test-password"+"salt")+"challenge"))throw new Exception("Invalid OBS challenge response");
            await Send(new{op=2,d=new{negotiatedRpcVersion=1}});identified.TrySetResult();
            while(ws.State==WebSocketState.Open){var r=await Read();var data=r["d"]!;var t=(string?)data["requestType"];object result=t switch{
                "GetSceneList"=>new{currentProgramSceneName="Studio",scenes=new[]{new{sceneName="Studio"}}},
                "GetStreamStatus"=>new{outputActive=false},_=>new{}};
                if(t=="SetCurrentProgramScene")scene.TrySetResult((string)data["requestData"]!["sceneName"]!);
                var ok=t!="SaveReplayBuffer";await Send(new{op=7,d=new{requestType=t,requestId=(string)data["requestId"]!,requestStatus=new{result=ok,code=ok?100:500,comment=ok?"":"Replay buffer inactive"},responseData=result}});
            }
        }catch(Exception ex)when(ex is IOException or WebSocketException or OperationCanceledException or JsonException){}
    });
    await server.StartAsync();using var client=new Obs(()=>(port,"test-password"));using var token=new CancellationTokenSource(TimeSpan.FromSeconds(12));var loop=client.Loop(token.Token);
    try{await identified.Task.WaitAsync(token.Token);while(client.Status!="OBS verbunden")await Task.Delay(10,token.Token);
        await client.Run(new("obs.scene","#1"),token.Token);if(await scene.Task.WaitAsync(token.Token)!="Studio")throw new Exception("Scene mismatch");
        var denied=false;try{await client.Run(new("obs.clip"),token.Token);}catch(InvalidOperationException e){denied=e.Message=="Replay buffer inactive";}
        if(!denied)throw new Exception("OBS denial hidden");
        while(JsonSerializer.SerializeToNode(client.Snapshot())?["data"]?["scene"]?["currentProgramSceneName"]?.ToString()!="Studio")await Task.Delay(15,token.Token);
    }catch(Exception e){throw new Exception(client.Status,e);}finally{token.Cancel();try{await loop;}catch(OperationCanceledException){}await server.StopAsync();}
 }
}
