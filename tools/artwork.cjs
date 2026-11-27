// Rebuilds the vector collection from the pinned Lucide icon geometry.
const fs = require('node:fs');
const path = require('node:path');
const lucide = require(process.env.LUCIDE_MODULE || 'lucide');
const root = path.join(__dirname,'..');
const dst = path.join(root,'Web','assets');
const catalog = {
 Streaming: 'radio tower-control video video-off airplay cast clapperboard camera aperture film focus frame images image-play picture-in-picture panels-top-left gallery-horizontal-end gallery-vertical-end layers layers-plus monitor-play monitor-stop projector scan-eye screen-share tv-minimal-play webcam circle-stop circle-pause disc-2 rewind switch-camera panorama square-play subtitles speech',
 Audio: 'mic-vocal mic-off audio-lines audio-waveform volume volume-1 volume-2 volume-x volume-off headphones headphone-off speaker ear ear-off sliders-horizontal sliders-vertical list-music podcast radio-tower cable audio-outputs',
 Musik: 'music music-2 music-3 music-4 disc disc-3 disc-album play pause square skip-forward skip-back fast-forward shuffle repeat repeat-1 album list-video piano guitar drum boom-box turntable',
 Gaming: 'gamepad gamepad-2 joystick rocket swords crosshair target bomb trophy crown dice-5 boxes pickaxe castle sword shield-ellipsis car-front goal volleyball orbit flame ghost gem tent-tree mountain-snow',
 Kommunikation: 'messages-square message-circle message-square-text message-square-more phone phone-call phone-off send mail headset video-icon contact users megaphone bell-ring presentation',
 Entwicklung: 'code code-xml terminal square-terminal braces brackets git-branch git-fork git-pull-request github-icon box panels-left-bottom bug database binary container file-json file-code-2 construction cpu circuit-board wrench network',
 Windows: 'folders folder-open files file-search hard-drive monitor-cog settings clipboard clipboard-list monitor memory-stick ethernet-port lock-keyhole rotate-cw power power-off bluetooth wifi app-window maximize-2 minimize-2 x-square activity gauge thermometer chart-no-axes-combined server usb printer keyboard mouse laptop globe',
 Automation: 'workflow timer timer-reset alarm-clock hourglass watch clock-3 list-ordered copy-plus split merge repeat-2 route folder-tree folder-plus panel-top panel-left key-round toggle-right list-checks zap scan-line fingerprint shield-check archive download upload save history undo-2 redo-2',
 Browser: 'chrome compass globe-2 earth link external-link bookmark search navigation panels-top-left book-open newspaper rss binoculars panel-top-open'
};
const renders=['stream','record','mic','cam','sunset','city','coffee','chat','tiktok','discord','spotify','quieter','louder','pads','macro','tower'];
const names=['Stream starten','Aufnahme','Studio-Mikrofon','Webcam','Szene · Berge','Szene · Skyline','BRB','Chat','TikTok LIVE Studio','Discord','Spotify','Lautstärke −','Lautstärke +','Soundboard','Multi-Aktion','Windows Host'];
const manifest=renders.map((id,i)=>({id,name:names[i],category:i<9?'Streaming':i===9?'Kommunikation':i===10?'Musik':i<14?'Audio':i===14?'Automation':'Windows',file:'assets/'+id+'.webp',master:'Artwork/Masters/'+id+'.webp',origin:'NovaLab · generated 3D artwork'}));
const palette=['#ad83ff','#ff59d2','#40cfff','#8c95ff','#49e7c2','#ff957b'];
for(const [category,items] of Object.entries(catalog)){
 for(const id of items.split(' ')){
  if(manifest.some(x=>x.id===id))continue;
  const key=id.split('-').map(x=>x[0].toUpperCase()+x.slice(1)).join('');
  const shape=lucide[key];if(!Array.isArray(shape))continue;
  const paths=shape.map(([tag,attrs])=>'<'+tag+' '+Object.entries(attrs).map(([k,v])=>k+'="'+v+'"').join(' ')+'/>').join('');
  const c=palette[manifest.length%palette.length];
  const svg='<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 100 100"><defs><linearGradient id="g" x2=".8" y2="1" gradientUnits="objectBoundingBox"><stop stop-color="#fff"/><stop offset=".32" stop-color="'+c+'"/><stop offset=".62" stop-color="#9971fb"/><stop offset="1" stop-color="#3b2575"/></linearGradient><radialGradient id="h"><stop stop-color="'+c+'" stop-opacity=".26"/><stop offset="1" stop-color="'+c+'" stop-opacity="0"/></radialGradient><g id="p" fill="none" stroke-linecap="round" stroke-linejoin="round">'+paths+'</g></defs><ellipse cx="50" cy="66" rx="49" ry="31" fill="url(#h)"/><g transform="translate(14 12) scale(3)"><use href="#p" transform="translate(.65 1.65)" stroke="#291039" stroke-width="3.4"/><use href="#p" transform="translate(.3 .9)" stroke="'+c+'" stroke-width="2.9"/><use href="#p" stroke="#37254d" stroke-width="2.9"/><use href="#p" stroke="url(#g)" stroke-width="2.3"/></g></svg>';
  fs.writeFileSync(path.join(dst,id+'.svg'),svg);
  manifest.push({id,name:id.replaceAll('-',' '),category,file:'assets/'+id+'.svg',origin:'Lucide geometry · NovaLab layered vector'});
 }
}
fs.writeFileSync(path.join(root,'ASSET_MANIFEST.json'),JSON.stringify(manifest,null,1)+'\n');
console.log(manifest.length+' distinct icons');
