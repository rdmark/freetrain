import { chromium } from '@playwright/test';
import { resolve } from 'node:path';
import { mkdtemp, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';

// Run from web/. Build release and profiling artifacts first. Uses only local servers.
const root=resolve('..'), saves=await mkdtemp(`${tmpdir()}/freetrain-profile-`);
function freePort(){const listener=Deno.listen({hostname:'127.0.0.1',port:0});const port=(listener.addr as Deno.NetAddr).port;listener.close();return port;}
const servicePort=freePort();let fixturePort=freePort();while(fixturePort===servicePort)fixturePort=freePort();
const serviceUrl=`http://127.0.0.1:${servicePort}`, fixtureUrl=`http://127.0.0.1:${fixturePort}`;
const child=new Deno.Command('dotnet',{args:[`${root}/src/FreeTrain.Server/bin/Release/net10.0/FreeTrain.Server.dll`,'--urls',serviceUrl,'--webroot',`${root}/web/dist`],
  cwd:root,env:{FreeTrain__Saves__Directory:saves,FreeTrain__OperatorSecret:'',FreeTrain__TrustedProxy:''},stdout:'null',stderr:'inherit'}).spawn();
const fixtureServer=new Deno.Command('python3',{args:['-m','http.server',String(fixturePort),'--bind','127.0.0.1','--directory',`${root}/tmp/web-profile`],stdout:'null',stderr:'inherit'}).spawn();
const browser=await chromium.launch({headless:true});
try {
  for(const port of [servicePort,fixturePort])for(let i=0;i<100;i++){try{if((await fetch(`http://127.0.0.1:${port}/`)).ok)break;}catch{}if(i===99)throw new Error(`Server ${port} unavailable`);await new Promise(r=>setTimeout(r,100));}
  const page=await browser.newPage({viewport:{width:1920,height:1080},deviceScaleFactor:1});
  const errors:string[]=[];page.on('pageerror',error=>errors.push(error.message));
  const cdp=await page.context().newCDPSession(page);await cdp.send('Performance.enable');
  await page.goto(fixtureUrl+'/profiling/renderer.html');
  await page.waitForFunction(()=>typeof (window as any).profileRenderer==='function');
  const synthetic=await page.evaluate(()=>(window as any).profileRenderer());
  await page.screenshot({path:`${root}/tmp/web-prototype-profile-large-scene.png`});
  const distribution=(values:number[])=>{const s=[...values].sort((a,b)=>a-b);return {count:s.length,p50:s[Math.ceil(.5*s.length)-1],p95:s[Math.ceil(.95*s.length)-1],p99:s[Math.ceil(.99*s.length)-1],max:s.at(-1)};};
  let totalBytes=0,deltas=0;const intervals:number[]=[];let last=0;
  page.on('websocket',socket=>socket.on('framereceived',frame=>{totalBytes+=new TextEncoder().encode(String(frame.payload)).length;const m=JSON.parse(String(frame.payload));if(m.type==='delta'){const now=performance.now();if(last)intervals.push(now-last);last=now;deltas++;}}));
  await page.goto(serviceUrl+'/');
  await page.getByTestId('connection').filter({hasText:'Connected to game'}).waitFor();
  const state=await (await page.request.get(serviceUrl+'/api/bootstrap?protocolVersion=1&contentVersion=prototype-content-1')).json();
  const metrics=async()=>Object.fromEntries((await cdp.send('Performance.getMetrics')).metrics.map(m=>[m.name,m.value]));
  const sampleCpu=async(active:boolean)=>{
    if(active)await page.getByRole('button',{name:'Run',exact:false}).click();
    const before=await metrics();await page.waitForTimeout(5000);const after=await metrics();
    if(active)await page.getByRole('button',{name:'Pause',exact:false}).click();
    return {seconds:after.Timestamp-before.Timestamp,rendererTaskCpuPercent:100*(after.TaskDuration-before.TaskDuration)/(after.Timestamp-before.Timestamp),jsHeapBytes:after.JSHeapUsedSize,jsHeapTotalBytes:after.JSHeapTotalSize};
  };
  const paused=await sampleCpu(false);totalBytes=0;deltas=0;last=0;intervals.length=0;
  const active=await sampleCpu(true);const traffic={seconds:active.seconds,jsonPayloadBytes:totalBytes,bytesPerSecond:totalBytes/active.seconds,deltas,intervalMs:distribution(intervals)};
  // Actual map input handlers -> renderer completion, measured inside the page.
  const feedback=async()=>page.locator('canvas').evaluate(async el=>{
    const canvas=el as HTMLCanvasElement,r=canvas.getBoundingClientRect(),times:number[]=[];
    for(let i=0;i<60;i++){
      const start=performance.now();
      await new Promise<void>((resolve,reject)=>{
        const timeout=setTimeout(()=>{observer.disconnect();reject(new Error('Map input did not draw'));},3000);
        const observer=new MutationObserver(()=>{clearTimeout(timeout);observer.disconnect();resolve();});
        observer.observe(canvas,{attributes:true,attributeFilter:['data-paint-ms']});
        canvas.dispatchEvent(new PointerEvent('pointermove',{bubbles:true,pointerId:1,buttons:1,clientX:r.x+r.width/2+20+i,clientY:r.y+r.height/2+10+i/2}));
      });times.push(performance.now()-start);
    }
    return times;
  });
  await page.locator('canvas').scrollIntoViewIfNeeded();const box=(await page.locator('canvas').boundingBox())!;
  await page.mouse.move(box.x+box.width/2,box.y+box.height/2);await page.mouse.down();
  const cameraFeedback=distribution(await feedback());await page.mouse.up();
  await page.getByRole('button',{name:'Build track',exact:true}).click();
  const previewFeedback=distribution(await feedback());
  await page.getByRole('button',{name:'Inspect / drive',exact:true}).click();
  // Inject 50 ms each direction through a same-origin WebSocket forwarding route.
  await page.goto('about:blank');await page.waitForTimeout(200);
  const timers:Set<ReturnType<typeof setTimeout>>=new Set();
  await page.routeWebSocket('**/api/session?*',route=>{
    const server=route.connectToServer();
    const delay=(fn:()=>void)=>{const timer=setTimeout(()=>{timers.delete(timer);fn();},50);timers.add(timer);};
    route.onMessage(message=>delay(()=>server.send(message)));
    server.onMessage(message=>delay(()=>route.send(message)));
  });
  await page.goto(serviceUrl+'/');await page.getByTestId('connection').filter({hasText:'Connected to game'}).waitFor();
  const latencies:number[]=[];
  for(let i=0;i<40;i++){
    const button=page.getByRole('button',{name:'Single step',exact:false});
    // Measure inside the page from actual DOM click through confirmed UI state.
    const time=await button.evaluate(async el=>{
      const previousTick=document.querySelector('[data-testid=tick]')?.textContent;
      const start=performance.now();(el as HTMLButtonElement).click();
      await new Promise<void>((resolve,reject)=>{const timeout=setTimeout(()=>{observer.disconnect();reject(new Error('Command confirmation timed out'));},8000);const check=()=>{if(document.querySelector('[role=status]')?.textContent==='Confirmed by server'&&document.querySelector('[data-testid=tick]')?.textContent!==previousTick){clearTimeout(timeout);observer.disconnect();resolve();}};const observer=new MutationObserver(check);observer.observe(document.body,{subtree:true,childList:true,characterData:true});check();});
      return performance.now()-start;
    });latencies.push(time);
  }
  await page.screenshot({path:`${root}/tmp/web-prototype-profile-reference.png`,fullPage:true});
  for(const timer of timers)clearTimeout(timer);
  if(latencies.some(value=>value<90))throw new Error('Latency sample bypassed the injected 100ms round trip');
  if(errors.length)throw new Error(errors.join('\n'));
  const report={recordedAt:new Date().toISOString(),browser:browser.version(),os:Deno.build,synthetic,
    reference:{map:state.snapshot.map,tracks:state.snapshot.tracks.length,trains:state.snapshot.trains.length,cars:state.snapshot.trains[0].cars,stations:state.snapshot.stations.length,buildings:state.snapshot.buildings.length,paused,active,traffic,cameraFeedbackMs:cameraFeedback,previewFeedbackMs:previewFeedback,authoritativeFeedback100msRtt:distribution(latencies)},errors};
  await writeFile(`${root}/tmp/web-prototype-performance.json`,JSON.stringify(report,null,2)+'\n');
  console.log(JSON.stringify(report,null,2));
} finally {
  await browser.close();
  for(const process of [child,fixtureServer]){try{process.kill('SIGTERM');}catch{/* Already exited. */}}
  await Promise.allSettled([child.status,fixtureServer.status]);
}
