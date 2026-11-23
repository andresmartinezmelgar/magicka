import test from 'node:test';
import assert from 'node:assert/strict';

test('la aplicación conecta, filtra, exporta, importa y vuelve al servidor con el nuevo modelo',async()=>{
  const names=['document','window','location','history','sessionStorage','fetch','setTimeout','clearTimeout','setInterval','clearInterval'];
  const originals=new Map(names.map(name=>[name,Object.getOwnPropertyDescriptor(globalThis,name)]));
  const createUrl=URL.createObjectURL,revokeUrl=URL.revokeObjectURL;
  const nodes=new Map(),timers=new Map(),blobs=[],requests=[];
  let nextTimer=0;
  const element = id => {
    if(!nodes.has(id))nodes.set(id,{id,value:'',checked:false,disabled:false,hidden:false,open:false,
      textContent:'',innerHTML:'',classList:{toggle(){}},dataset:{},addEventListener(){},setAttribute(){},
      showModal(){this.open=true;},close(){this.open=false;},click(){},focus(){},setSelectionRange(){}});
    return nodes.get(id);
  };
  const day=new Date().toISOString().slice(0,10);
  const report = id => ({schemaVersion:1,runId:id.repeat(32),revision:600,gameVersion:'0.29.73',
    day,mode:'defense',map:'bastion',network:true,status:'completed',seconds:10,humans:1,bots:1,
    difficulty:2,winnerTeam:0,scores:[2,0],metrics:[
      {group:'humanElement',key:7,measure:'damage',value:10},
      {group:'botElement',key:7,measure:'damage',value:20}
    ]});
  const page={schemaVersion:1,start:day,end:day,reports:[report('a')],telemetry:[]};
  const listeners=new Map();
  const document={hidden:false,documentElement:{},querySelector(selector){
    return selector==='dialog[open]'?null:element(selector.slice(1));
  },querySelectorAll(){return [];},createElement(){return element('download');},
  addEventListener(name,callback){listeners.set(name,callback);},removeEventListener(name){listeners.delete(name);}};
  const fetch=async(url,options={})=>{
    requests.push({url,options});
    if(url==='./version.json')return {ok:true,json:async()=>({version:'fixture'})};
    if(!['https://arena.example.test/api/dashboard/history','https://arena.example.test/api/dashboard/live'].includes(url))
      throw Error('Petición no prevista: '+url);
    const value=url.endsWith('/history')?page:{...page,reports:[]};
    return {ok:true,status:200,headers:{get(){return 'fixture-etag';}},text:async()=>JSON.stringify(value)};
  };
  const settle=async()=>{for(let index=0;index<6;index++)await new Promise(resolve=>setImmediate(resolve));};
  try {
    Object.assign(globalThis,{document,fetch,location:{hash:'',hostname:'arena.example.test',protocol:'https:',origin:'https://arena.example.test'},
      window:{addEventListener(){},scrollTo(){}},history:{replaceState(){}},sessionStorage:{getItem(){return null;},setItem(){}},
      setTimeout(callback,delay){const id=++nextTimer;timers.set(id,{callback,delay});return id;},
      clearTimeout(id){timers.delete(id);},setInterval(){return ++nextTimer;},clearInterval(){}});
    URL.createObjectURL=blob=>{blobs.push(blob);return 'blob:fixture';};URL.revokeObjectURL=()=>{};
    await import('../web/app.mjs');
    await settle();
    assert.match(element('source-status').innerHTML,/Servidor conectado/);
    assert.match(element('coverage').textContent,/1 documentos/);
    assert.ok(element('content').innerHTML.includes('a'.repeat(32)));
    assert.ok(requests.filter(request=>request.url.includes('/api/dashboard/')).every(request=>request.options.credentials==='omit'));

    const bots=element('include-bots');bots.checked=false;bots.onchange({target:bots});
    element('export').onclick();
    let csv=await blobs.at(-1).text();
    assert.equal(csv.split('\r\n')[1].split(';')[8],'"10"');

    const input=element('import');
    input.files=[{size:100,text:async()=>JSON.stringify([report('b')])}];
    await input.onchange({target:input});
    assert.match(element('source-status').innerHTML,/Archivo importado/);
    element('export').onclick();csv=await blobs.at(-1).text();
    assert.ok(csv.includes('b'.repeat(32))&&!csv.includes('a'.repeat(32)));

    input.files=[{size:2,text:async()=>'[]'}];await input.onchange({target:input});
    assert.match(element('settings-error').textContent,/No hay informes válidos/);
    element('export').onclick();assert.ok((await blobs.at(-1).text()).includes('b'.repeat(32)));

    element('server-data').onclick();await settle();
    assert.match(element('source-status').innerHTML,/Servidor conectado/);
    element('export').onclick();csv=await blobs.at(-1).text();
    assert.ok(csv.includes('a'.repeat(32))&&!csv.includes('b'.repeat(32)));
  } finally {
    const source=await import('../web/public-data.mjs');source.stop();
    URL.createObjectURL=createUrl;URL.revokeObjectURL=revokeUrl;
    for(const [name,descriptor] of originals){if(descriptor)Object.defineProperty(globalThis,name,descriptor);else delete globalThis[name];}
  }
});
