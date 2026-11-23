import test from 'node:test';
import assert from 'node:assert/strict';
import {exportRows,encodeCsv} from '../web/csv-export.mjs';
import {escapeHtml,formatNumber,compactNumber,formatDuration} from '../web/format.mjs';

const report = {
  runId:'1'.repeat(32),day:'2026-11-23',mode:'arena',map:'classic',status:'completed',
  seconds:10,humans:1,bots:1,metrics:[
    {group:'humanElement',key:7,measure:'damage',value:25},
    {group:'botElement',key:7,measure:'damage',value:50}
  ]
};
const player = {id:1,name:'=SUM(A1)',bot:false,kills:3,deaths:1,
  health:[25,0,0,0,0,0,0,0,0],shield:[5,0,0,0,0,0,0,0,0]};
const session = {...report,network:true,reporterId:1,players:[player,{...player,id:2,bot:true}],
  ping:{samples:4,sumMs:100,minMs:20,maxMs:35}};
const data = {reports:[report],sessions:[session],streams:[session],totals:{elements:[]}};

test('la exportación conserva todas las partidas y el filtro de bots fuera de la paginación',()=>{
  const reports = Array.from({length:83},(_,index)=>({...report,runId:String(index)}));
  const rows = exportRows('matches',{...data,reports},{bots:false,status:'completed'});
  assert.equal(rows.length,84);
  assert.equal(rows[1][8],25);
  assert.deepEqual(rows[1].slice(9),['','','','']);
  assert.equal(exportRows('overview',data,{bots:true,status:'all'})[1][8],75);
});

test('la exportación de jugadores y conexión respeta cierres y estados seleccionados',()=>{
  const source = {...data,sessions:[session,{...session,status:'playing'},{...session,status:'left'}]};
  const filters={bots:false,status:'completed'};
  const rows=exportRows('players',source,filters);
  assert.deepEqual(rows.slice(1),[[player.name,report.runId,report.day,false,30,3,1]]);
  const live={...data,streams:[session,{...session,status:'left'},{...session,network:false}]};
  assert.deepEqual(exportRows('connection',live,filters).slice(1),[[report.runId,player.name,4,25,20,35]]);
});

test('CSV conserva UTF-8, delimitadores y líneas sin ejecutar fórmulas de nombres importados',()=>{
  const csv=encodeCsv([['á;"nombre"',null,'=1+1','+1','-1','@SUM(A1)','\t=1','\r=1']]);
  assert.equal(csv.charCodeAt(0),0xFEFF);
  assert.equal(csv.slice(1),'"á;""nombre""";"";"\'=1+1";"\'+1";"\'-1";"\'@SUM(A1)";"\'\t=1";"\'\r=1"');
  assert.equal(encodeCsv([[1],[2]]).slice(1),'"1"\r\n"2"');
});

test('el formato conserva cifras españolas, ausencias y nombres sin HTML ejecutable',()=>{
  assert.equal(formatNumber(1234.56,1),'1234,6');
  assert.equal(formatNumber(0), '0');
  assert.equal(formatNumber(null), '—');
  assert.equal(formatNumber(undefined), '—');
  assert.equal(compactNumber(1250000),'1,25 M');
  assert.equal(formatDuration(5400),'1,5 h');
  assert.equal(escapeHtml('<img src="x" onerror=\'a&b\'>'),'&lt;img src=&quot;x&quot; onerror=&#39;a&amp;b&#39;&gt;');
});
