import { MapRenderer, loadImages } from '../src/lib/map-renderer.ts';
import { project } from '../src/lib/map-geometry.ts';
import type { Snapshot } from '../src/generated/contracts.ts';

// Fixed renderer stress fixture, not a playable world or a capacity prediction.
// 128x128 ground + 4096 tracks + 1024 four-layer buildings + 128 three-car trains
// + 16 stations + 2 decorative sprites = 24978 candidate draw items.
const read: Snapshot = {
  type: 'snapshot', worldEpoch: '1', revision: '0', tick: '0', paused: true, cash: '1000000',
  map: { width: 128, height: 128, levels: 16 },
  tracks: Array.from({ length: 4096 }, (_, i) => ({ position: { x: i % 128, y: Math.floor(i / 128) * 4, z: 1 }, shape: 'eastWest', revision: '0' })),
  buildings: Array.from({ length: 1024 }, (_, i) => ({ id: `building-${i}`, definitionId: 'profile-condo', position: { x: (i % 32) * 4, y: Math.floor(i / 32) * 4 + 2, z: 0 }, height: 4 })),
  trains: Array.from({ length: 128 }, (_, i) => ({ id: `train-${i}`, revision: '0', name: 'Profile', cars: 3, controller: 'automatic', direction: 'east', state: 'moving',
    position: { x: 3 + (i % 16) * 7, y: Math.floor(i / 16) * 16, z: 1 },
    carPositions: [0, 1, 2].map(offset => ({ x: 3 + (i % 16) * 7 - offset, y: Math.floor(i / 16) * 16, z: 1 })) as [{x:number;y:number;z:number}, ...{x:number;y:number;z:number}[]] })),
  stations: Array.from({ length: 16 }, (_, i) => ({ id: `station-${i}`, name: 'Profile', revision: '0', position: { x: i * 8, y: 64, z: 1 } })),
};
const canvas = document.querySelector('canvas')!;
const renderer = new MapRenderer(canvas, await loadImages());
renderer.setRead(read);
const options = { heightCut: 15, level: 1, night: false, winter: false, recolor: false, shape: 'buffer' as const, remove: false };
function distribution(values: number[]) {
  const sorted = [...values].sort((a,b) => a-b);
  const p = (q:number) => sorted[Math.min(sorted.length-1, Math.ceil(q*sorted.length)-1)];
  return { count: sorted.length, p50: p(.5), p95: p(.95), p99: p(.99), max: sorted.at(-1) };
}
Object.assign(window, { async profileRenderer() {
  const paints:number[]=[], frames:number[]=[], feedback:number[]=[], updates:number[]=[];
  let previous=0;
  const width=1920, height=1080, camera={...project({x:64,y:64,z:0}),zoom:1};
  // Warm caches and browser JIT before collecting 360 continuous moving-camera frames.
  for(let i=0;i<420;i++) {
    const scheduled=performance.now();
    await new Promise<void>(resolve=>requestAnimationFrame(now=>{
      camera.x+=Math.sin(i/20)*2;camera.y+=Math.cos(i/25);
      const start=performance.now();
      if(i%6===0){const begin=performance.now();renderer.setRead(read);if(i>=60)updates.push(performance.now()-begin);}
      renderer.draw(camera,width,height,devicePixelRatio,{...options,preview:{x:64+i%12,y:64,z:1}});
      if(i>=60){paints.push(performance.now()-start);frames.push(now-previous);feedback.push(performance.now()-scheduled);}
      previous=now;resolve();
    }));
  }
  return { scene:{ map:'128x128',ground:16384,tracks:4096,buildings:1024,buildingSprites:4096,trains:128,trainSprites:384,stations:16,decorativeSprites:2,candidateDrawItems:24978 },
    viewport:{width,height,dpr:devicePixelRatio}, frames:distribution(frames),paintMs:distribution(paints),
    scheduledFeedbackMs:distribution(feedback),setReadMs:distribution(updates),
    fps:1000/(frames.reduce((a,b)=>a+b,0)/frames.length),drawn:Number(canvas.dataset.drawn),culled:Number(canvas.dataset.culled),
    note:'Continuous camera/preview mutations at animation-frame boundaries; scheduling-to-draw completion excludes physical input/display latency. Headless Chromium.' };
}});
