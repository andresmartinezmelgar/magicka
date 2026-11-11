import test from 'node:test';
import assert from 'node:assert/strict';
import {parseReport,deduplicate,isNewer,dayRange,aggregate,pingSummary,liveRuns,playersRanking,filterReports,matchPlayers} from '../web/model.mjs';
const seed = {schemaVersion:1, runId:'1'.repeat(32), revision:600, gameVersion:'0.29.53',
  day:'2026-11-07', mode:'defense', map:'bastion', network:true, status:'completed',
  seconds:10, humans:1, bots:1, difficulty:3, winnerTeam:0, scores:[2,1], metrics:[]};
const {humans, bots, difficulty, winnerTeam, scores, metrics, ...session} = seed;
const player = {id:1, name:'Mago', team:0, bot:false, wizardType:0, kills:3, deaths:1, alive:true,
  health:[10,0,0,0,0,0,0,20,0], shield:[0,0,0,0,0,0,0,5,0]};
const telemetry = {...session, players:[player, {...player,id:2,name:'Bot',bot:true}], reporterId:1,
  updatedAt:Date.parse('2026-11-07T12:00:00Z')};
test('7/30 días UTC inclusivos cruzan mes y año',()=>{assert.deepEqual(dayRange(7,new Date('2027-01-03T22:00:00Z')),{start:'2026-12-28',end:'2027-01-03'});assert.deepEqual(dayRange(30,new Date('2026-03-01T00:00:00Z')),{start:'2026-01-31',end:'2026-03-01'});});
test('revisiones acumuladas, finalización y cabeceras incompatibles',()=>{const a={...seed,status:'left',revision:100,seconds:100/60};const b={...a,status:'completed',revision:200,seconds:200/60};const stale={...a,revision:300,seconds:5};assert.equal(isNewer(stale,b),false);assert.equal(isNewer({...b,revision:50},a),false);const out=deduplicate([a,b,stale,{...b,map:'practice'}]);assert.equal(out.reports.length,1);assert.equal(out.reports[0].revision,200);assert.equal(out.conflicts,1);});
test('documentos con payload y métricas dañadas no bloquean el resto',()=>{const r=deduplicate([{payload:JSON.stringify(seed)},'{',{...seed,metrics:[{group:'total',key:0,measure:'damage',value:-1}]},{...seed,runId:'wrong'}]);assert.equal(r.reports.length,1);assert.equal(r.invalid,3);});
test('deduplicar antes de sumar evita contar una partida dos veces',()=>{const out=deduplicate([seed,seed,{...seed,revision:seed.revision+60,seconds:seed.seconds+1}]);assert.equal(aggregate(out.reports).matches,1);});
test('proyecciones de daño y exceso no se suman entre sí; bots se filtran',()=>{const r={...seed,metrics:[{group:'total',key:0,measure:'damage',value:100},{group:'humanElement',key:7,measure:'damage',value:20},{group:'humanElement',key:7,measure:'shieldDamage',value:5},{group:'humanElement',key:7,measure:'overkill',value:90},{group:'botElement',key:7,measure:'damage',value:15},{group:'actor',key:0,measure:'damage',value:20}]};assert.equal(aggregate([r]).damage,40);assert.equal(aggregate([r],false).damage,25);});
test('FPS y ping son medias ponderadas, no medias de medias',()=>{const a={...seed,metrics:[{group:'performance',key:2,measure:'samples',value:100},{group:'performance',key:2,measure:'valueSum',value:6000}]};const b={...seed,metrics:[{group:'performance',key:2,measure:'samples',value:10},{group:'performance',key:2,measure:'valueSum',value:300}]};assert.equal(aggregate([a,b]).fps,6300/110);const p=pingSummary([{...telemetry,ping:{samples:100,sumMs:2000,minMs:10,maxMs:40}},{...telemetry,reporterId:2,ping:{samples:10,sumMs:1000,minMs:90,maxMs:110}},{...telemetry,ping:{samples:5,sumMs:100,minMs:10,maxMs:30}}]);assert.equal(p.mean,3000/110);assert.equal(p.samples,110);assert.equal(p.max,110);assert.equal(pingSummary([{...telemetry,network:false}]).mean,null);});
test('heartbeat caducado no es partida en directo; varios remitentes no duplican',()=>{const r={...telemetry,status:'playing',updatedAt:1000000};let out=liveRuns([r,{...r,id:'another',updatedAt:1001000}],1100000);assert.equal(out.runs.length,1);assert.equal(out.runs[0].active,false);out=liveRuns([r],1000010);assert.equal(out.runs[0].active,true);});
test('nombres iguales son participaciones separadas; no jugadores únicos inventados',()=>{const rows=playersRanking([telemetry,{...telemetry,runId:'f'.repeat(32)}],false);assert.equal(rows.length,2);});
test('muestra consistente con IDs únicos y filtros semanales',()=>{
  const older={...seed,runId:'f'.repeat(32),day:'2026-10-31'};
  const d=deduplicate([seed,older]);
  assert.equal(d.duplicates,0);assert.equal(d.invalid,0);
  assert.equal(liveRuns([telemetry]).invalid,0);
  const f={...dayRange(7,new Date('2026-11-07')),mode:'',status:'completed'};
  assert.deepEqual(filterReports(d.reports,f),[seed]);
});

