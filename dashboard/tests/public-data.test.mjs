import test from 'node:test';
import assert from 'node:assert/strict';
import {createPublicSource, defaultApiRoot} from '../web/public-data.mjs';

const settle = () => new Promise(resolve => setImmediate(resolve));
function fixture(fetchImpl, hidden=false) {
  let serial=0;
  const timers = new Map(), listeners = new Map();
  const documentImpl = {hidden, addEventListener:(name,fn)=>listeners.set(name,fn), removeEventListener:name=>listeners.delete(name)};
  const source = createPublicSource({fetchImpl,documentImpl,apiRoot:'https://example.test/api/dashboard',
    setTimer:(fn,ms)=>{timers.set(++serial,{fn,ms});return serial;},clearTimer:id=>timers.delete(id)});
  const history=[], live=[], errors=[], coverage=[];
  const callbacks={history:(...args)=>history.push(args),live:value=>live.push(value),error:(...args)=>errors.push(args),coverage:value=>coverage.push(value)};
  return {source,history,live,errors,coverage,timers,callbacks,
    visibility(value){documentImpl.hidden=value;listeners.get('visibilitychange')?.();},
    tick(){const entry=[...timers].find(([,v])=>v.ms===15000);assert.ok(entry,'Hay un refresco programado');timers.delete(entry[0]);entry[1].fn();}};
}
function page(reports=[],telemetry=[]) {return {schemaVersion:1,start:'2026-10-10',end:'2026-11-08',partial:false,stale:false,reports,telemetry};}
function response(value,etag='W/"one"') {return new Response(JSON.stringify(value),{headers:{'Content-Type':'application/json',ETag:etag}});}

test('consulta automática pública, sin sesión y con histórico separado del directo',async()=>{
  const requests=[];
  const f=fixture(async(url,options)=>{requests.push({url,options});return response(url.endsWith('/history')?page([{runId:'historical'}],[{report:{runId:'ended'},updatedAt:1}]):page([],[{report:{runId:'playing'},updatedAt:2}]));});
  f.source.subscribe({},f.callbacks);await settle();
  assert.equal(requests.length,2);
  assert.ok(requests.every(r=>r.options.credentials==='omit'&&!('Authorization' in r.options.headers)));
  assert.equal(f.history[0][0][0].runId,'historical');
  assert.deepEqual(f.live[0].map(v=>v.runId),['ended','playing']);
  assert.equal(f.errors.length,0);f.source.stop();assert.equal(f.timers.size,0);
});

test('304 conserva los datos y usa ETag sin descargar otra copia del mes',async()=>{
  const requests=[];
  const f=fixture(async(url,options)=>{requests.push({url,options});return requests.length<=2?response(page([{runId:'one'}])):new Response(null,{status:304});});
  f.source.subscribe({},f.callbacks);await settle();f.tick();await settle();
  assert.ok(requests.slice(2).every(r=>r.options.headers['If-None-Match']==='W/"one"'));
  assert.equal(f.history[1][0][0].runId,'one');assert.equal(f.errors.length,0);f.source.stop();
});

test('la pestaña oculta no consulta y vuelve a leer al hacerse visible',async()=>{
  let calls=0;
  const f=fixture(async()=>{calls++;return response(page());},true);
  f.source.subscribe({},f.callbacks);await settle();assert.equal(calls,0);
  f.visibility(false);await settle();assert.equal(calls,2);
  f.visibility(true);assert.equal(f.timers.size,0);
  f.visibility(false);await settle();assert.equal(calls,4);f.source.stop();
});

test('detener o cambiar de fuente cancela respuestas antiguas',async()=>{
  const pending=[];
  const f=fixture((url,options)=>new Promise(resolve=>pending.push({resolve,signal:options.signal})));
  f.source.subscribe({},f.callbacks);f.source.stop();
  assert.ok(pending.every(p=>p.signal.aborted));pending.forEach(p=>p.resolve(response(page())));await settle();
  assert.equal(f.history.length,0);assert.equal(f.live.length,0);assert.equal(f.timers.size,0);
});

test('un fallo del directo conserva participantes históricos e informa del error',async()=>{
  const f=fixture(async url=>url.endsWith('/history')?response(page([],[{report:{runId:'ended'},updatedAt:1}])):new Response(null,{status:503}));
  f.source.subscribe({},f.callbacks);await settle();
  assert.equal(f.live[0][0].runId,'ended');assert.equal(f.errors.length,1);assert.equal(f.errors[0][1],true);f.source.stop();
});

test('rechaza respuestas incompatibles y mantiene acotado el contenido',async()=>{
  const f=fixture(async()=>response({...page(),reports:new Array(501).fill({})}));
  f.source.subscribe({},f.callbacks);await settle();assert.equal(f.history.length,0);assert.equal(f.errors.length,2);f.source.stop();
});

test('el panel local de Windows usa el servidor público sin configurar Firebase',()=>{
  assert.equal(defaultApiRoot({hostname:'localhost',origin:'http://localhost:8080',protocol:'http:'}),'https://familiamelgar.duckdns.org/api/dashboard');
  assert.equal(defaultApiRoot({hostname:'familiamelgar.duckdns.org',origin:'https://familiamelgar.duckdns.org',protocol:'https:'}),'https://familiamelgar.duckdns.org/api/dashboard');
});
