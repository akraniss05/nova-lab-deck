import {editor,layoutManager,iconLibrary} from './editor.js';
import {hostView,audioView,mediaView,systemView,appsView,obsView,settingsView,soundView} from './views.js';
export const S={board:null,role:'',state:null,obs:null,catalog:[],actions:[],assets:[],view:'deck',page:0,edit:false,ws:null,connected:false,clip:null,timer:0,history:[],lang:'de'};
export const tr=(de,en)=>S.lang==='en'?en:de;
export const $=id=>document.getElementById(id);
export function el(tag,props={},...children){const node=document.createElement(tag);for(const [k,v] of Object.entries(props)){if(v==null)continue;if(k.startsWith('on'))node.addEventListener(k.slice(2),v);else if(k==='class')node.className=v;else if(k==='aria')node.setAttribute('aria-label',v);else if(k==='style')Object.assign(node.style,v);else node[k]=v;}for(const c of children.flat(Infinity)){if(c!=null)node.append(c instanceof Node?c:document.createTextNode(String(c)));}return node;}
export const txt=(s,cls='sub')=>el('span',{class:cls},s);
export const btn=(text,fn,cls='',disabled=false)=>el('button',{type:'button',class:cls,disabled,onclick:async e=>{try{await fn(e);}catch(err){toast(err.message);}}},text);
export const panel=(...children)=>el('section',{class:'panel'},...children);
export const row=(...c)=>el('div',{class:'actions'},...c);
export const field=(name,input)=>el('label',{class:'field'},name,input);
export function select(options,value,fn){let s=el('select');for(const opt of options){const [id,name]=Array.isArray(opt)?opt:[opt,opt];s.append(el('option',{value:id},name));}s.value=value;if(fn)s.onchange=()=>Promise.resolve(fn(s.value)).catch(e=>toast(e.message));return s;}
export function input(value,fn,other={}){return el('input',{value:value??'',...other,oninput:e=>fn(e.target.value)});}
export function heading(name,kicker='',...actions){return el('div',{class:'heading'},el('div',{},kicker?txt(kicker,'eyebrow'):null,el('h1',{},name)),actions.length?el('div',{class:'toolbar'},...actions):null);}
export const profile=()=>S.board.profiles.find(p=>p.id===S.board.active);
export const page=()=>profile()?.pages[S.page]||profile()?.pages[0];
export const asset=id=>id?.startsWith('data:image/')?id:'/'+(S.assets.find(a=>a.id===id)?.file||'logo.svg');
export function toast(s){if(!s)return;const t=$('toast');t.textContent=typeof s==='string'?s:JSON.stringify(s);t.classList.add('show');clearTimeout(t.hide);t.hide=setTimeout(()=>t.classList.remove('show'),4100);}
export function modal(title,...body){const d=$('modal');$('dialogContent').replaceChildren(el('div',{class:'dialoghead'},el('h2',{},title),btn('×',()=>d.close())),...body);if(!d.open)d.showModal();}
export async function api(op,data={}){const r=await fetch('/cmd',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({op,data})});let j=await r.json();if(!r.ok)throw Error(j.error||r.statusText);return j;}
export async function save(board){let result=await api('save',board);if(!S.board||S.board.revision<=result.revision){S.board={...board,revision:result.revision};apply();draw();}return result;}
export async function mutate(fn){const copy=structuredClone(S.board);fn(copy);return save(copy);}
export function run(kind,target='',value=''){return api('run',{kind,target,value:String(value)}).then(r=>{if(S.board?.settings?.notifications!==false)toast(r);return r;});}
export async function reloadCatalog(){const info=await api('info');S.catalog=info.catalog;S.board=info.board;draw();}
export function go(view){S.view=view;S.edit=false;$('rail').classList.remove('open');draw();}
const routeNames=()=>({...S.role==='host'?{host:['Dashboard','panels-top-left']}:{},deck:['Deck','layers'],audio:['Audio-Mixer','sliders-horizontal'],media:['Now Playing','disc-3'],obs:['OBS Studio','radio'],sounds:['Soundboard','pads'],apps:[tr('Programme','Applications'),'app-window'],system:['Monitoring','tower'],settings:[tr('Einstellungen','Settings'),'settings']});
function apply(){const pref=S.board.settings;S.lang=pref.language;document.documentElement.lang=S.lang;$('toast').dataset.position=pref.noticePosition||'bottom';document.documentElement.dataset.theme=pref.theme==='system'?(matchMedia('(prefers-color-scheme:light)').matches?'light':'dark'):pref.theme;document.documentElement.dataset.motion=pref.motion?'on':'off';document.documentElement.style.setProperty('--motion',Math.round(pref.motion*2.5)+'ms');}
matchMedia('(prefers-color-scheme:light)').onchange=()=>{if(S.board)apply();};
export function keyArt(k,active=false){const style=(active&&k.activeStyle)||k.style||{};const button=el('button',{type:'button',class:'key '+(style.align||'bottom')+(active?' active':'')});button.dataset.motion=style.motion||'pulse';
 for(const [css,v] of Object.entries({'--edge':style.accent,'--end':style.end,'--fg':style.text,'--rad':style.radius!=null?style.radius+'px':null,'--border':style.border!=null?style.border+'px':null,'--fs':style.size?style.size+'px':null,'--art':style.icon?style.icon+'%':null,'--glow':style.glow!=null?style.glow+'%':null,'--op':style.opacity?style.opacity/100:null}))if(v!=null)button.style.setProperty(css,v);
 if(style.background)button.append(el('img',{class:'bgArt',src:asset(style.background),alt:''}));
 const icon=(active&&k.activeIcon)||k.icon;if(icon)button.append(el('img',{class:'art',src:asset(icon),alt:'',draggable:false}));button.append(el('span',{class:'keyname'},k.name));button.title=k.description||k.name;return button;}
