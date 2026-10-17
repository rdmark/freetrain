<script lang="ts">
  import { onMount } from 'svelte';
  import type { ShellModel, ShellState } from './lib/shell-model.ts';
  import { messages } from './lib/localization.ts';
  import { MapRenderer, loadImages, type Selection } from './lib/map-renderer.ts';
  import { project, same, tileAt, zoomAt, type Camera } from './lib/map-geometry.ts';
  import type { Position, Track } from './generated/contracts.ts';
  let { model, view, onselect }: { model: ShellModel; view: ShellState; onselect: (selection: Selection | undefined) => void } = $props();
  const text=$derived(messages[view.locale]);
  let canvas: HTMLCanvasElement;
  let renderer: MapRenderer | undefined;
  let camera: Camera={...project({x:22,y:16,z:0}),zoom:1};
  let width=0, height=0, dirty=true;
  let tool=$state<'inspect'|'build'|'remove'>('inspect');
  let entering=$state(false);
  let level=$state(1), heightCut=$state(15);
  let shape=$state<Track['shape']>('buffer');
  let night=$state(false), winter=$state(false), recolor=$state(false);
  let preview=$state<Position>();
  let selection=$state<Selection>();
  let selectedPosition=$state<Position>();
  let assetsFailed=$state(false);
  let manualActive=$state(false);
  let held=$state('');
  const keys=new Set<string>();
  let sendingInput=false, inputDirty=false;
  let epoch:string|undefined;
  let drag: {x:number;y:number;camera:Camera;moved:boolean} | undefined;
  const blocked=$derived(view.pending || view.connection!=='ready' || entering);
  const shapes: Track['shape'][]=['buffer','eastWest','northSouth','curveSE','curveSW','curveNE','curveNW','junctionE'];
  async function flushInput() {
    inputDirty=true;
    if(sendingInput)return;
    sendingInput=true;
    try { while(inputDirty){inputDirty=false;await model.input(new Set(keys));} }
    finally {sendingInput=false;}
  }
  function clearInput(){const had=keys.size>0;keys.clear();held='';manualActive=false;if(had)void flushInput();}
  function key(event:KeyboardEvent,down:boolean) {
    if(event.key==='Escape'){event.preventDefault();preview=undefined;selectedPosition=undefined;clearInput();if(tool!=='inspect')void chooseTool('inspect');return;}
    if(event.isComposing || event.keyCode===229 || event.target!==canvas || tool!=='inspect' || view.connection!=='ready' || view.read?.trains[0].controller!=='manual')return;
    if(!['ArrowUp','ArrowDown','ArrowLeft','ArrowRight','Shift'].includes(event.key))return;
    event.preventDefault();
    if(event.repeat)return;
    if(down)keys.add(event.key);else keys.delete(event.key);
    manualActive=true;held=[...keys].join(' + ');void flushInput();
  }
  async function chooseTool(next:typeof tool) {
    if(blocked)return;
    clearInput(); entering=true;
    // Tools remain local; entering construction explicitly pauses through the host.
    // Leaving never resumes, preserving a pause from before tool entry.
    try {
      if(next!=='inspect' && view.read?.paused===false && !await model.send({kind:'pause'}))return;
      tool=next;preview=undefined;selectedPosition=undefined;
    } finally {entering=false;dirty=true;}
  }
  function point(event:MouseEvent){const r=canvas.getBoundingClientRect();return {x:event.clientX-r.left,y:event.clientY-r.top};}
  function move(event:PointerEvent){
    const p=point(event);
    if(drag && (event.buttons&1 || event.buttons&4)) {
      if(Math.hypot(p.x-drag.x,p.y-drag.y)>3)drag.moved=true;
      if(drag.moved){camera={...drag.camera,x:drag.camera.x-(p.x-drag.x)/camera.zoom,y:drag.camera.y-(p.y-drag.y)/camera.zoom};preview=undefined;dirty=true;return;}
    }
    if(tool!=='inspect'){preview=tileAt(p,level,camera,width,height);dirty=true;}
  }
  function down(event:PointerEvent){if(event.button!==0&&event.button!==1)return;event.preventDefault();canvas.focus({preventScroll:true});const p=point(event);drag={...p,camera:{...camera},moved:false};canvas.setPointerCapture(event.pointerId);}
  function up(event:PointerEvent){
    const was=drag;drag=undefined;
    if(canvas.hasPointerCapture(event.pointerId))canvas.releasePointerCapture(event.pointerId);
    if(!was||was.moved||event.button!==0)return;
    const p=point(event);
    if(tool==='inspect') { selection=renderer?.pick(p,camera.zoom);onselect(selection); }
    else { selectedPosition=tileAt(p,level,camera,width,height);preview=selectedPosition; }
    dirty=true;
  }
  async function commit(){
    if(blocked||!selectedPosition||!view.read?.paused)return;
    const position={...selectedPosition};
    if(tool==='build')await model.send({kind:'buildTrack',position,shape});
    else if(tool==='remove') {
      const track=view.read.tracks.find(t=>same(t.position,position));
      if(track)await model.send({kind:'removeTrack',position,expectedRevision:track.revision});
    }
    // Keep rejected previews for correction; authoritative state alone changes the map/cash.
  }
  function zoom(factor:number){camera=zoomAt(camera,factor,{x:width/2,y:height/2},width,height);dirty=true;}
  function home(){camera={...project({x:22,y:16,z:0}),zoom:Math.min(1.5,Math.max(.5,width/850))};dirty=true;}
  function focusTrain(){const train=view.read?.trains[0];if(train){camera={...project(train.position),zoom:camera.zoom};dirty=true;canvas.focus();}}
  $effect(()=>{if(view.read){if(epoch!==view.read.worldEpoch){epoch=view.read.worldEpoch;selection=undefined;preview=undefined;selectedPosition=undefined;tool='inspect';}renderer?.setRead(view.read);dirty=true;}if(view.connection!=='ready'){preview=undefined;selectedPosition=undefined;}if(view.connection!=='ready'||view.read?.trains[0].controller!=='manual')clearInput();});
  $effect(()=>{void heightCut;void level;void night;void winter;void recolor;void selection;void preview;void shape;void tool;dirty=true;});
  onMount(()=>{
    let active=true, frame=0;
    const resize=new ResizeObserver(entries=>{width=entries[0].contentRect.width;height=entries[0].contentRect.height;if(!camera.zoom)camera.zoom=1;dirty=true;});resize.observe(canvas);
    loadImages().then(images=>{if(!active)return;renderer=new MapRenderer(canvas,images);if(view.read)renderer.setRead(view.read);home();}).catch(()=>{assetsFailed=true;});
    const paint=()=>{if(active){if(dirty&&renderer&&width&&height&&!document.hidden){renderer.draw(camera,width,height,devicePixelRatio,{heightCut,level,night,winter,recolor,selection,preview,shape,remove:tool==='remove'});dirty=false;}frame=requestAnimationFrame(paint);}};frame=requestAnimationFrame(paint);
    const renew=setInterval(()=>{if(keys.size)void flushInput();},150);
    const blur=()=>clearInput();
    const visibility=()=>{clearInput();dirty=true;};
    const wheel=(event:WheelEvent)=>{event.preventDefault();camera=zoomAt(camera,event.deltaY<0?1.15:1/1.15,point(event),width,height);dirty=true;};
    canvas.addEventListener('wheel',wheel,{passive:false});window.addEventListener('blur',blur);document.addEventListener('visibilitychange',visibility);
    return()=>{active=false;clearInput();clearInterval(renew);cancelAnimationFrame(frame);resize.disconnect();canvas.removeEventListener('wheel',wheel);window.removeEventListener('blur',blur);document.removeEventListener('visibilitychange',visibility);};
  });
