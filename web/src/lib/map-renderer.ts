import content from '../generated/sprites.json' with { type: 'json' };
import type { Position, Track } from '../generated/contracts.ts';
import type { ReadState } from './session-client.ts';
import { opaqueAt, ports, project, toScreen, visible, type Camera, type Point } from './map-geometry.ts';
export type SpriteKey = keyof typeof content.sprites;
export type Selection = { kind: 'train' | 'station' | 'building' | 'track'; id: string; position: Position };
export interface MapOptions { heightCut: number; level: number; night: boolean; winter: boolean; recolor: boolean; selection?: Selection; preview?: Position; shape: Track['shape']; remove: boolean; }
interface Item { position: Position; kind: Selection['kind'] | 'sample'; id: string; sprite?: SpriteKey; shape?: Track['shape']; cut?: boolean; }
export async function loadImages() {
  const images = new Map<string, HTMLImageElement>();
  await Promise.all(Object.values(content.sprites).flatMap(sprite => [sprite.image,sprite.nightImage,sprite.tintImage]).map(async path => {
    const image = new Image(); image.src=path; await image.decode(); images.set(path,image);
  }));
  return images;
}
export class MapRenderer {
  private items: Item[]=[];
  private hits: { item: Item; x: number; y: number }[]=[];
  private read?: ReadState;
  private buildings = new Map<string,ReadState['buildings'][number]>();
  constructor(private canvas: HTMLCanvasElement, private images: Map<string, HTMLImageElement>) {}
  setRead(read: ReadState) {
    this.read=read;
    this.buildings=new Map(read.buildings.map(building=>[building.id,building]));
    this.items = [
      ...read.tracks.map(track => ({ position: track.position, kind:'track' as const,id:`${track.position.x},${track.position.y},${track.position.z}`,shape:track.shape })),
      ...read.stations.map(station => ({position:station.position,kind:'station' as const,id:station.id})),
      ...read.trains.flatMap(train => train.carPositions.map((position,index,body) => {
        // Each follower faces its next car, including around corners.
        const next=body[Math.max(0,index-1)];
        const direction=index===0 ? train.direction : next.x>position.x?'east':next.x<position.x?'west':next.y>position.y?'south':'north';
        return {position,kind:'train' as const,id:train.id,sprite:`train-${direction}` as SpriteKey};
      })),
      ...[{sprite:'fence' as const,position:{x:18,y:18,z:0}},{sprite:'slope' as const,position:{x:19,y:18,z:0}}].map(item=>({...item,kind:'sample' as const,id:item.sprite})),
      ...read.buildings.flatMap(building => Array.from({length:building.height},(_,z) => ({
        position:{...building.position,z:building.position.z+z}, kind:'building' as const,id:building.id,
        sprite:(building.height===1?'pier':z===0?'condo-bottom':z===building.height-1?'condo-top':'condo-middle') as SpriteKey,
      }))),
    ].sort((a,b) => (a.position.y-a.position.x)-(b.position.y-b.position.x) || a.position.z-b.position.z || this.layer(a)-this.layer(b));
  }
  private layer(item: Item) { return {track:0,sample:1,building:1,station:2,train:3}[item.kind]; }
  draw(camera: Camera, width: number, height: number, dpr: number, options: MapOptions) {
    const start=performance.now(), ctx=this.canvas.getContext('2d')!;
    if (this.canvas.width!==Math.round(width*dpr) || this.canvas.height!==Math.round(height*dpr)) {
      this.canvas.width=Math.round(width*dpr); this.canvas.height=Math.round(height*dpr);
    }
    ctx.setTransform(dpr,0,0,dpr,0,0); ctx.imageSmoothingEnabled=false;
    ctx.fillStyle=options.night?'#17232b':'#d8e5d9'; ctx.fillRect(0,0,width,height);
    this.hits=[]; let drawn=0, culled=0;
    if (!this.read) return;
    const zoom=camera.zoom;
    const diamond=(p:Point,color:string,stroke?:string) => {
      ctx.beginPath(); ctx.moveTo(p.x,p.y-8*zoom);ctx.lineTo(p.x+16*zoom,p.y);ctx.lineTo(p.x,p.y+8*zoom);ctx.lineTo(p.x-16*zoom,p.y);ctx.closePath();
      ctx.fillStyle=color;ctx.fill();if(stroke){ctx.strokeStyle=stroke;ctx.lineWidth=1;ctx.stroke();}
    };
    const sprite=(key:SpriteKey,p:Point) => {
      const s=content.sprites[key], x=p.x-16*zoom, y=p.y-(s.offset+8)*zoom;
      if (!visible(x,y,s.width*zoom,s.height*zoom,width,height)) {culled++;return false;}
      const path=options.night?s.nightImage:options.recolor?s.tintImage:s.image;
      ctx.drawImage(this.images.get(path)!,x,y,s.width*zoom,s.height*zoom);drawn++; return true;
    };
    for(let y=0;y<this.read.map.height;y++) for(let x=0;x<this.read.map.width;x++) {
      const p=toScreen({x,y,z:0},camera,width,height);
      if(!visible(p.x-16*zoom,p.y-8*zoom,32*zoom,16*zoom,width,height)){culled++;continue;}
      // Decorative water beneath the fixed pier; no simulation terrain mutation.
      const water=x>=14&&x<=18&&y>=14&&y<=18;
      const color=water?(options.night?'#18334d':'#81b9c6'):options.night?'#263b35':options.winter?'#e5ecea':(x+y)%2?'#aec49f':'#b5c9a6';
      diamond(p,color,options.night?'#30463b':'#a1b795');drawn++;
    }
    for(const item of this.items) {
      if(item.position.z>options.heightCut) continue;
      const p=toScreen(item.position,camera,width,height);
      const building=this.buildings.get(item.id);
      const cut=item.kind==='building' && building && building.position.z+building.height-1>options.heightCut;
      if(cut) {
        if(item.position.z!==building.position.z) continue;
        if(!visible(p.x-16*zoom,p.y-8*zoom,32*zoom,16*zoom,width,height)){culled++;continue;}
        diamond(p,'#d7b883','#705c42');this.hits.push({item:{...item,cut:true},...p});drawn++;continue;
      }
      if(item.sprite) { if(sprite(item.sprite,p)&&item.kind!=='sample')this.hits.push({item,...p});continue; }
      if(!visible(p.x-20*zoom,p.y-20*zoom,40*zoom,40*zoom,width,height)){culled++;continue;}
      if(item.shape) {
        diamond(p,options.night?'#444548':'#b5b7ad');
        // Draw each port as a connected half rail. Curves/junctions follow the host topology.
        for(const port of ports[item.shape]) {
          const q=project({...item.position,x:item.position.x+port.x/2,y:item.position.y+port.y/2}); const base=project(item.position);
          ctx.beginPath();ctx.moveTo(p.x,p.y);ctx.lineTo(p.x+(q.x-base.x)*zoom,p.y+(q.y-base.y)*zoom);
          ctx.strokeStyle=options.night?'#a4b0b5':'#44575c';ctx.lineWidth=5*zoom;ctx.stroke();
          ctx.strokeStyle='#e1dacc';ctx.lineWidth=zoom;ctx.stroke();
        }
        if(item.shape==='buffer'){ctx.fillStyle='#b8593c';ctx.fillRect(p.x-3*zoom,p.y-4*zoom,6*zoom,8*zoom);}
      } else if(item.kind==='station') {
        ctx.fillStyle=options.night?'#aaa277':'#f1d58c';ctx.fillRect(p.x-16*zoom,p.y-15*zoom,32*zoom,7*zoom);
      }
      this.hits.push({item,...p});drawn++;
    }
    if(options.preview && options.preview.z<=options.heightCut) {
      const p=toScreen(options.preview,camera,width,height);
      diamond(p,options.remove?'#d86a5377':'#4ba2a177','#fff');
      ctx.strokeStyle=options.remove?'#b63128':'#126e79';ctx.lineWidth=3*zoom;
      if(options.remove){ctx.beginPath();ctx.moveTo(p.x-7*zoom,p.y-4*zoom);ctx.lineTo(p.x+7*zoom,p.y+4*zoom);ctx.moveTo(p.x+7*zoom,p.y-4*zoom);ctx.lineTo(p.x-7*zoom,p.y+4*zoom);ctx.stroke();}
      else for(const port of ports[options.shape]){ctx.beginPath();ctx.moveTo(p.x,p.y);ctx.lineTo(p.x+8*(port.x+port.y)*zoom,p.y+4*(port.y-port.x)*zoom);ctx.stroke();}
    }
    if(options.selection) {
      const selected=options.selection;
      const position=selected.kind==='train'?this.read.trains.find(train=>train.id===selected.id)?.position:selected.position;
      if(position&&position.z<=options.heightCut)diamond(toScreen(position,camera,width,height),'#ffffff22','#ffeaa0');
    }
    this.canvas.dataset.drawn=String(drawn);this.canvas.dataset.culled=String(culled);
    this.canvas.dataset.paintMs=(performance.now()-start).toFixed(2);
    this.canvas.dataset.zoom=camera.zoom.toFixed(3);this.canvas.dataset.camera=`${camera.x.toFixed(1)},${camera.y.toFixed(1)}`;
    this.canvas.dataset.assets='ready';
  }
  pick(p:Point,zoom:number):Selection|undefined {
    for(let i=this.hits.length-1;i>=0;i--) {
      const {item,x,y}=this.hits[i]; let hit=false;
      if(item.sprite && !item.cut) {
        const s=content.sprites[item.sprite];hit=opaqueAt(s,(p.x-x)/zoom+16,(p.y-y)/zoom+s.offset+8);
      } else if(item.kind==='station') hit=Math.abs(p.x-x)<=16*zoom && p.y>=y-15*zoom && p.y<=y-8*zoom;
      else hit=Math.abs(p.x-x)/(16*zoom)+Math.abs(p.y-y)/(8*zoom)<=1;
      if(hit&&item.kind!=='sample')return {kind:item.kind,id:item.id,position:item.position};
    }
  }
}
