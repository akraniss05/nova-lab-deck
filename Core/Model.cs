using System.Text.Json;
using System.Text.RegularExpressions;

namespace NovaLab;
public record Step(string Kind="none", string Target="", string Value="", int Repeat=1, string When="");
public record KeyStyle(string Accent="#8B5CF6",string End="#37184f",string Text="#ffffff",int Radius=17,int Border=1,int Size=15,int Icon=74,int Glow=36,int Opacity=100,string Motion="pulse",string Align="bottom",string Background="");
public record Key(string Name="",string Icon="macro",Step? Action=null,string Description="", KeyStyle? Style=null,Step? Hold=null,Step? Double=null,List<Step>? Macro=null,string ActiveIcon="",KeyStyle? ActiveStyle=null);
public record Page(string Id,string Name,List<Key> Keys);
public record Profile(string Id,string Name,List<Page> Pages,string Match="");
public record Preferences(string Theme="dark",string Language="de",int Motion=65,int Interval=2,bool AutoProfile=false);
public record Board(int Schema,long Revision,List<Profile> Profiles,string Active,Preferences Settings);
public record CatalogItem(string Id,string Name,string Type,string Icon="");
public record PlatformState(object Audio,object Media,object System,string Foreground);
public interface IPlatform : IDisposable {
    IReadOnlyList<CatalogItem> Catalog();
    Task<string> Run(Step step,CancellationToken ct);
    Task<PlatformState> Read(CancellationToken ct);
    Task<bool> Confirm(string message);
    Task<string?> Approve(string name);
}
public static class Model {
    public static JsonSerializerOptions Json = new(JsonSerializerDefaults.Web){MaxDepth=32};
    public static string Id()=>Guid.NewGuid().ToString("N");
    public static Page Blank(string name)=>new(Id(),name,Enumerable.Range(0,15).Select(_=>new Key()).ToList());
    public static Board Default(){
        var p=Blank("Studio"); (string,string,Step)[] keys=[
            ("Stream","stream",new("obs.stream")),("Aufnahme","record",new("obs.record")),("Mikro","mic",new("audio.toggle","@mic")),("Kamera","cam",new("obs.camera")),
            ("Szene 1","sunset",new("obs.scene","#1")),("Szene 2","city",new("obs.scene","#2")),("Pause","coffee",new("timer","","300")),("Chat","chat",new("view","apps")),
            ("TikTok Studio","tiktok",new("launch","find:TikTok LIVE Studio")),("Discord","discord",new("launch","find:Discord")),("Spotify","spotify",new("launch","find:Spotify")),
            ("Leiser","quieter",new("audio.delta","@master","-5")),("Lauter","louder",new("audio.delta","@master","5")),("Soundboard","pads",new("view","sounds")),("Audio-Mixer","sliders-horizontal",new("view","audio"))];
        for(int i=0;i<15;i++) p.Keys[i]=new(keys[i].Item1,keys[i].Item2,keys[i].Item3);
        var second=Blank("Produktion");
        (string,string,Step)[] more=[("Replay","rewind",new("obs.replay")),("Clip speichern","clapperboard",new("obs.clip")),("Studio-Modus","panels-top-left",new("obs.studio")),("Player","disc-3",new("view","media")),("Monitoring","tower",new("view","system")),("Desktop stumm","volume-x",new("audio.toggle","@master"))];
        for(int i=0;i<more.Length;i++)second.Keys[i]=new(more[i].Item1,more[i].Item2,more[i].Item3);
        var profiles=new List<Profile>{new("streaming","Streaming",[p,second])};
        foreach(var (id,title,items) in new[]{("gaming","Gaming",new[]{"Steam","Discord","Xbox"}),("music","Musik",new[]{"Spotify","VLC","Apple Music"}),("dev","Entwicklung",new[]{"Visual Studio Code","Blender","Roblox Studio"})}){
            var page=Blank(title); for(int i=0;i<items.Length;i++)page.Keys[i]=new(items[i],i==0?"rocket":i==1?"headphones":"boxes",new("launch","find:"+items[i]));
            profiles.Add(new(id,title,[page]));
        }
        return new(1,0,profiles,"streaming",new());
    }
    public static void Require(bool ok,string why){if(!ok)throw new ArgumentException(why);}
    public static void Validate(Board b){
        Require(b.Schema==1 && b.Profiles is {Count:>0 and <=128} && b.Profiles.Any(p=>p.Id==b.Active),"Profil oder Format ungültig");
        var ids=new HashSet<string>();void ID(string id)=>Require(Regex.IsMatch(id,@"^[\w-]{1,64}$")&&ids.Add(id),"Doppelte oder ungültige ID");
        Require(b.Settings.Theme is "dark" or "light" or "system" or "amoled" && b.Settings.Language is "de" or "en" && b.Settings.Interval is >=1 and <=15 && b.Settings.Motion is >=0 and <=100,"Einstellungen ungültig");
        foreach(var p in b.Profiles){ID(p.Id);Require(p.Name.Length is >0 and <=80 && p.Match.Length<=100 && p.Pages.Count is >0 and <=256,"Profil ungültig");
            foreach(var page in p.Pages){ID(page.Id);Require(page.Name.Length is >0 and <=80 && page.Keys.Count==15,"Eine Seite benötigt 15 Slots");
                foreach(var k in page.Keys){Require(k.Name.Length<=80 && k.Description.Length<=512,"Text zu lang"); Image(k.Icon);Image(k.ActiveIcon);CheckStyle(k.Style);CheckStyle(k.ActiveStyle);
                    foreach(var s in new[]{k.Action,k.Hold,k.Double}.Concat(k.Macro??[]))if(s!=null)Check(s);
                    Require((k.Macro?.Count??0)<=32,"Maximal 32 Schritte");
                }
            }
        }
    }
    public static void Image(string s){
        if(Regex.IsMatch(s,@"^[a-zA-Z0-9_-]{0,100}$"))return;
        var parts=s.Split(',',2);Require(parts.Length==2 && s.Length<=400000 && parts[0] is "data:image/png;base64" or "data:image/webp;base64" or "data:image/gif;base64" or "data:image/jpeg;base64","Bildformat oder Größe ungültig");
        var bytes=Convert.FromBase64String(parts[1]); Require(bytes.Length>=12 && (parts[0].Contains("gif")?bytes.AsSpan(0,3).SequenceEqual("GIF"u8):parts[0].Contains("png")?bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}):parts[0].Contains("webp")?bytes.AsSpan(0,4).SequenceEqual("RIFF"u8)&&bytes.AsSpan(8,4).SequenceEqual("WEBP"u8):bytes[0]==255&&bytes[1]==216),"Bilddaten ungültig");
    }
    static void CheckStyle(KeyStyle? s){if(s==null)return;foreach(var c in new[]{s.Accent,s.End,s.Text})Require(Regex.IsMatch(c,"^#[0-9a-fA-F]{6}$"),"Farbe ungültig");
        Require(s.Radius is >=0 and <=48 && s.Border is >=0 and <=8 && s.Size is >=9 and <=36 && s.Icon is >=20 and <=100 && s.Glow is >=0 and <=100 && s.Opacity is >=20 and <=100 && s.Motion is "none" or "pulse" or "wave" or "rgb" or "particles" && s.Align is "top" or "center" or "bottom","Stil ungültig");Image(s.Background);}
    public static readonly string[] Kinds="none view page profile timer delay macro launch focus minimize maximize close hotkey sound sound.stop sound.stopAll audio.set audio.delta audio.toggle audio.preset media.play media.pause media.toggle media.next media.previous media.stop media.seek media.shuffle media.repeat obs.stream obs.record obs.pause obs.scene obs.source obs.mute obs.volume obs.studio obs.transition obs.camera obs.replay obs.clip obs.screenshot system.lock system.restart system.shutdown".Split();
    public static void Check(Step s)=>Require(Kinds.Contains(s.Kind)&&s.Target.Length<=500&&s.Value.Length<=512&&s.When.Length<=100&&s.Repeat is >=1 and <=8,"Aktion ungültig");
}
public sealed class BoardStore {
    readonly string file;readonly object sync=new();Board board;
    public BoardStore(string dir){Directory.CreateDirectory(dir);file=Path.Combine(dir,"deck.json");board=File.Exists(file)?Parse(File.ReadAllText(file)):Model.Default();}
    static Board Parse(string json){var b=JsonSerializer.Deserialize<Board>(json,Model.Json)??throw new InvalidDataException();Model.Validate(b);return b;}
    public Board Current {get{lock(sync)return board;}}
    public Board Save(Board next){lock(sync){if(next.Revision!=board.Revision)throw new InvalidOperationException("Konflikt. Aktuellen Stand laden.");Model.Validate(next);
        var updated=next with {Revision=board.Revision+1};var json=JsonSerializer.Serialize(updated,Model.Json);Model.Require(json.Length<=8_000_000,"Profilbestand zu groß");
        var temp=file+".tmp";File.WriteAllText(temp,json);if(File.Exists(file))File.Replace(temp,file,file+".bak");else File.Move(temp,file);return board=updated;}}
    public Board Restore(){lock(sync){return Save(Parse(File.ReadAllText(file+".bak")) with {Revision=board.Revision});}}
}