test('desglose de la partida usa una sola captura del cierre y respeta el filtro de bots',()=>{
  const playing={...telemetry,status:'playing'};
  const older={...telemetry,revision:540,seconds:9};
  const otherMatch={...telemetry,runId:'a'.repeat(32)};
  assert.equal(matchPlayers(seed,[playing,older,otherMatch]),null);

  const latest={...telemetry,reporterId:2,updatedAt:telemetry.updatedAt+1,
    players:[{...player,kills:4},telemetry.players[1]]};
  const streams=[latest,telemetry,{...latest,updatedAt:latest.updatedAt+1,map:'practice'}];
  assert.equal(matchPlayers(seed,streams).length,2);
  const rows=matchPlayers(seed,streams,false);
  assert.equal(rows.length,1);
  assert.deepEqual([rows[0].kills,rows[0].healthDamage,rows[0].shieldDamage,rows[0].damage],[4,30,5,35]);
  assert.deepEqual(matchPlayers(seed,[{...telemetry,players:[telemetry.players[1]]}],false),[]);
});

test('el informe de cierre desglosa humanos y bots sin telemetría adicional',()=>{
  const human = {slot:1,bot:false,wizardType:0,team:0,healthDamage:30,shieldDamage:5,kills:3,deaths:1};
  const bot = {...human,slot:2,bot:true,team:1,healthDamage:50,shieldDamage:2,kills:2,deaths:0};
  const archived = parseReport(JSON.stringify({...seed,participants:[human,bot]}));
  assert.deepEqual(matchPlayers(archived,[]).map(p => [p.name,p.damage,p.kills,p.deaths]),
    [['Bot 2',52,2,0],['Jugador 1',35,3,1]]);
  assert.deepEqual(matchPlayers(archived,[],false).map(p => p.name),['Jugador 1']);
  assert.equal(deduplicate([seed,archived]).reports[0].participants.length,2);
  assert.equal(deduplicate([archived,seed]).reports[0].participants.length,2);
  assert.equal(matchPlayers(seed,[]),null);
});

test('valida los contadores y rechaza identificadores externos en los participantes',()=>{
  const participant = {slot:1,bot:false,wizardType:0,team:0,healthDamage:0,shieldDamage:0,kills:0,deaths:0};
  for (const participants of [
    [participant,participant], [{...participant,slot:65}], [{...participant,kills:-1}],
    [{...participant,healthDamage:Infinity}], [{...participant,name:'dato privado'}]
  ]) assert.throws(() => parseReport({...seed,participants}),/Participante inválido/);
  assert.throws(() => parseReport({...seed,participantsPartial:true}),/Desglose inválido/);
});

test('apoyo se deriva de actores: curación y absorción no son daño elemental inventado',()=>{const r={...seed,metrics:[{group:'actor',key:0,measure:'healing',value:100},{group:'actor',key:16,measure:'healing',value:50},{group:'actor',key:0,measure:'shieldAbsorbed',value:80},{group:'total',key:0,measure:'shieldDamage',value:80}]};const t=aggregate([r]);assert.equal(t.elements[1].healing,150);assert.equal(t.elements[2].absorbed,80);assert.equal(t.absorbed,80);assert.equal(aggregate([r],false).elements[1].healing,100);assert.equal(t.damage,0);});