</script>

<div class="map-tools" aria-label={text.mapTools}>
  <button aria-pressed={tool==='inspect'} disabled={blocked} onclick={()=>chooseTool('inspect')}>{text.inspectTool}</button>
  <button aria-pressed={tool==='build'} disabled={blocked} onclick={()=>chooseTool('build')}>{text.buildTool}</button>
  <button aria-pressed={tool==='remove'} disabled={blocked} onclick={()=>chooseTool('remove')}>{text.removeTool}</button>
  <button aria-label={text.zoomIn} onclick={()=>zoom(1.25)}>＋</button><button aria-label={text.zoomOut} onclick={()=>zoom(.8)}>−</button>
  <button onclick={home}>{text.homeMap}</button><button onclick={focusTrain}>{text.findTrain}</button>
</div>
<div class="map-options">
  <label>{text.heightCut}<input aria-label={text.heightCut} type="range" min="0" max="15" bind:value={heightCut}/><span>{heightCut}</span></label>
  {#if tool!=='inspect'}
    <label>{text.buildLevel}<select bind:value={level} onchange={()=>{selectedPosition=undefined;preview=undefined;}}><option value={0}>0</option><option value={1}>1</option></select></label>
    {#if tool==='build'}<label>{text.trackShape}<select bind:value={shape}>{#each shapes as s}<option value={s}>{text[s]}</option>{/each}</select></label>{/if}
  {/if}
  <label><input type="checkbox" bind:checked={night}/>{text.night}</label><label><input type="checkbox" bind:checked={winter}/>{text.winter}</label><label><input type="checkbox" bind:checked={recolor}/>{text.recolor}</label>
</div>
<p class="map-help">{tool==='inspect'?text.mapHelp:text.constructionHelp}</p>
<canvas bind:this={canvas} class="world-canvas" tabindex="0" aria-label={text.mapCanvas} aria-describedby="manual-help" onpointerdown={down} onpointermove={move} onpointerup={up} onpointercancel={()=>{drag=undefined;}} onpointerleave={()=>{if(!drag){preview=selectedPosition;dirty=true;}}} onkeydown={event=>key(event,true)} onkeyup={event=>key(event,false)} onblur={clearInput}></canvas>
{#if assetsFailed}<p role="alert">{text.assetsFailed}</p>{/if}
<div class="map-action">
  {#if tool!=='inspect'}
    <span data-testid="preview-position">{selectedPosition?`${selectedPosition.x}, ${selectedPosition.y}, ${selectedPosition.z}`:text.pickTile}</span>
    <button disabled={blocked||!selectedPosition||!view.read?.paused||(tool==='remove'&&!view.read?.tracks.some(t=>same(t.position,selectedPosition!)))} onclick={commit}>{text.confirmTrack}</button>
    <button onclick={()=>{preview=undefined;selectedPosition=undefined;dirty=true;}}>{text.cancelPreview}</button>
  {:else}<span data-testid="map-selection">{selection?`${text[selection.kind]} · ${selection.id}`:text.pickObject}</span>{/if}
</div>
<p id="manual-help" class="map-help">{text.manualHelp} <strong data-testid="manual-input">{held|| (manualActive?text.keysReleased:text.mapFocus)}</strong></p>
<p class="map-help">{text.visualSample}</p>
