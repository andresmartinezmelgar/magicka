import {icon,tag,empty} from './view-components.mjs';
import {createDashboardViews} from './views.mjs';
import {createDashboardData} from './data-view.mjs';
import {escapeHtml as esc,formatNumber as fmt,formatDuration as duration,formatTime as stamp} from './format.mjs';
import {exportRows,encodeCsv} from './csv-export.mjs';
import {MODES,MAPS,dayRange,deduplicate,aggregate,matchPlayers} from './model.mjs';
import * as cloud from './public-data.mjs';
import {hasSelectionData,selectionDetails} from './selection-view.mjs';
const $=s=>document.querySelector(s);
const views={overview:['Resumen','Todo lo que ocurre.','Partidas, elementos y jugadores en una sola vista.'],live:['En directo','La arena, en este instante.','Partidas activas, participantes y daño acumulado.'],matches:['Partidas','Cada partida cuenta.','Consulta la actividad y abre una partida para ver su desglose.'],players:['Jugadores','Quienes marcan la diferencia.','Daño, bajas y participación dentro de cada partida.'],elements:['Elementos y ataques','El poder de cada elemento.','Compara el daño efectivo, las formas de ataque y su origen.'],connection:['Conexión','El ritmo de la conexión.','Ping ponderado, muestras recibidas y rendimiento del juego.']};
let remembered={};try{remembered=JSON.parse(sessionStorage.getItem('arcana-dashboard-view')||'{}');}catch{}
const state={view:location.hash.slice(1) in views?location.hash.slice(1):remembered.view||'overview',days:remembered.days||7,range:dayRange(remembered.days===30?30:7),mode:remembered.mode||'',status:remembered.status||'completed',bots:remembered.bots!==false,source:'public',historyReady:false,liveReady:false,cache:false,updated:0,error:'',liveError:'',loading:true,search:'',extraRows:0};
if(!(state.view in views))state.view='overview';
const PAGE_SIZE = 50;
const rowLimit = () => (state.view === 'overview' ? 5 : PAGE_SIZE) + state.extraRows;
const {renderers,playerDetails} = createDashboardViews(state,rowLimit);
const dashboardData = createDashboardData();
function persist(){try{sessionStorage.setItem('arcana-dashboard-view',JSON.stringify({view:state.view,days:state.days,mode:state.mode,status:state.status,bots:state.bots}));}catch{}}
const data = () => dashboardData.read(state);
// The historical report filter uses start/end fields; keep a single normalized range.
function normalize(){state.start=state.range.start;state.end=state.range.end;}
function render(){normalize();persist();const d=data();$('#navigation').innerHTML=Object.entries(views).map(([key,v])=>`<a href="#${key}" data-view="${key}" class="${state.view===key?'active':''}" ${state.view===key?'aria-current="page"':''} title="${v[0]}">${icon(key)}<span class="nav-name">${v[0]}</span>${key==='live'&&d.sessions.some(r=>r.active)?'<span class="nav-count">'+d.sessions.filter(r=>r.active).length+'</span>':''}</a>`).join('');$('#crumb').textContent=views[state.view][0];$('#page-title').textContent=views[state.view][1];$('#page-subtitle').textContent=views[state.view][2];
 const imported = state.source === 'import';
 const hasHistory = imported || state.historyReady;
 const inLiveView = state.view === 'live';
 const available = inLiveView ? imported || state.liveReady : hasHistory;
 const cached = inLiveView ? state.publicCoverage?.stale : state.cache || !!state.error;
 const status = $('#source-status');
 status.classList.toggle('connected', available && !imported && !cached);
 const label = imported ? 'Archivo importado' : available ? (cached ? 'Datos en caché' : 'Servidor conectado')
   : state.loading ? 'Conectando…' : 'Sin conexión';
 status.innerHTML = '<i></i>' + label;

 let notice = 'Las estadísticas se consultan automáticamente sin iniciar sesión.';
 if (imported) notice = 'Datos del informe importado.';
 else if (available) {
   notice = 'Estadísticas públicas de solo lectura.';
   if (inLiveView && state.error) notice = 'El directo está disponible. El historial está pendiente de conexión.';
   else if (state.error) notice = 'Se muestran los últimos datos recibidos. ' + state.error;
   else if (state.liveError) notice = 'Historial disponible. Datos en directo no accesibles. ' + state.liveError;
   else if (cached) notice = 'Se muestra la última copia disponible.';
 }
 $('#notice').textContent = notice;

 const error = inLiveView ? state.liveError : state.error;
 $('#content').innerHTML = available ? renderers[state.view](d)
   : state.loading ? empty('Cargando estadísticas', 'Consultando el servidor de Arcana Arena.')
   : empty('Datos no accesibles', esc(error || 'El servidor aún no ha devuelto estadísticas para esta sección.'), true);
 $('#coverage').textContent=hasHistory?`${state.start} — ${state.end} UTC · ${fmt(d.reports.length)} partidas · ${fmt(d.documents)} documentos${d.invalid+d.liveInvalid?' · '+fmt(d.invalid+d.liveInvalid)+' inválidos omitidos':''}${d.conflicts?' · '+d.conflicts+' cabeceras incompatibles':''}${d.totals.dropped?' · Desglose incompleto en origen':''}`:'Historial sin datos cargados';
 if(state.source==='public'&&state.publicCoverage){const c=state.publicCoverage;$('#coverage').textContent+=` · Disponible ${c.start} — ${c.end} UTC${c.partial?' · Cobertura parcial por límites o registros omitidos':''}${c.stale?' · Última copia disponible':''}`;}
 $('#updated').textContent=state.updated?'Última actualización '+stamp(state.updated):'Esperando conexión';
 $('#export').disabled=!hasHistory;
}
function setView(view){if(!(view in views))return;state.view=view;state.extraRows=0;state.search='';history.replaceState(null,'','#'+view);render();window.scrollTo({top:0,behavior:'instant'});}
function showSettings(){if(!$('#settings').open)$('#settings').showModal();}
function connect() {
  if (state.source === 'import') {
    dashboardData.reset();
    Object.assign(state, {source:'public', historyReady:false,
      liveReady:false, publicCoverage:null, cache:false, updated:0});
  }
  state.loading = true;
  state.error = state.liveError = '';
  render();
  cloud.subscribe(state.range, {
    history(records, cache) {
      dashboardData.setHistory(records);
      Object.assign(state, {historyReady:true, cache, error:'', loading:false, updated:Date.now()});
      render();
    },
    live(records, available) {
      dashboardData.setTelemetry(records);
      state.liveReady = available;
      if (available) state.liveError = '';
      render();
    },
    coverage(value) {
      state.publicCoverage = value;
      if (state.days !== 'custom') state.range = dayRange(state.days);
      for (const id of ['start-date', 'end-date']) {
        const input = $('#' + id);
        input.min = value.start;
        input.max = value.end;
      }
      render();
    },
    error(error, isLive) {
      if (isLive) {
        state.liveReady = false;
        state.liveError = cloud.friendlyError(error);
      } else {
        state.error = cloud.friendlyError(error);
        state.loading = false;
      }
      render();
    }
  });
}
function changeRange(){if(state.days!=='custom')state.range=dayRange(state.days);else{const start=$('#start-date').value,end=$('#end-date').value;if(!start||!end||start>end||(Date.parse(end)-Date.parse(start))/86400000>366){$('#notice').textContent='Selecciona un intervalo válido de hasta 367 días.';return;}state.range={start,end};}state.extraRows=0;render();}

