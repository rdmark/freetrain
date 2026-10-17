import { chromium, expect } from '@playwright/test';
import { resolve } from 'node:path';

// Run from web/ after docker build. Owns only the freetrain-stage7-check project.
// Fixed subnet/ports in compose.yaml must be free. Leaves evidence under tmp/.
const root=resolve('..'), project='freetrain-stage7-check';
const secret=crypto.randomUUID()+crypto.randomUUID();
const env={...Deno.env.toObject(),FREETRAIN_OPERATOR_SECRET:secret,
  FREETRAIN_IMAGE:'freetrain:prototype',FREETRAIN_SAVE_VOLUME:`${project}_saves`,
  FREETRAIN_SITE:'localhost',FREETRAIN_TLS:'internal',FREETRAIN_BIND:'127.0.0.1',
  FREETRAIN_HTTP_PORT:'8080',FREETRAIN_HTTPS_PORT:'8443',FREETRAIN_CHECKPOINT_INTERVAL:'00:00:30'};
const compose=['compose','-p',project,'-f',`${root}/deploy/compose.yaml`];
async function docker(args:string[],environment=env) {
  const result=await new Deno.Command('docker',{args,env:environment,stdout:'piped',stderr:'piped'}).output();
  if(!result.success)throw new Error(`docker ${args[0]} failed: ${new TextDecoder().decode(result.stderr)}`);
  return new TextDecoder().decode(result.stdout).trim();
}
const url='https://localhost:8443',boot='/api/bootstrap?protocolVersion=1&contentVersion=prototype-content-1';
let client:Deno.HttpClient|undefined;
const sleep=(ms:number)=>new Promise(r=>setTimeout(r,ms));
async function healthy(){for(let i=0;i<200;i++){try{const response=await fetch(url+'/health',{client});if(response.ok){const health=await response.json();expect(health.status).toBe('ok');return;}}catch{}await sleep(100);}throw new Error('Deployment not healthy');}
const browser=await chromium.launch({headless:true});
const errors:string[]=[];
let restoredVolume=false;
const restoreEnv={...env,FREETRAIN_SAVE_VOLUME:`${project}_restored`};
try {
  const start=performance.now();
  await docker([...compose,'up','-d','--no-build']);
  for(let i=0;i<100;i++){
    try {await docker(['cp',`${project}-proxy-1:/data/caddy/pki/authorities/local/root.crt`,`${root}/tmp/web-prototype-deployment-root.crt`]);break;}
    catch(error){if(i===99)throw error;await sleep(100);}
  }
  client=Deno.createHttpClient({caCerts:[await Deno.readTextFile(`${root}/tmp/web-prototype-deployment-root.crt`)]});
  await healthy();const coldStartMs=performance.now()-start;
  const redirect=await fetch('http://localhost:8080/health',{redirect:'manual'});
  expect(redirect.status).toBe(308);expect(redirect.headers.get('location')).toBe(url+'/health');
  const architecture=await docker(['exec',`${project}-game-1`,'uname','-m']);expect(architecture).toBe('x86_64');
  const info=await docker(['exec',`${project}-game-1`,'dotnet','--info']);
  const image=JSON.parse(await docker(['image','inspect','freetrain:prototype','--format','{{json .}}']));
  // Independent CA/hostname verification above. Chromium bypasses local test-CA trust only.
  const context=await browser.newContext({ignoreHTTPSErrors:true,viewport:{width:1920,height:1080},locale:'en'});
  const page=await context.newPage();page.on('pageerror',e=>errors.push(e.message));
  const socketUrls:string[]=[];page.on('websocket',socket=>socketUrls.push(socket.url()));
  async function login(){await page.goto(url);await page.getByLabel('Operator access key').fill(secret);await page.getByRole('button',{name:'Connect',exact:true}).click();await expect(page.getByTestId('connection')).toHaveText('Connected to game');await expect(page.locator('canvas')).toHaveAttribute('data-assets','ready');}
  async function bootstrap(){const response=await page.request.get(url+boot);expect(response.ok()).toBe(true);return response.json();}
  async function rename(name:string){await page.getByLabel('Station name',{exact:true}).fill(name);await page.getByRole('button',{name:'Apply name',exact:true}).click();await expect(page.getByTestId('confirmed-name')).toHaveText(name);}
  await login();
  expect((await context.cookies()).find(c=>c.name==='freetrain.operator')).toMatchObject({secure:true,httpOnly:true,sameSite:'Strict',path:'/api'});
  expect(socketUrls.length).toBeGreaterThan(0);expect(socketUrls.every(u=>u.startsWith('wss://'))).toBe(true);
  expect((await page.request.get(url+boot,{headers:{Origin:'https://foreign.example'}})).status()).toBe(403);
  expect((await page.request.get(url+'/api/bootstrap?protocolVersion=999&contentVersion=prototype-content-1')).status()).toBe(426);
  const anonymous=await browser.newContext({ignoreHTTPSErrors:true});expect((await anonymous.request.get(url+boot)).status()).toBe(401);await anonymous.close();
  await page.getByRole('button',{name:'Run',exact:false}).click();await expect.poll(async()=>Number((await bootstrap()).snapshot.tick)).toBeGreaterThan(3);
  await page.getByRole('button',{name:'Pause',exact:false}).click();
  async function resources(active:boolean){
    if(active)await page.getByRole('button',{name:'Run',exact:false}).click();
    const sample=async()=>{
      const data=await docker(['exec',`${project}-game-1`,'sh','-c','cat /sys/fs/cgroup/cpu.stat /sys/fs/cgroup/memory.current /sys/fs/cgroup/memory.peak']);
      const lines=data.split('\n');return {usageUs:Number(lines.find(l=>l.startsWith('usage_usec '))!.split(' ')[1]),memoryBytes:Number(lines.at(-2)),peakMemoryBytes:Number(lines.at(-1))};
    };
    const before=await sample(),started=performance.now();await sleep(5000);const after=await sample(),elapsedMs=performance.now()-started;
    if(active)await page.getByRole('button',{name:'Pause',exact:false}).click();
    return {elapsedMs,cpuPercentOfOneCore:100*(after.usageUs-before.usageUs)/(elapsedMs*1000),memoryBytes:after.memoryBytes,peakMemoryBytes:after.peakMemoryBytes};
  }
  const paused=await resources(false),active=await resources(true);
  await rename('Linux永続ボリューム駅');
  let before=await bootstrap();const saveTimes:number[]=[];
  for(let i=0;i<20;i++){const t=performance.now();const r=await page.request.post(`${url}/api/saves/checkpoint/save?expectedWorldEpoch=${before.worldEpoch}`);expect(r.ok()).toBe(true);saveTimes.push(performance.now()-t);}
  const backup=await (await page.request.get(url+'/api/saves/download')).body();await Deno.writeFile(`${root}/tmp/web-prototype-linux-backup.json`,backup);
  await page.screenshot({path:`${root}/tmp/web-prototype-deployment-en.png`,fullPage:true});
  const incarnation=before.serverIncarnation;
  await rename('チェックポイント後の未保存変更');
  // Keep the browser socket open: closing it would intentionally checkpoint.
  await docker([...compose,'kill','-s','SIGKILL','game']);
  const crashStart=performance.now();await docker([...compose,'up','-d','--no-build','game']);await healthy();const crashRestartMs=performance.now()-crashStart;
  await login();before=await bootstrap();expect(before.serverIncarnation).not.toBe(incarnation);expect(before.snapshot.paused).toBe(true);expect(before.snapshot.stations[0].name).toBe('Linux永続ボリューム駅');
  await rename('SIGTERMで保存した駅');
  const shutdownStart=performance.now();await docker([...compose,'stop','game']);const gracefulStopMs=performance.now()-shutdownStart;
  expect(await docker(['inspect',`${project}-game-1`,'--format','{{.State.ExitCode}}'])).toBe('0');
  const gracefulStart=performance.now();await docker([...compose,'up','-d','--no-build','game']);await healthy();const gracefulRestartMs=performance.now()-gracefulStart;
  await login();before=await bootstrap();expect(before.snapshot.stations[0].name).toBe('SIGTERMで保存した駅');expect(before.snapshot.paused).toBe(true);
  await docker([...compose,'stop','game']);
  await docker(['volume','create',`${project}_restored`]);restoredVolume=true;
  await docker(['run','--rm','--platform','linux/amd64','--user','0','-v',`${project}_restored:/data/saves`,'--mount',`type=bind,src=${root}/tmp/web-prototype-linux-backup.json,dst=/backup.json,readonly`,'--entrypoint','sh','freetrain:prototype','-c','cp /backup.json /data/saves/checkpoint.json && chown -R 1654:1654 /data/saves']);
  const restoreStart=performance.now();await docker([...compose,'up','-d','--no-build','game'],restoreEnv);await healthy();const independentRestoreMs=performance.now()-restoreStart;
  await login();before=await bootstrap();expect(before.snapshot.stations[0].name).toBe('Linux永続ボリューム駅');expect(before.snapshot.paused).toBe(true);
  await page.getByLabel('Language').selectOption('ja');await page.screenshot({path:`${root}/tmp/web-prototype-deployment-ja.png`,fullPage:true});
  const profile=JSON.parse(await docker(['run','--rm','--platform','linux/amd64','freetrain:prototype','--headless-profile']));
  const sorted=saveTimes.sort((a,b)=>a-b);expect(errors).toEqual([]);
  const report={recordedAt:new Date().toISOString(),scope:'Local Docker Desktop ARM host, emulated linux/amd64 game and Caddy; private CA verified by Deno; Chromium ignores local CA trust only; no public host',
    image:{id:image.Id,architecture:image.Architecture,os:image.Os,bytes:image.Size},architecture,runtime:info,browser:browser.version(),coldComposeStartMs:coldStartMs,
    save:{bytes:backup.length,httpMs:{count:20,p50:sorted[9],p95:sorted[18],max:sorted[19]},intervalSeconds:30},paused,active,
    crashRestartMs,gracefulStopMs,gracefulRestartMs,independentRestoreMs,headlessProfile:profile,
    checks:['CA and hostname verified HTTPS','same-origin WSS real movement/rename','Secure HttpOnly Strict cookie','anonymous/foreign-origin/version rejected','SIGKILL loses post-checkpoint rename only','same-volume restart changes incarnation and starts paused','SIGTERM with open socket exits zero and checkpoints','independent download restores onto fresh volume','English/Japanese release captures'],errors};
  await Deno.writeTextFile(`${root}/tmp/web-prototype-deployment-measurements.json`,JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify(report,null,2));
} finally {
  await browser.close();client?.close();
  await docker([...compose,'down','--volumes'],restoredVolume?restoreEnv:env);
  // With a restore volume selected, Compose removed that volume; remove the test's original too.
  if(restoredVolume)await docker(['volume','rm',`${project}_saves`]);
}
