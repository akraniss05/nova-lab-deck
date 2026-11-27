using System.Diagnostics;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;
using QRCoder;

namespace NovaLab;
static class Program {
    [STAThread] static int Main(string[] args){ApplicationConfiguration.Initialize();
        if(args is ["--diagnose",var output]){
            var temp=Path.Combine(Path.GetTempPath(),"NovaLab-Probe-"+Guid.NewGuid());
            try{using var probe=new WindowsEngine(temp,_=>Task.FromResult(false),_=>Task.FromResult<string?>(null));probe.Scan();
                var state=Task.Run(()=>probe.Read(CancellationToken.None)).GetAwaiter().GetResult();
                var json=System.Text.Json.JsonSerializer.SerializeToNode(state,Model.Json)!;
                var result=new{platform=Environment.OSVersion.ToString(),applications=probe.Catalog().Count,audioChannels=json["audio"]?["channels"]?.AsArray().Count,
                    audioError=json["audio"]?["error"]?.ToString(),mediaSessions=json["media"]?["sessions"]?.AsArray().Count,mediaError=json["media"]?["error"]?.ToString(),
                    ramMeasured=json["system"]?["ram"]!=null,gpuMeasured=json["system"]?["gpu"]!=null};
                File.WriteAllText(output,System.Text.Json.JsonSerializer.Serialize(result));return result.ramMeasured?0:2;
            }finally{if(Directory.Exists(temp))Directory.Delete(temp,true);}
        }
        using var single=new Mutex(true,@"Local\NovaLabDeckHost",out var first);
        if(!first){MessageBox.Show("NovaLab Deck läuft bereits. Öffne das violette Symbol im Infobereich.","NovaLab Deck");return 0;}
        try{Application.Run(new HostWindow());return 0;}catch(Exception e){MessageBox.Show(e.Message,"NovaLab Deck");return 1;}
    }
}
public sealed class HostWindow:Form {
    static readonly Color Bg=Color.FromArgb(12,11,22),PanelColor=Color.FromArgb(28,24,46),Ink=Color.FromArgb(239,235,252),Accent=Color.FromArgb(139,92,246);
    readonly string dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NovaLab Deck","Data");
    readonly WindowsEngine engine;Server? server;readonly NotifyIcon tray;bool quitting,closing;
    readonly PictureBox qr=new(){Size=new(216,216),SizeMode=PictureBoxSizeMode.Zoom,Margin=new(8)};
    readonly Label address=LabelText(""),status=LabelText("");
    readonly ComboBox interfaces=new(){Width=180,DropDownStyle=ComboBoxStyle.DropDownList};
    readonly TextBox port=new(){Width=75},obsPort=new(){Width=75},password=new(){Width=240,UseSystemPasswordChar=true};
    readonly ListBox paired=new(){Width=640,Height=170,DisplayMember="Name"};readonly TabControl tabs=new(){Dock=DockStyle.Fill};
    public HostWindow(){
        Text="NovaLab Deck Host";Font=new("Segoe UI",10);BackColor=Bg;ForeColor=Ink;MinimumSize=new(730,590);Size=new(900,710);StartPosition=FormStartPosition.CenterScreen;
        var icon=typeof(HostWindow).Assembly.GetManifestResourceStream("deck.ico");if(icon!=null)Icon=new Icon(icon);
        engine=new(dir,m=>UI(()=>MessageBox.Show(this,m,"Aktion am PC bestätigen",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes),n=>UI(()=>Role(n)));
        var menu=new ContextMenuStrip();menu.Items.Add("Host öffnen",null,(_,_)=>Reveal());menu.Items.Add("Deck öffnen",null,(_,_)=>OpenDeck());menu.Items.Add("Beenden",null,(_,_)=>{quitting=true;Close();});
        tray=new(){Icon=Icon,Text="NovaLab Deck Host",ContextMenuStrip=menu,Visible=true};tray.DoubleClick+=(_,_)=>Reveal();
        var title=new Label{Text="◈   NovaLab Deck   /   Host",Font=new("Segoe UI Semibold",20),Height=70,Padding=new(22,12,0,0),Dock=DockStyle.Top,BackColor=PanelColor};
        Controls.Add(tabs);Controls.Add(title);var dashboard=Tab("Verbindung");
        dashboard.Controls.Add(LabelText("Smartphone koppeln",19));dashboard.Controls.Add(status);dashboard.Controls.Add(qr);dashboard.Controls.Add(address);
        dashboard.Controls.Add(Row(Btn("QR-Code erneuern",Pair),Btn("Deck auf dem PC öffnen",OpenDeck),Btn("In den Infobereich",Hide)));
        var network=Tab("Netzwerk & Geräte");network.Controls.Add(LabelText("Netzwerkzugang",18));interfaces.Items.Add("127.0.0.1");foreach(var a in Server.Addresses().Where(a=>!IPAddress.IsLoopback(a)))interfaces.Items.Add(a.ToString());
        interfaces.SelectedItem=engine.Config.Address;if(interfaces.SelectedIndex<0)interfaces.SelectedIndex=0;port.Text=engine.Config.Port.ToString();
        network.Controls.Add(LabelText("127.0.0.1 erlaubt ausschließlich Zugriff an diesem PC. Für das Handy die WLAN- oder LAN-IP wählen."));
        network.Controls.Add(Row(interfaces,port,Btn("Übernehmen",()=>Safe(Restart))));network.Controls.Add(Row(Btn("HTTPS-Zertifikat (.pfx)",ChooseCertificate),Btn("HTTP auswählen",()=>{engine.Config.Certificate="";engine.Config.CertSecret="";engine.Save();Safe(Restart);})));
        network.Controls.Add(LabelText("Gekoppelte Geräte",16));network.Controls.Add(paired);network.Controls.Add(Row(Btn("Aktualisieren",Devices),Btn("Zugriff entziehen",()=>{if(paired.SelectedItem is Device d){server?.Revoke(d.Id);Devices();}})));
        var apps=Tab("Programme & Hotkeys");apps.Controls.Add(LabelText("Lokale Freigaben",18));apps.Controls.Add(LabelText("Freigegebene Ziele erscheinen im Button-Editor. Hotkeys werden ausschließlich hier am PC definiert."));
        apps.Controls.Add(Row(Btn("Programm / Datei",()=>AddFile(false)),Btn("Ordner",Folder),Btn("Website",Website),Btn("Sound importieren",()=>AddFile(true))));
        apps.Controls.Add(Row(Btn("Hotkey aufnehmen",Hotkey),Btn("Programmliste neu einlesen",()=>Safe(Scan)),Btn("Windows-Soundgeräte",()=>Open("ms-settings:sound"))));
        var options=Tab("OBS & Host");options.Controls.Add(LabelText("OBS WebSocket 5",18));options.Controls.Add(LabelText("OBS → Werkzeuge → WebSocket-Servereinstellungen. Verbindung über diesen PC."));
        obsPort.Text=engine.Config.ObsPort.ToString();password.PlaceholderText="Neues Passwort";options.Controls.Add(Row(obsPort,password,Btn("OBS speichern",()=>{engine.Config.ObsPort=ParsePort(obsPort.Text);if(password.Text.Length>0)engine.Config.ObsSecret=LocalConfig.Protect(password.Text);password.Clear();engine.Save();Safe(Restart);})));
        options.Controls.Add(Btn("OBS-Passwort entfernen",()=>{engine.Config.ObsSecret="";engine.Save();Safe(Restart);}));
        var auto=new CheckBox{Text="Bei Windows-Anmeldung starten",AutoSize=true,Checked=Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run","NovaLabDeck",null)!=null};
        auto.CheckedChanged+=(_,_)=>{using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");if(auto.Checked)key.SetValue("NovaLabDeck","\""+Environment.ProcessPath+"\"");else key.DeleteValue("NovaLabDeck",false);};options.Controls.Add(auto);
        options.Controls.Add(Btn("Soundboard-Ausgang wählen",SoundOutput));options.Controls.Add(Btn("Host beenden",()=>{quitting=true;Close();}));
        tabs.SelectedIndexChanged+=(_,_)=>Devices();Shown+=(_,_)=>Safe(async()=>{await Restart();await Scan();});Resize+=(_,_)=>{if(WindowState==FormWindowState.Minimized)Hide();};
        FormClosing+=async(_,e)=>{if(closing)return;e.Cancel=true;if(!quitting&&e.CloseReason==CloseReason.UserClosing){Hide();return;}closing=true;Enabled=false;if(server!=null)await server.DisposeAsync();engine.Dispose();tray.Dispose();BeginInvoke(Close);};
    }
    static int ParsePort(string s)=>int.TryParse(s,out int p)&&p is >=1024 and <=65535?p:throw new ArgumentException("Port 1024–65535 eingeben");
    void Reveal(){Show();WindowState=FormWindowState.Normal;Activate();}
    async void Safe(Func<Task> f){try{await f();}catch(Exception e){MessageBox.Show(this,e.GetBaseException().Message,"NovaLab Deck");}}
    Task<T> UI<T>(Func<T> f){var t=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);BeginInvoke(()=>{try{Reveal();t.SetResult(f());}catch(Exception ex){t.TrySetException(ex);}});return t.Task;}
    string? Role(string n){using var box=new Form{Text="Gerät koppeln",Size=new(520,205),StartPosition=FormStartPosition.CenterParent,BackColor=Bg,ForeColor=Ink};string? role=null;var flow=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new(12)};flow.Controls.Add(LabelText(n));foreach(var pair in new[]{("Nur steuern","control"),("Auch bearbeiten","edit"),("Ablehnen","")})flow.Controls.Add(Btn(pair.Item1,()=>{role=pair.Item2;box.Close();}));box.Controls.Add(flow);box.ShowDialog(this);return role;}
    static Label LabelText(string s,int size=10)=>new(){Text=s,AutoSize=true,MaximumSize=new(800,0),Margin=new(6,7,6,7),Font=new("Segoe UI",size),ForeColor=Ink};
    Button Btn(string s,Action f){var b=new Button{Text=s,AutoSize=true,Height=38,Padding=new(8,3,8,3),FlatStyle=FlatStyle.Flat,BackColor=PanelColor,ForeColor=Ink};b.FlatAppearance.BorderColor=Accent;b.Click+=(_,_)=>{try{f();}catch(Exception ex){MessageBox.Show(this,ex.Message,"NovaLab Deck");}};return b;}
    static FlowLayoutPanel Row(params Control[] controls){var row=new FlowLayoutPanel{AutoSize=true,WrapContents=true};row.Controls.AddRange(controls);return row;}
    FlowLayoutPanel Tab(string name){var tab=new TabPage(name){BackColor=Bg};var f=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new(20),FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};tab.Controls.Add(f);tabs.TabPages.Add(tab);return f;}
    async Task Restart(){
        var ip=IPAddress.Parse(interfaces.SelectedItem!.ToString()!);var p=ParsePort(port.Text);
        if(!IPAddress.IsLoopback(ip)&&engine.Config.Certificate==""&&(engine.Config.Address!=ip.ToString()||server!=null)&&MessageBox.Show(this,"HTTP im Heimnetz überträgt Steuerbefehle und Sitzungscookies unverschlüsselt. Nur in einem vertrauenswürdigen privaten Netz verwenden. Aktivieren?","LAN aktivieren",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
        var cert=engine.Config.Certificate.Length==0?null:new X509Certificate2(engine.Config.Certificate,LocalConfig.Reveal(engine.Config.CertSecret));if(cert!=null&&!cert.HasPrivateKey)throw new ArgumentException("Privater Zertifikatschlüssel fehlt");
        if(server!=null){await server.DisposeAsync();server=null;}var obs=new Obs(()=>(engine.Config.ObsPort,LocalConfig.Reveal(engine.Config.ObsSecret)));
        var next=new Server(dir,engine,obs,p,ip,cert);try{await next.Start();}catch{await next.DisposeAsync();throw;}server=next;
        engine.Config.Address=ip.ToString();engine.Config.Port=p;engine.Save();status.Text=IPAddress.IsLoopback(ip)?"Host aktiv · Für das Handy unter Netzwerk & Geräte die LAN-IP wählen.":"Host aktiv · "+(cert==null?"HTTP ohne Transportverschlüsselung":"HTTPS")+" · Gleiches WLAN verwenden.";Pair();Devices();
    }
    void Pair(){if(server==null)return;var link=server.PairLink();using var gen=new QRCodeGenerator();using var data=gen.CreateQrCode(link,QRCodeGenerator.ECCLevel.M);using var code=new PngByteQRCode(data);using var stream=new MemoryStream(code.GetGraphic(7));using var bitmap=new Bitmap(stream);var old=qr.Image;qr.Image=new Bitmap(bitmap);old?.Dispose();address.Text=server.BaseUrl+"   ·   QR-Code 2 Minuten gültig";}
    async Task Scan(){await Task.Run(engine.Scan);status.Text+="   ·   "+engine.Catalog().Count+" Ziele";}
    static void Open(string url)=>Process.Start(new ProcessStartInfo(url){UseShellExecute=true})?.Dispose();
    void OpenDeck(){if(server!=null)Open(server.HostLink());}
    void Devices(){paired.DataSource=server?.Devices.ToList();}
    static string? Input(string title,string initial="",bool secret=false){using var f=new Form{Text=title,Size=new(510,140),StartPosition=FormStartPosition.CenterParent};var edit=new TextBox{Dock=DockStyle.Top,Text=initial,UseSystemPasswordChar=secret};var ok=new Button{Dock=DockStyle.Bottom,Text="Übernehmen",DialogResult=DialogResult.OK};f.Controls.Add(edit);f.Controls.Add(ok);f.AcceptButton=ok;return f.ShowDialog()==DialogResult.OK?edit.Text:null;}
    void ChooseCertificate(){using var d=new OpenFileDialog{Filter="Zertifikat|*.pfx;*.p12"};if(d.ShowDialog()!=DialogResult.OK)return;var pw=Input("PFX-Passwort","",true);if(pw==null)return;using var c=new X509Certificate2(d.FileName,pw);if(!c.HasPrivateKey)throw new ArgumentException("Privater Schlüssel fehlt");engine.Config.Certificate=d.FileName;engine.Config.CertSecret=LocalConfig.Protect(pw);engine.Save();Safe(Restart);}
    void AddFile(bool sound){using var d=new OpenFileDialog{Filter=sound?"Sounddateien|*.mp3;*.wav;*.ogg":"Programme / Dateien|*.*"};if(d.ShowDialog()!=DialogResult.OK)return;
        if(sound){
            var id=Model.Id();var category=Input("Sound-Kategorie","Soundboard");if(category==null)return;
            var files=Path.Combine(dir,"sounds");Directory.CreateDirectory(files);var dest=Path.Combine(files,id+Path.GetExtension(d.FileName).ToLowerInvariant());
            File.Copy(d.FileName,dest);try{engine.Add(new(id,Path.GetFileNameWithoutExtension(d.FileName),"sound",dest,Category:category));}catch{File.Delete(dest);throw;}Notify();return;
        }
        var exe=d.FileName.EndsWith(".exe",StringComparison.OrdinalIgnoreCase);var args=exe?Input("Optionale Startparameter"):"";if(args==null)return;
        engine.Add(new(Model.Id(),Path.GetFileNameWithoutExtension(d.FileName),sound?"sound":exe?"app":"file",d.FileName,args,exe?Path.GetFileNameWithoutExtension(d.FileName):""));Notify();}
    void Folder(){using var d=new FolderBrowserDialog();if(d.ShowDialog()==DialogResult.OK){engine.Add(new(Model.Id(),Path.GetFileName(d.SelectedPath),"folder",d.SelectedPath));Notify();}}
    void Website(){var s=Input("Website-Adresse","https://");if(s==null)return;if(!Uri.TryCreate(s,UriKind.Absolute,out var u)||u.Scheme is not ("https" or "http"))throw new ArgumentException("HTTP(S)-Adresse erforderlich");engine.Add(new(Model.Id(),u.Host,"web",u.AbsoluteUri));Notify();}
    void Hotkey(){var name=Input("Name der Aktion, z. B. Discord Mute");if(string.IsNullOrWhiteSpace(name))return;var target=Input("Zielprozess ohne .exe (leer für global)","");if(target==null)return;
        using var capture=new Form{Text="Tastenkombination drücken",Size=new(430,135),KeyPreview=true,StartPosition=FormStartPosition.CenterParent};capture.Controls.Add(new Label{Text="Strg, Alt oder Umschalt halten und eine Taste drücken.",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter});
        var keys=new List<int>();capture.KeyDown+=(_,e)=>{if(e.KeyCode is Keys.ControlKey or Keys.Menu or Keys.ShiftKey)return;if(e.Control)keys.Add(17);if(e.Alt)keys.Add(18);if(e.Shift)keys.Add(16);keys.Add(e.KeyValue);e.SuppressKeyPress=true;capture.DialogResult=DialogResult.OK;};if(capture.ShowDialog(this)!=DialogResult.OK)return;
        var hold=Input("Haltedauer in ms (35–5000, z. B. für Push-to-Talk)","60");if(!int.TryParse(hold,out var ms))return;engine.Add(new(Model.Id(),name,"hotkey",string.Join('+',keys),Math.Clamp(ms,35,5000).ToString(),target));Notify();}
    void SoundOutput(){using var e=new NAudio.CoreAudioApi.MMDeviceEnumerator();var choices=new Dictionary<string,string>{{"Windows-Standard",""}};foreach(var d in e.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render,NAudio.CoreAudioApi.DeviceState.Active)){using(d)choices[d.FriendlyName]=d.ID;}
        using var box=new Form{Text="Soundboard-Ausgang",Size=new(500,150)};var list=new ComboBox{Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList,DataSource=choices.Keys.ToList()};var save=new Button{Dock=DockStyle.Bottom,Text="Speichern",DialogResult=DialogResult.OK};box.Controls.Add(list);box.Controls.Add(save);if(box.ShowDialog()==DialogResult.OK){engine.Config.Output=choices[list.SelectedItem!.ToString()!];engine.Save();}}
    void Notify(){MessageBox.Show(this,"Gespeichert. Mit „Ziele aktualisieren“ im Deck laden.","NovaLab Deck");}
}
