// Control requests always use the network. Only immutable artwork is cached.
const cacheName='nova-art-v1';
self.addEventListener('install',()=>self.skipWaiting());
self.addEventListener('activate',e=>e.waitUntil(self.clients.claim()));
self.addEventListener('fetch',event=>{
 const url=new URL(event.request.url);
 if(event.request.method==='GET'&&url.origin===location.origin&&url.pathname.startsWith('/assets/')){
  event.respondWith(caches.open(cacheName).then(async c=>{const hit=await c.match(event.request);if(hit)return hit;const r=await fetch(event.request);if(r.ok)await c.put(event.request,r.clone());return r;}));
 }
});