function active(k){const a=k.action||{},o=S.obs?.data||{};switch(a.kind){case 'obs.startRecord':case 'obs.stopRecord':case 'obs.record':return o.record?.outputActive;case 'obs.pause':return o.record?.outputPaused;case 'obs.startStream':case 'obs.stopStream':case 'obs.stream':return o.stream?.outputActive;case 'obs.camera':return o.camera?.outputActive;case 'obs.replay':return o.replay?.outputActive;case 'obs.studio':return o.studio?.studioModeEnabled;case 'obs.scene':return !!S.obs?.connected&&!!o.scene?.currentProgramSceneName&&o.scene.currentProgramSceneName===(a.target.startsWith('#')?o.scene?.scenes?.[Number(a.target.slice(1))-1]?.sceneName:a.target);case 'audio.toggle':return S.state?.audio?.channels?.find(x=>x.id===a.target)?.muted;default:return false;}}
function reason(k){const a=k.action;if(!a||a.kind==='none')return tr('Slot belegen','Configure slot');if(a.kind.startsWith('obs.')&&!S.obs?.connected)return 'OBS offline';
 if(a.kind==='launch'&&a.target.startsWith('find:')&&!S.catalog.some(t=>t.name.toLowerCase().includes(a.target.slice(5).toLowerCase())))return tr('Startziel fehlt','Launch target missing');
 if(['audio.toggle','audio.delta'].includes(a.kind)&&!S.state?.audio?.channels?.some(c=>c.id.toLowerCase()===a.target.toLowerCase()))return tr('Keine Audio-Sitzung','No audio session');return '';}