function showDetail(id) {
  const d = data();
  const report = d.reports.find(r => r.runId === id);
  if (!report) return;
  const t = aggregate([report], state.bots);
  const players = matchPlayers(report, d.streams, state.bots);
  const totals = report.participants?.length && !report.participantsPartial
    ? players.reduce((sum, p) => ({
      damage:sum.damage+p.damage, kills:sum.kills+p.kills, deaths:sum.deaths+p.deaths
    }), {damage:0, kills:0, deaths:0})
    : null;
  $('#detail-body').innerHTML = `<p>${MODES[report.mode]} · ${MAPS[report.map]} · ${esc(report.day)} UTC ${tag(report)}</p>
    <small>#${report.runId} · v${esc(report.gameVersion)}</small>
    <div class="detail-grid">
      <div class="detail-stat"><span>Daño total ${state.bots ? 'de jugadores' : 'de humanos'}</span><b>${fmt(totals?.damage ?? t.damage,1)}</b></div>
      ${totals ? `<div class="detail-stat"><span>Bajas / muertes ${state.bots ? 'de jugadores' : 'de humanos'}</span><b>${fmt(totals.kills)} / ${fmt(totals.deaths)}</b></div>` : ''}
      <div class="detail-stat"><span>Duración</span><b>${duration(report.seconds)}</b></div>
      <div class="detail-stat"><span>Humanos / bots</span><b>${report.humans} / ${report.bots}</b></div>
      <div class="detail-stat"><span>Dificultad</span><b>${fmt(report.difficulty)}</b></div>
    </div>
    ${hasSelectionData(t.selection) ? `<section class="detail-players"><h3>Selección de elementos · total humano</h3>${selectionDetails(t.selection)}</section>` : ''}
    ${playerDetails(report, players)}
    ${elementBars(t)}
    <p class="help">${report.continuation ? 'Registro continuado tras recuperar una partida. ' : ''}${report.network ? 'Partida en red.' : 'Partida local.'} ${report.droppedBuckets ? 'Hay métricas de detalle omitidas en origen.' : ''}</p>`;
  $('#detail').showModal();
}

