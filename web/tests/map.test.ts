import { opaqueAt, project, tileAt, toScreen, unproject, visible, zoomAt } from '../src/lib/map-geometry.ts';
import sprites from '../src/generated/sprites.json' with { type: 'json' };
function assert(value:unknown,message='Assertion failed'):asserts value {if(!value)throw new Error(message);}
Deno.test('source projection fixtures and inverse preserve elevation and negative coordinates',()=>{
  // World.fromXYZToAB, excluding the camera-only world-origin translation.
  for(const [position,expected] of [
    [{x:0,y:0,z:0},{x:0,y:0}], [{x:1,y:0,z:0},{x:16,y:-8}],
    [{x:0,y:1,z:0},{x:16,y:8}], [{x:0,y:0,z:1},{x:0,y:-16}],
    [{x:34,y:22,z:2},{x:896,y:-128}],
  ]){
    assert(JSON.stringify(project(position))===JSON.stringify(expected));
    const inverse=unproject(expected,position.z);assert(inverse.x===position.x&&inverse.y===position.y);
  }
  const camera={x:600,y:-50,zoom:1.7};
  for(const position of [{x:37,y:16,z:1},{x:-3,y:7,z:0},{x:34,y:22,z:2}]) {
    assert(JSON.stringify(tileAt(toScreen(position,camera,1000,600),position.z,camera,1000,600))===JSON.stringify(position));
  }
});
Deno.test('cursor anchored zoom preserves the world point; camera bounds clamp zoom',()=>{
  const camera={x:800,y:0,zoom:1}; const pointer={x:193,y:472};
  const before=tileAt(pointer,1,camera,900,600), next=zoomAt(camera,1.25,pointer,900,600);
  assert(JSON.stringify(before)===JSON.stringify(tileAt(pointer,1,next,900,600)));
  assert(zoomAt(camera,100,pointer,900,600).zoom===4);
  assert(zoomAt(camera,.001,pointer,900,600).zoom===.5);
});
Deno.test('alpha masks reject transparent bounding-box pixels and preserve source offsets',()=>{
  assert(sprites.sprites['condo-top'].offset===13&&sprites.sprites['condo-bottom'].offset===7&&sprites.sprites.pier.offset===16);
  for(const sprite of Object.values(sprites.sprites)){
    assert(sprite.mask.length===sprite.height&&sprite.mask.every(row=>row.length===sprite.width));
    assert(!opaqueAt(sprite,-1,0)&&!opaqueAt(sprite,sprite.width,0));
    let transparent=0,opaque=0;
    sprite.mask.forEach((row,y)=>[...row].forEach((pixel,x)=>{
      assert(opaqueAt(sprite,x+.2,y+.9)===(pixel==='1'));if(pixel==='1')opaque++;else transparent++;
    }));assert(transparent>0&&opaque>0);
  }
  assert(!visible(-50,0,32,16,800,600)&&visible(-20,0,32,16,800,600));
});
Deno.test('importer rejects external XML, nested expansion, duplicate IDs, missing files and unsupported bitmaps',async()=>{
  const script=`import importlib.util,tempfile,pathlib
s=importlib.util.spec_from_file_location('content','../scripts/import-web-content.py');m=importlib.util.module_from_spec(s);s.loader.exec_module(m)
with tempfile.TemporaryDirectory() as folder:
 p=pathlib.Path(folder)/'plugin.xml'
 for text in ['<!DOCTYPE x SYSTEM "file:///etc/passwd"><plug-in/>', "<!DOCTYPE x [<!ENTITY a '&a;'>]><plug-in>&a;</plug-in>", '<plug-in><contribution id="a"/><contribution id="a"/></plug-in>']:
  p.write_text(text)
  try:m.manifest(p)
  except ValueError:pass
  else:raise AssertionError('Malformed manifest accepted')
 try:m.resolve(pathlib.Path(folder),'missing.bmp')
 except ValueError:pass
 else:raise AssertionError('Missing file accepted')
 p.write_bytes(b'not BMP')
 try:m.bitmap(p)
 except ValueError:pass
 else:raise AssertionError('Invalid bitmap accepted')
`;
  const result=await new Deno.Command('python3',{args:['-c',script],cwd:new URL('../',import.meta.url)}).output();
  assert(result.success,new TextDecoder().decode(result.stderr));
});

Deno.test('renderer picks visible opaque layers in paint order and height cut removes upper floors',async()=>{
  const {MapRenderer}=await import('../src/lib/map-renderer.ts');
  const fixture=JSON.parse(await Deno.readTextFile(new URL('../../prototype/protocol/fixtures/snapshot.json',import.meta.url)));
  const ctx=new Proxy({}, {get:()=>()=>{}});
  const canvas={width:800,height:600,dataset:{},getContext:()=>ctx} as unknown as HTMLCanvasElement;
  const renderer=new MapRenderer(canvas,new Map());
  fixture.map={width:4,height:4,levels:16};fixture.tracks=[{position:{x:1,y:1,z:0},shape:'eastWest',revision:'0'}];fixture.stations=[];fixture.trains=[];
  fixture.buildings=[{id:'tall',definitionId:'condo',position:{x:1,y:1,z:0},height:3}];
  renderer.setRead(fixture);
  const camera={...project({x:1,y:1,z:0}),zoom:2};
  const options={heightCut:15,level:0,night:false,winter:false,recolor:false,shape:'buffer' as const,remove:false};
  renderer.draw(camera,800,600,1,options);
  const top=sprites.sprites['condo-top'];let picked=false;
  for(let y=0;y<top.height&&!picked;y++)for(let x=0;x<top.width&&!picked;x++){
    if(!opaqueAt(top,x,y))continue;
    const p=toScreen({x:1,y:1,z:2},camera,800,600);
    const hit=renderer.pick({x:p.x+(x-16+.5)*2,y:p.y+(y-top.offset-8+.5)*2},2);
    if(hit?.id==='tall'&&hit.position.z===2)picked=true;
  }
  assert(picked,'Opaque roof must pick its upper voxel');
  renderer.draw(camera,800,600,2,{...options,heightCut:0});
  const cut=renderer.pick({x:400,y:300},2);assert(cut?.id==='tall'&&cut.position.z===0,'Cut building has a pickable footprint');
  // A transparent pier corner lets the actual underlying rail receive the pick.
  fixture.buildings=[{id:'pier',definitionId:'pier',position:{x:1,y:1,z:0},height:1}];renderer.setRead(fixture);renderer.draw(camera,800,600,1,options);
  const pier=sprites.sprites.pier;let through=false;
  for(let y=0;y<pier.height&&!through;y++)for(let x=0;x<pier.width&&!through;x++){
    if(opaqueAt(pier,x,y))continue;
    const hit=renderer.pick({x:400+(x-16+.5)*2,y:300+(y-pier.offset-8+.5)*2},2);
    through=hit?.kind==='track';
  }
  assert(through,'Transparent pixels must not swallow a rail pick');
});
