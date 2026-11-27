using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Vorbis;
using System.Diagnostics;
using System.Text.Json;

namespace NovaLab;
public record Channel(string Id,string Name,int Volume,bool Muted,int Peak,int Sessions);
public sealed class Mixer(string dir) {
    readonly object gate=new();
    readonly Dictionary<string,int> waiting=new(StringComparer.OrdinalIgnoreCase);
    record Group(string Name,List<AudioSessionControl> Items);
    static Dictionary<string,Group> Groups(MMDeviceEnumerator enumerator,List<MMDevice> opened){
        var groups=new Dictionary<string,Group>(StringComparer.OrdinalIgnoreCase);
        foreach(var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active)){
            opened.Add(device);var sessions=device.AudioSessionManager.Sessions;
            for(int i=0;i<sessions.Count;i++){var s=sessions[i];try{
                var pid=(int)s.GetProcessID;if(pid<=0){s.Dispose();continue;}using var p=Process.GetProcessById(pid);var name=p.ProcessName;
                if(!groups.TryGetValue(name,out var g))groups[name]=g=new(name,[]);g.Items.Add(s);
            }catch{s.Dispose();}}
        }return groups;
    }
    T With<T>(Func<MMDeviceEnumerator,Dictionary<string,Group>,T> action){lock(gate){using var e=new MMDeviceEnumerator();var opened=new List<MMDevice>();var groups=Groups(e,opened);
        try{return action(e,groups);}finally{foreach(var g in groups.Values)foreach(var s in g.Items)s.Dispose();foreach(var d in opened)d.Dispose();}}}
    static readonly (string Id,DataFlow Flow)[] Endpoints=[("@master",DataFlow.Render),("@mic",DataFlow.Capture)];
    static int Pct(float v)=>(int)Math.Round(Math.Clamp(v,0,1)*100);
    public object Read()=>With((e,groups)=>{
        var list=new List<Channel>();foreach(var ep in Endpoints){try{using var d=e.GetDefaultAudioEndpoint(ep.Flow,Role.Multimedia);list.Add(new(ep.Id,d.FriendlyName,Pct(d.AudioEndpointVolume.MasterVolumeLevelScalar),d.AudioEndpointVolume.Mute,Pct(d.AudioMeterInformation.MasterPeakValue),1));}catch(System.Runtime.InteropServices.COMException){}}
        foreach(var g in groups.Values){if(waiting.TryGetValue(g.Name,out var pending)){foreach(var s in g.Items)s.SimpleAudioVolume.Volume=pending/100f;waiting.Remove(g.Name);}
            list.Add(new(g.Name,g.Name,Pct(g.Items.Average(s=>s.SimpleAudioVolume.Volume)),g.Items.All(s=>s.SimpleAudioVolume.Mute),Pct(g.Items.Max(s=>s.AudioMeterInformation.MasterPeakValue)),g.Items.Count));}
        var devices=new List<object>();foreach(var d in e.EnumerateAudioEndPoints(DataFlow.All,DeviceState.Active)){using(d)devices.Add(new{id=d.ID,name=d.FriendlyName,flow=d.DataFlow.ToString()});}
        return new{channels=list,devices,waiting=new Dictionary<string,int>(waiting),presets=Presets().Keys.ToArray()};
    });
    Dictionary<string,Dictionary<string,int>> Presets(){var file=Path.Combine(dir,"audio-presets.json");return File.Exists(file)?JsonSerializer.Deserialize<Dictionary<string,Dictionary<string,int>>>(File.ReadAllText(file))??new():new();}
    public string Change(Step step)=>With((e,groups)=>{
        if(step.Kind=="audio.default")return EndpointPolicy.Set(e,step.Target,step.Value);
        if(step.Kind=="audio.preset"){
            Model.Require(step.Target.Length is >0 and <=60,"Presetname erforderlich");var presets=Presets();
            if(step.Value=="save"){var values=groups.ToDictionary(x=>x.Key,x=>Pct(x.Value.Items.Average(s=>s.SimpleAudioVolume.Volume)));foreach(var ep in Endpoints){try{using var d=e.GetDefaultAudioEndpoint(ep.Flow,Role.Multimedia);values[ep.Id]=Pct(d.AudioEndpointVolume.MasterVolumeLevelScalar);}catch(System.Runtime.InteropServices.COMException){}}
                presets[step.Target]=values;var file=Path.Combine(dir,"audio-presets.json");File.WriteAllText(file+".tmp",JsonSerializer.Serialize(presets));File.Move(file+".tmp",file,true);return "Audioprofil gespeichert";}
            if(!presets.TryGetValue(step.Target,out var saved))throw new ArgumentException("Audioprofil fehlt");
            foreach(var v in saved){if(v.Key.StartsWith('@')){using var device=e.GetDefaultAudioEndpoint(v.Key=="@mic"?DataFlow.Capture:DataFlow.Render,Role.Multimedia);device.AudioEndpointVolume.MasterVolumeLevelScalar=v.Value/100f;}else if(groups.TryGetValue(v.Key,out var g)){foreach(var s in g.Items)s.SimpleAudioVolume.Volume=v.Value/100f;}else waiting[v.Key]=v.Value;}
            return "Audioprofil angewendet";
        }
        float Level(float old)=>Math.Clamp((step.Kind=="audio.delta"?old:0)+int.Parse(step.Value)/100f,0,1);
        if(step.Target is "@master" or "@mic"){using var d=e.GetDefaultAudioEndpoint(step.Target=="@mic"?DataFlow.Capture:DataFlow.Render,Role.Multimedia);var vol=d.AudioEndpointVolume;if(step.Kind=="audio.toggle")vol.Mute=!vol.Mute;else vol.MasterVolumeLevelScalar=Level(vol.MasterVolumeLevelScalar);}
        else if(groups.TryGetValue(step.Target,out var group)){var mute=!group.Items.All(s=>s.SimpleAudioVolume.Mute);var level=step.Kind=="audio.toggle"?0:Level(group.Items.Average(x=>x.SimpleAudioVolume.Volume));
            foreach(var s in group.Items){if(step.Kind=="audio.toggle")s.SimpleAudioVolume.Mute=mute;else s.SimpleAudioVolume.Volume=level;}}
        else if(step.Kind=="audio.set" && step.Target.Length>0){waiting[step.Target]=Pct(Level(0));return "Vorgemerkt bis zur nächsten Audio-Sitzung";}
        else throw new InvalidOperationException("Keine Audio-Sitzung für "+step.Target);
        return "Audio geändert · "+step.Target;
    });
}
public sealed class SoundEngine:IDisposable {
    sealed class Envelope(WaveStream source,ISampleProvider samples,bool loop,float volume,int fadeIn,int fadeOut):ISampleProvider{
        long position;public volatile bool Stopping;long faded;
        public WaveFormat WaveFormat=>samples.WaveFormat;
        public int Read(float[] data,int offset,int count){
            int used=0;long rate=(long)WaveFormat.SampleRate*WaveFormat.Channels;
            while(used<count){var n=samples.Read(data,offset+used,count-used);if(n==0){if(!loop||source.Position==0||Stopping)break;source.Position=0;continue;}
                for(int i=0;i<n;i++){var gain=fadeIn==0?1:Math.Min(1,position++/(rate*fadeIn/1000.0));if(Stopping){if(fadeOut==0||faded>=rate*fadeOut/1000)return used+i;gain*=1-faded++/(rate*fadeOut/1000.0);}data[offset+used+i]*=(float)gain*volume;}used+=n;
            }return used;
        }
    }
    sealed record Voice(string Id,WasapiOut Out,WaveStream Source,Envelope Gain,MMDevice Device);
    readonly List<Voice> voices=[];
    public void Play(Target t,int level,bool loop,int fadeIn,int fadeOut,string output){
        lock(voices){Model.Require(voices.Count<24,"Zu viele Sounds");using var e=new MMDeviceEnumerator();var d=output==""?e.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia):e.GetDevice(output);
            WaveStream reader=Path.GetExtension(t.Path).Equals(".ogg",StringComparison.OrdinalIgnoreCase)?new VorbisWaveReader(t.Path):new AudioFileReader(t.Path);
            var outp=new WasapiOut(d,AudioClientShareMode.Shared,true,100);
            try{var envelope=new Envelope(reader,reader.ToSampleProvider(),loop,level/100f,fadeIn,fadeOut);var v=new Voice(t.Id,outp,reader,envelope,d);outp.Init(envelope.ToWaveProvider());
                outp.PlaybackStopped+=(_,_)=>{lock(voices){if(voices.Remove(v)){outp.Dispose();reader.Dispose();d.Dispose();}}};voices.Add(v);outp.Play();
            }catch{voices.RemoveAll(v=>v.Out==outp);outp.Dispose();reader.Dispose();d.Dispose();throw;}
        }
    }
    public void Stop(string? id){lock(voices)foreach(var v in voices.Where(v=>id==null||v.Id==id))v.Gain.Stopping=true;}
    public void Dispose(){Voice[] all;lock(voices){all=voices.ToArray();voices.Clear();}foreach(var v in all){v.Out.Stop();v.Out.Dispose();v.Source.Dispose();v.Device.Dispose();}}
}
