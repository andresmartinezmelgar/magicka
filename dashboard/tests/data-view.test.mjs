import test from 'node:test';
import assert from 'node:assert/strict';
import {createDashboardData} from '../web/data-view.mjs';
import {createDashboardViews} from '../web/views.mjs';
import {liveRuns} from '../web/model.mjs';
import {indexLiveStreams,projectLiveRuns} from '../web/telemetry.mjs';

const now=Date.parse('2026-11-23T12:00:00Z');
const report = (id='a',extra={}) => ({schemaVersion:1,runId:id.repeat(32),revision:600,
  gameVersion:'0.29.73',day:'2026-11-23',mode:'defense',map:'bastion',network:true,
  status:'completed',seconds:10,humans:1,bots:1,difficulty:2,winnerTeam:0,scores:[2,0],metrics:[
    {group:'humanElement',key:7,measure:'damage',value:10},
    {group:'botElement',key:7,measure:'damage',value:20}
  ],...extra});
const filters = extra => ({start:'2026-11-23',end:'2026-11-23',mode:'',status:'completed',bots:true,
  range:{start:'2026-11-23',end:'2026-11-23'},...extra});
const player = {id:1,name:'<script>nombre</script>',team:0,bot:false,wizardType:0,kills:2,deaths:1,
  alive:true,health:[10,0,0,0,0,0,0,0,0],shield:[2,0,0,0,0,0,0,0,0]};
const session = (extra={}) => {
  const {humans,bots,difficulty,winnerTeam,scores,metrics,...header}=report();
  return {...header,players:[player],reporterId:1,updatedAt:now,status:'playing',...extra};
};

test('la vista recalcula los filtros y los bots sin conservar totales de otra selección',()=>{
  const data=createDashboardData();
  data.setHistory([report(),report('b',{mode:'arena',map:'classic',status:'left',gameVersion:'0.29.72'}),
    report('c',{day:'2026-11-22'})]);
  const state=filters();
  assert.deepEqual([data.read(state,now).totals.matches,data.read(state,now).totals.damage],[1,30]);
  state.bots=false;assert.equal(data.read(state,now).totals.damage,10);
  state.status='all';assert.equal(data.read(state,now).totals.damage,20);
  state.mode='arena';assert.equal(data.read(state,now).totals.matches,1);
  state.status='completed';assert.equal(data.read(state,now).totals.matches,0);
  Object.assign(state,{mode:'',status:'all',version:'0.29.72'});
  assert.equal(data.read(state,now).reports[0].mode,'arena');
  Object.assign(state,{version:'',start:'2026-11-22'});state.range.start=state.start;
  assert.equal(data.read(state,now).totals.matches,3);
});

test('buscar, paginar y cambiar de pestaña reutiliza la validación y los totales',()=>{
  let historyReads=0,liveReads=0;
  const data=createDashboardData();
  data.setHistory([{get payload(){historyReads++;return JSON.stringify(report());}}]);
  data.setTelemetry([{updatedAt:now,get payload(){liveReads++;return JSON.stringify(session());}}]);
  const state=filters(),before=data.read(state,now),reads=[historyReads,liveReads];
  assert.ok(historyReads>0&&liveReads>0);
  for (const view of ['overview','players','matches','live']) {
    Object.assign(state,{view,search:'nombre',extraRows:150});
    const next=data.read(state,now);
    assert.strictEqual(next.reports,before.reports);
    assert.strictEqual(next.totals,before.totals);
    assert.strictEqual(next.streams,before.streams);
  }
  assert.deepEqual([historyReads,liveReads],reads);
});

test('el reloj sigue caducando el directo aunque el historial no cambie',()=>{
  const data=createDashboardData();data.setHistory([report()]);data.setTelemetry([session()]);
  const state=filters(),before=data.read(state,now);
  assert.equal(before.sessions[0].active,true);
  assert.equal(data.read(state,now+89999).sessions[0].active,true);
  assert.equal(data.read(state,now+90000).sessions[0].active,false);
  assert.equal(data.read(state,now-30000).sessions[0].active,true);
  assert.equal(data.read(state,now-30001).sessions[0].active,false);
  assert.strictEqual(data.read(state,now+90000).totals,before.totals);
});

test('una nueva entrega de la misma fila invalida también el índice de métricas',()=>{
  const records=[report()],data=createDashboardData(),state=filters();
  data.setHistory(records);assert.equal(data.read(state,now).totals.damage,30);
  records[0].metrics[0].value=70;
  data.setHistory(records);assert.equal(data.read(state,now).totals.damage,90);
  records.push(report('b'));data.setHistory(records);
  assert.equal(data.read(state,now).documents,2);
  assert.equal(data.read(state,now).totals.matches,2);
  data.reset();const empty=data.read(state,now);
  assert.equal(empty.documents,0);assert.equal(empty.totals.damage,0);
  assert.deepEqual(empty.sessions,[]);assert.deepEqual(empty.streams,[]);
});

test('errores y revisiones de telemetría no se acumulan al volver a leer',()=>{
  const data=createDashboardData(),records=[session(),session({mode:'arena',map:'classic'}),null];
  data.setTelemetry(records);
  for(let i=0;i<4;i++){
    const next=data.read(filters(),now+i);
    assert.equal(next.liveInvalid,2);assert.equal(next.sessions.length,1);
    assert.equal(next.streams.length,2);
  }
  const indexed=indexLiveStreams(records);
  assert.deepEqual(projectLiveRuns(indexed,now),liveRuns(records,now));
  assert.deepEqual(projectLiveRuns(indexed,now+90000),liveRuns(records,now+90000));
});

test('los filtros de historial y directo conservan sus rangos y estados independientes',()=>{
  const data=createDashboardData();data.setHistory([report()]);
  data.setTelemetry([session(),session({runId:'b'.repeat(32),day:'2026-11-22'})]);
  const state=filters();assert.equal(data.read(state,now).sessions.length,1);
  state.range.start='2026-11-22';assert.equal(data.read(state,now).sessions.length,2);
  assert.equal(data.read(state,now).reports.length,1);
  state.range.end='2026-11-22';assert.equal(data.read(state,now).sessions[0].day,'2026-11-22');
});

test('las seis vistas mantienen los datos compartidos y escapan los nombres de jugadores',()=>{
  const data=createDashboardData();data.setHistory([report()]);
  data.setTelemetry([session(),session({runId:'b'.repeat(32),status:'completed'})]);
  const state=filters({search:'',view:'overview'}),value=data.read(state,now);
  const {renderers}=createDashboardViews(state,()=>50),before=JSON.stringify(value);
  for(const [view,render] of Object.entries(renderers)){
    state.view=view;const html=render(value);
    assert.equal(typeof html,'string');assert.ok(html.length>0);
    assert.ok(!html.includes('<script>nombre</script>'));
    if(view==='live'||view==='players')assert.ok(html.includes('&lt;script&gt;nombre&lt;/script&gt;'));
    assert.equal(JSON.stringify(value),before,view+' alteró el modelo compartido');
  }
});
