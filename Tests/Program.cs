using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NovaLab;
sealed class Fixture:IPlatform {
    public string Role="edit";public bool Confirmed=false;public int Calls;
    public Task<bool> Confirm(string m)=>Task.FromResult(Confirmed);
    public Task<string?> Approve(string n)=>Task.FromResult<string?>(Role);
    public IReadOnlyList<CatalogItem> Catalog()=>[new("spotify","Spotify","app")];
    public Task<PlatformState> Read(CancellationToken ct)=>Task.FromResult(new PlatformState(new{channels=Array.Empty<object>()},new{sessions=Array.Empty<object>()},new{},""));
    public Task<string> Run(Step s,CancellationToken ct){Calls++;return Task.FromResult("fixture dispatch");}
    public void Dispose(){}
}
static class Program {
 static int tests;static void Assert(bool ok,string msg){if(!ok)throw new Exception(msg);Console.WriteLine("PASS "+msg);tests++;}
 static async Task Main(string[] args){
  var path=Path.Combine(Path.GetTempPath(),"nova-test-"+Guid.NewGuid());Directory.CreateDirectory(path);
  using var engine=new Fixture();var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
  var obs=new Obs(()=>(1,""));await using var host=new Server(path,engine,obs,port,IPAddress.Loopback);await host.Start();var url=host.BaseUrl;
  if(args.Contains("--serve")){Console.WriteLine("TEST_HOST="+host.HostLink());Console.Out.Flush();await Task.Delay(TimeSpan.FromMinutes(20));return;}
  try{
   using var jar=new HttpClientHandler{CookieContainer=new CookieContainer()};using var client=new HttpClient(jar){BaseAddress=new Uri(url)};
   async Task<HttpResponseMessage> Post(string route,object data,bool origin=true){var req=new HttpRequestMessage(HttpMethod.Post,route){Content=JsonContent.Create(data)};if(origin)req.Headers.Add("Origin",url);return await client.SendAsync(req);}
   var root=await client.GetStringAsync("/");Assert(root.Contains("NovaLab Deck"),"embedded frontend");
   var icons=(await client.GetFromJsonAsync<JsonArray>("/ASSET_MANIFEST.json"))!;Assert(icons.Count>=150,"at least 150 distinct assets");
   foreach(var icon in icons){if(!(await client.GetAsync("/"+icon!["file"])).IsSuccessStatusCode)throw new Exception("missing "+icon["file"]);}Assert(true,"every catalog asset resolves");
   Assert((await Post("/cmd",new{op="info",data=new{}})).StatusCode==HttpStatusCode.Forbidden,"anonymous control denied");
   var code=host.PairLink().Split('=')[1];Assert((await Post("/pair",new{code,name="Phone"},false)).StatusCode==HttpStatusCode.Forbidden,"cross-origin pair denied");
   Assert((await Post("/pair",new{code,name="Phone"})).IsSuccessStatusCode,"native approval exchanges single-use code for cookie");
   Assert((await Post("/pair",new{code,name="Replay"})).StatusCode==HttpStatusCode.Forbidden,"pairing code replay denied");
   var info=await (await Post("/cmd",new{op="info",data=new{}})).Content.ReadFromJsonAsync<JsonObject>();Assert((string?)info!["role"]=="edit","edit role");
   var board=host.Store.Current;Assert(board.Profiles.All(p=>p.Pages.All(pg=>pg.Keys.Count==15)),"15 slots per page");
   var altered=board with{Settings=board.Settings with{Theme="amoled"}};
   Assert((await Post("/cmd",new{op="save",data=altered})).IsSuccessStatusCode,"settings persisted");
   Assert((await Post("/cmd",new{op="save",data=altered})).StatusCode==HttpStatusCode.Conflict,"stale updates rejected");
   Assert(new BoardStore(path).Current.Settings.Theme=="amoled","restart reads persisted profile");
   var bad=JsonSerializer.SerializeToNode(host.Store.Current,Model.Json)!;bad["profiles"]![0]!["pages"]![0]!["keys"]!.AsArray().RemoveAt(0);
   Assert((await Post("/cmd",new{op="save",data=bad})).StatusCode==HttpStatusCode.BadRequest,"malformed layout rejected");
   var critical=await Post("/cmd",new{op="run",data=new Step("system.shutdown")});Assert(!critical.IsSuccessStatusCode&&engine.Calls==0,"critical action declined without dispatch");
   var invalid=await Post("/cmd",new{op="run",data=new Step("shell","anything")});Assert(!invalid.IsSuccessStatusCode,"unknown actions denied");
   using var ws=new ClientWebSocket();ws.Options.Cookies=jar.CookieContainer;ws.Options.SetRequestHeader("Origin",url);await ws.ConnectAsync(new Uri(url.Replace("http","ws")+"/ws"),default);
   async Task<JsonNode> Read(){using var ms=new MemoryStream();var buf=new byte[8192];WebSocketReceiveResult r;do{r=await ws.ReceiveAsync(new ArraySegment<byte>(buf),CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));ms.Write(buf,0,r.Count);}while(!r.EndOfMessage);return JsonNode.Parse(ms.ToArray())!;}
   Assert((string?)(await Read())["type"]=="hello","authenticated websocket hello");
   await ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new{op="key",data=new{revision=host.Store.Current.Revision,page=board.Profiles[0].Pages[0].Id,slot=10,gesture="tap"}}),WebSocketMessageType.Text,true,default);
   JsonNode m;do{m=await Read();}while(m["type"]?.ToString()=="state");Assert(m["result"]?.ToString()=="fixture dispatch"&&engine.Calls==1,"WebSocket press reaches platform dispatcher once");
   using var foreign=new ClientWebSocket();foreign.Options.Cookies=jar.CookieContainer;foreign.Options.SetRequestHeader("Origin","https://foreign.invalid");var denied=false;try{await foreign.ConnectAsync(new Uri(url.Replace("http","ws")+"/ws"),default);}catch(WebSocketException){denied=true;}Assert(denied,"foreign websocket origin rejected");
   var id=host.Devices.Single().Id;host.Revoke(id);Assert((await Post("/cmd",new{op="info",data=new{}})).StatusCode==HttpStatusCode.Forbidden,"device revocation takes effect");
   engine.Role="control";await Post("/pair",new{code=host.PairLink().Split('=')[1],name="Remote"});Assert((await Post("/cmd",new{op="save",data=host.Store.Current})).StatusCode==HttpStatusCode.Forbidden,"read-only layout permission enforced");
   Assert((await Post("/cmd",new{op="test",data=new[]{new Step("launch","spotify")}})).StatusCode==HttpStatusCode.Forbidden,"control device cannot inject macro steps");
   Console.WriteLine(tests+" passed");
  }finally{Directory.Delete(path,true);}
 }
}