let holdTimer,clickTimer,skipClick=false,dragIndex=null,dragPage='',heldSocket=null;
function releasePTT(){if(heldSocket?.readyState===WebSocket.OPEN)heldSocket.send(JSON.stringify({op:'release',data:{}}));heldSocket=null;}
window.addEventListener('blur',releasePTT);document.addEventListener('visibilitychange',()=>{if(document.hidden)releasePTT();});
function pageDeck(){const p=profile();if(!p.pages[S.page])S.page=0;const current=page();const chooser=select(S.board.profiles.map(p=>[p.id,p.name]),p.id,async id=>{await run('profile',id);S.page=0;});
 const top=heading(current.name,p.name+' / '+tr('Steuerdeck','Control deck'),chooser,S.role!=='control'?btn(S.edit?tr('Fertig','Done'):tr('Bearbeiten','Edit'),()=>{S.edit=!S.edit;draw();},S.edit?'primary':''):null);
 const grid=el('div',{class:'deck'+(S.edit?' editMode':'')});current.keys.forEach((k,i)=>{
 const empty=!k.name&&!k.action;const on=!!active(k);let key=empty?el('button',{class:'key empty',aria:tr('Freier Slot','Empty slot')+' '+(i+1)},'+'):keyArt(k,on);
 const unavailable=reason(k);if(!S.edit&&!empty&&unavailable){key.disabled=true;key.title=unavailable;key.append(txt(unavailable,'flag'));}
 if(on){let state=k.action.kind==='obs.stream'?'LIVE':k.action.kind==='audio.toggle'?'MUTE':'ON';key.classList.add('live');key.append(txt(state,'flag'));}
 key.dataset.slot=i;const execute=gesture=>{if(!S.connected)return;S.ws.send(JSON.stringify({op:'key',data:{slot:i,page:current.id,revision:S.board.revision,gesture}}));};
 const edit=()=>editor(i);key.onclick=()=>{if(skipClick){skipClick=false;return;}if(S.edit||empty){if(S.role!=='control')edit();return;}
 if(k.double){if(clickTimer){clearTimeout(clickTimer);clickTimer=null;execute('double');}else clickTimer=setTimeout(()=>{clickTimer=null;execute('tap');},270);}else execute('tap');};
 key.onpointerdown=e=>{skipClick=false;
 if(!S.edit&&k.action?.kind==='hotkey'&&k.action.value==='ptt'&&S.connected){skipClick=true;key.setPointerCapture(e.pointerId);heldSocket=S.ws;heldSocket.send(JSON.stringify({op:'press',data:{revision:S.board.revision,page:current.id,slot:i}}));return;}
 if(!S.edit&&k.hold){let repeat=()=>{skipClick=true;execute('hold');if(k.hold.kind==='audio.delta')holdTimer=setTimeout(repeat,180);};holdTimer=setTimeout(repeat,460);}};
 key.onpointercancel=key.onpointerup=()=>{clearTimeout(holdTimer);if(heldSocket)releasePTT();};key.onpointerleave=()=>clearTimeout(holdTimer);
 key.draggable=S.edit;key.ondragstart=()=>{dragIndex=i;dragPage=current.id;};key.ondragover=e=>e.preventDefault();key.ondrop=async e=>{e.preventDefault();if(S.edit&&dragIndex!=null&&dragPage===current.id){const from=dragIndex;dragIndex=null;try{await mutate(b=>{const keys=b.profiles.find(x=>x.id===p.id).pages.find(x=>x.id===current.id).keys;[keys[i],keys[from]]=[keys[from],keys[i]];});}catch(err){toast(err.message);}}};grid.append(key);});
 let touch;grid.ontouchstart=e=>{touch=e.touches[0].clientX;};grid.ontouchend=e=>{const diff=e.changedTouches[0].clientX-touch;if(!S.edit&&Math.abs(diff)>65){clearTimeout(holdTimer);skipClick=true;changePage(diff<0?1:-1);}};
 const switcher=select(p.pages.map((x,i)=>[String(i),x.name]),String(S.page),v=>{S.page=+v;draw();});
 const stats=S.state?.system||{};return [top,el('section',{class:'deckwrap'},grid,el('div',{class:'pager'},btn('‹',()=>changePage(-1),'',S.page===0),switcher,txt((S.page+1)+' / '+p.pages.length,'tiny'),btn('›',()=>changePage(1),'',S.page===p.pages.length-1)),el('div',{class:'footstats'},...['CPU','GPU','RAM'].map((s,i)=>el('span',{},s+' ',el('b',{},[stats.cpu,stats.gpu?.load,stats.ram][i]==null?'—':[stats.cpu,stats.gpu?.load,stats.ram][i]+' %'))),txt('','timer'))),S.edit?row(btn(tr('Seiten & Profile','Pages & profiles'),layoutManager),btn(tr('Icon-Bibliothek','Icon library'),()=>iconLibrary())):null];}
function changePage(d){S.page=Math.max(0,Math.min(profile().pages.length-1,S.page+d));draw();}
export function draw(){if(!S.board||!S.connected)return;apply();const routes=routeNames();$('rail').replaceChildren(txt('Workspace','rail-label'),...Object.entries(routes).map(([id,[name,icon]])=>{const b=btn('',()=>go(id),'navrow'+(id===S.view?' selected':''));b.append(el('img',{src:asset(icon),alt:''}),name);return b;}),txt('NovaLab Studios','railfoot'));
 const views={host:hostView,deck:pageDeck,audio:audioView,media:mediaView,obs:obsView,system:systemView,sounds:soundView,apps:appsView,settings:settingsView};
 $('main').replaceChildren(...(views[S.view]||pageDeck)().filter(Boolean));updateTimer();}