function showMore() {
  const firstNewRow = rowLimit();
  state.extraRows += PAGE_SIZE;
  render();
  const row = document.querySelectorAll('#content tr[data-run]')[firstNewRow];
  row?.focus({preventScroll:true});
  row?.scrollIntoView({block:'nearest'});
}

function exportCsv() {
 const blob = new Blob([encodeCsv(exportRows(state.view,data(),state))],{type:'text/csv;charset=utf-8'});
 const url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download=`arcana-${state.view}-${state.start}.csv`;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
}
$('#mode').value=state.mode;$('#record-status').value=state.status;$('#include-bots').checked=state.bots;$('#start-date').value=state.range.start;$('#end-date').value=state.range.end;
for(const b of document.querySelectorAll('[data-days]')){b.classList.toggle('selected',String(state.days)===b.dataset.days);b.setAttribute('aria-pressed',String(String(state.days)===b.dataset.days));b.addEventListener('click',()=>{state.days=b.dataset.days==='custom'?'custom':Number(b.dataset.days);document.querySelectorAll('[data-days]').forEach(el=>{el.classList.toggle('selected',el===b);el.setAttribute('aria-pressed',String(el===b));});$('#date-inputs').hidden=state.days!=='custom';changeRange();});}
$('#mode').onchange=e=>{state.mode=e.target.value;state.extraRows=0;render();};$('#record-status').onchange=e=>{state.status=e.target.value;state.extraRows=0;render();};$('#include-bots').onchange=e=>{state.bots=e.target.checked;render();};$('#start-date').onchange=changeRange;$('#end-date').onchange=changeRange;
$('#connection-button').onclick=showSettings;$('#refresh').onclick=()=>state.source==='import'?showSettings():connect();$('#export').onclick=exportCsv;
$('#fullscreen').onclick=async()=>{try{if(document.fullscreenElement)await document.exitFullscreen();else await document.documentElement.requestFullscreen();}catch{$('#notice').textContent='Usa F11 para abrir la pantalla completa en Windows.';}};
$('#server-data').onclick=()=>{$('#settings').close();connect();};
$('#import').onchange=async e=>{try{const f=e.target.files[0];if(!f)return;if(f.size>25*1024*1024)throw Error('El archivo supera 25 MB.');const v=JSON.parse(await f.text());const raw=Array.isArray(v)?v:Array.isArray(v.reports)?v.reports:v.payload||v.runId?[v]:null;if(!raw)throw Error('El archivo no contiene informes.');const parsed=deduplicate(raw);if(!parsed.reports.length)throw Error('No hay informes válidos en el archivo.');cloud.stop();dashboardData.setHistory(raw);dashboardData.setTelemetry(Array.isArray(v.telemetry)?v.telemetry:[]);state.source='import';state.error='';state.liveError='';state.updated=Date.now();state.loading=false;$('#settings').close();render();}catch(e){$('#settings-error').textContent=e.message;}finally{e.target.value='';}};
document.addEventListener('click',e=>{const view=e.target.closest('[data-view]');if(view){e.preventDefault();setView(view.dataset.view);return;}const action=e.target.closest('[data-action]')?.dataset.action;if(action==='connect')connect();if(action==='more')showMore();const row=e.target.closest('[data-run]');if(row)showDetail(row.dataset.run);});
document.addEventListener('keydown',e=>{if((e.key==='Enter'||e.key===' ')&&e.target.matches('[data-run]')){e.preventDefault();showDetail(e.target.dataset.run);}});
document.addEventListener('input',e=>{if(e.target.id==='player-search'){const pos=e.target.selectionStart;state.search=e.target.value;render();$('#player-search').focus();$('#player-search').setSelectionRange(pos,pos);}});
window.addEventListener('hashchange',()=>setView(location.hash.slice(1)));
window.addEventListener('online',()=>{if(state.source==='public')connect();});
window.addEventListener('offline', () => {
  if (state.source !== 'public') return;
  state.loading = state.liveReady = false;
  state.error = state.liveError = 'Sin conexión de red.';
  render();
});
setInterval(()=>{$('#clock').textContent=new Date().toLocaleTimeString('es-ES',{hour:'2-digit',minute:'2-digit'});},1000);
setInterval(()=>{if(!document.hidden&&state.view==='live')render();},15000);
let version;async function checkVersion(){try{const response=await fetch('./version.json',{cache:'no-store'});if(!response.ok)return;const next=(await response.json()).version;if(version&&next!==version&&!document.querySelector('dialog[open]'))location.reload();version=next;}catch{}}
connect();checkVersion();setInterval(()=>{if(!document.hidden)checkVersion();},60000);
