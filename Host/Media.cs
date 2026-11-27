using Windows.Media.Control;
using Windows.Storage.Streams;
namespace NovaLab;
public sealed class Media {
    GlobalSystemMediaTransportControlsSessionManager? manager;
    async Task<GlobalSystemMediaTransportControlsSessionManager> Manager()=>manager??=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
    string lastKey="",cover="";
    public async Task<object> Read(){
        var m=await Manager();var all=m.GetSessions();var session=m.GetCurrentSession();
        var list=new List<object>();foreach(var s in all){var id=s.SourceAppUserModelId;
            var props=await s.TryGetMediaPropertiesAsync();var play=s.GetPlaybackInfo();var t=s.GetTimelineProperties();
            var key=id+"|"+props.Artist+"|"+props.Title+"|"+props.AlbumTitle;string image="";
            if(s.SourceAppUserModelId==session?.SourceAppUserModelId||id.Contains("spotify",StringComparison.OrdinalIgnoreCase)){
                if(lastKey!=key){cover="";if(props.Thumbnail!=null){using var stream=await props.Thumbnail.OpenReadAsync();if(stream.Size is >0 and <=400000 && stream.ContentType.StartsWith("image/")){
                    using var reader=new DataReader(stream.GetInputStreamAt(0));await reader.LoadAsync((uint)stream.Size);var bytes=new byte[(int)stream.Size];reader.ReadBytes(bytes);cover="data:"+stream.ContentType+";base64,"+Convert.ToBase64String(bytes);
                }}lastKey=key;}image=cover;
            }
            var ctl=play.Controls;var playing=play.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            double progress=t.Position.TotalSeconds;if(playing)progress+=Math.Max(0,(DateTimeOffset.UtcNow-t.LastUpdatedTime).TotalSeconds)*(play.PlaybackRate??1);
            list.Add(new{id,title=props.Title,artist=props.Artist,album=props.AlbumTitle,cover=image,playing,position=Math.Clamp(progress,0,Math.Max(0,t.EndTime.TotalSeconds)),duration=t.EndTime.TotalSeconds,
                canPlay=ctl.IsPlayEnabled,canPause=ctl.IsPauseEnabled,canNext=ctl.IsNextEnabled,canPrevious=ctl.IsPreviousEnabled,canSeek=ctl.IsPlaybackPositionEnabled,canShuffle=ctl.IsShuffleEnabled,canRepeat=ctl.IsRepeatEnabled});
        }return new{sessions=list};
    }
    public async Task<string> Run(Step step){
        var m=await Manager();var s=step.Target.Length==0?m.GetCurrentSession():m.GetSessions().FirstOrDefault(x=>x.SourceAppUserModelId.Equals(step.Target,StringComparison.OrdinalIgnoreCase)||(step.Target=="spotify"&&x.SourceAppUserModelId.Contains("spotify",StringComparison.OrdinalIgnoreCase)));
        if(s==null)throw new InvalidOperationException("Keine passende Mediensitzung");
        var info=s.GetPlaybackInfo();bool ok=step.Kind switch{
            "media.play"=>await s.TryPlayAsync(),"media.pause"=>await s.TryPauseAsync(),"media.toggle"=>await s.TryTogglePlayPauseAsync(),"media.stop"=>await s.TryStopAsync(),
            "media.next"=>await s.TrySkipNextAsync(),"media.previous"=>await s.TrySkipPreviousAsync(),"media.seek"=>await s.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(Math.Clamp(int.Parse(step.Value),0,s.GetTimelineProperties().EndTime.TotalSeconds)).Ticks),
            "media.shuffle"=>await s.TryChangeShuffleActiveAsync(info.IsShuffleActive!=true),"media.repeat"=>await s.TryChangeAutoRepeatModeAsync(info.AutoRepeatMode==Windows.Media.MediaPlaybackAutoRepeatMode.List?Windows.Media.MediaPlaybackAutoRepeatMode.None:Windows.Media.MediaPlaybackAutoRepeatMode.List),
            _=>false};if(!ok)throw new InvalidOperationException("Player unterstützt die Aktion aktuell nicht");return "Player hat die Anfrage angenommen";
    }
}