function updateTimer(){const t=document.querySelector('.timer');if(t&&S.timer){const sec=Math.max(0,Math.ceil((S.timer-Date.now())/1000));t.textContent=Math.floor(sec/60)+':'+String(sec%60).padStart(2,'0');if(!sec)S.timer=0;}}
setInterval(updateTimer,500);
function pairUI(msg=''){S.connected=false;$('status').className='';$('status').textContent='Offline';$('rail').replaceChildren();const code=new URLSearchParams(location.hash.slice(1));const tok=code.get('pair')||code.get('host')||sessionStorage.getItem('pending-pair')||'';if(tok)sessionStorage.setItem('pending-pair',tok);history.replaceState(null,'',location.pathname);
 const name=input(localStorage.getItem('name')||(/iPhone/.test(navigator.userAgent)?'iPhone':'Deck Remote'),()=>{}, {maxLength:60});
 const action=btn(tr('Mit PC koppeln','Pair with PC'),async()=>{action.disabled=true;try{localStorage.setItem('name',name.value);const r=await fetch('/pair',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({code:tok,name:name.value})});if(!r.ok)throw Error((await r.json()).error);sessionStorage.removeItem('pending-pair');connect();}catch(e){toast(e.message);action.disabled=false;}},'primary',!tok);
 $('main').replaceChildren(el('div',{class:'panel paircard'},el('img',{src:'/logo.svg',alt:''}),el('h1',{},tr('Deck verbinden','Connect deck')),el('p',{},msg||tr('Scanne den QR-Code im Windows-Host. Bestätige danach dieses Gerät am PC.','Scan the QR code in the Windows host, then approve this device on your PC.')),field(tr('Gerätename','Device name'),name),action,location.protocol==='http:'?txt(tr('HTTP · Verbindung ohne Transportverschlüsselung','HTTP · Unencrypted connection')):null));if(code.get('host'))action.click();}
let attempt=0,retry,received=false;
function connect(){clearTimeout(retry);const ws=new WebSocket((location.protocol==='https:'?'wss://':'ws://')+location.host+'/ws');S.ws=ws;
 ws.onmessage=e=>{if(S.ws!==ws)return;const m=JSON.parse(e.data);if(m.type==='hello'){if(!received&&m.role==='host'&&innerWidth>840)S.view='host';received=true;attempt=0;Object.assign(S,{board:m.board,role:m.role,state:m.state,obs:m.obs,catalog:m.catalog,actions:m.actions,connected:true});apply();$('status').className='online';$('status').textContent=tr('Verbunden','Connected');draw();}
 else if(m.type==='board'){if(!S.board||m.board.revision>=S.board.revision){S.board=m.board;draw();}}
 else if(m.type==='state'){S.state=m.state;S.obs=m.obs;S.history.push(m.state?.system?.cpu??null);if(S.history.length>50)S.history.shift();if(!document.querySelector('dialog[open]')&&!pointerHeld&&!['INPUT','SELECT','TEXTAREA'].includes(document.activeElement?.tagName)&&['deck','audio','media','system','obs'].includes(S.view))draw();}
 else if(m.type==='result'){if(S.board?.settings?.notifications!==false)toast(m.result);}else if(m.type==='error')toast(m.error);else if(m.type==='navigate'){const s=m.step;if(s.kind==='view')go(s.target);if(s.kind==='page'){const i=profile().pages.findIndex(p=>p.id===s.target);if(i>=0){S.page=i;go('deck');}}if(s.kind==='timer'){S.timer=Date.now()+Math.max(0,Number(s.value))*1000;go('deck');}}};
 ws.onclose=()=>{if(S.ws!==ws)return;const was=received;S.connected=false;clearTimeout(holdTimer);if($('modal').open)$('modal').close();pairUI(was?tr('Verbindung unterbrochen. Verbindung zum Host wird erneut versucht.','Disconnected. Reconnecting to host.'):undefined);if(was)retry=setTimeout(connect,Math.min(10000,700*2**Math.min(5,attempt++)));};}
let pointerHeld=false;document.addEventListener('pointerdown',()=>pointerHeld=true);for(const ev of ['pointerup','pointercancel'])document.addEventListener(ev,()=>pointerHeld=false);
$('menu').onclick=()=>$('rail').classList.toggle('open');
S.assets=await fetch('/ASSET_MANIFEST.json').then(r=>r.json());
if(location.hash.includes('pair=')||location.hash.includes('host='))pairUI();else connect();
if('serviceWorker'in navigator&&window.isSecureContext)navigator.serviceWorker.register('/sw.js').catch(()=>{});
