"""Add optional brand identifiers from simple-icons 13.21.0. Run after artwork.cjs."""
import json, sys
import xml.etree.ElementTree as ET
from pathlib import Path
root = Path(__file__).resolve().parents[1]
source = Path(sys.argv[1])
meta = json.loads((source/'_data/simple-icons.json').read_text())['icons']
manifest = root/'ASSET_MANIFEST.json'
items = [x for x in json.loads(manifest.read_text()) if not x['id'].startswith('brand-')]
groups = {
 'Streaming':'obsstudio twitch youtube youtubestudio tiktok',
 'Musik':'spotify applemusic youtubemusic vlcmediaplayer',
 'Gaming':'steam epicgames roblox valorant counterstrike rockstargames fivem',
 'Kommunikation':'discord teamspeak whatsapp telegram zoom',
 'Entwicklung':'github blender robloxstudio python gnubash vscodium',
 'Browser':'googlechrome firefoxbrowser brave'}
for cat, slugs in groups.items():
 for slug in slugs.split():
    tree=ET.parse(source/'icons'/f'{slug}.svg').getroot()
    name=tree.find('{*}title').text
    info=next((v for v in meta if v['title']==name),{})
    color='#'+info.get('hex','D5CDE4')
    if int(color[1:],16)<0x151515: color='#ffffff'
    path=tree.find('{*}path').attrib['d']
    svg=f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="512" height="512"><defs><linearGradient id="e" x2=".9" y2="1"><stop stop-color="#d7beff"/><stop offset=".4" stop-color="#483364"/><stop offset="1" stop-color="#aa61de"/></linearGradient><radialGradient id="h" cy=".25"><stop stop-color="#3c304e"/><stop offset="1" stop-color="#100b1c"/></radialGradient></defs><ellipse cx="50" cy="79" rx="35" ry="7" fill="#190d2a"/><rect x="12" y="11" width="76" height="75" rx="22" fill="#22182f" stroke="url(#e)" stroke-width="2"/><rect x="12" y="8" width="76" height="73" rx="22" fill="url(#h)" stroke="url(#e)" stroke-width="2"/><path d="{path}" transform="translate(26 20) scale(2)" fill="{color}"/></svg>'
    file=f'assets/brand-{slug}.svg'
    (root/'Web'/file).write_text(svg)
    items.append(dict(id='brand-'+slug,name=name,category=cat,file=file,origin='Simple Icons 13.21.0 · NovaLab frame',source=info.get('source',''),guidelines=info.get('guidelines',''),license=info.get('license',{})))
manifest.write_text(json.dumps(items,ensure_ascii=False,indent=1)+'\n')
print(len(items),'assets')
