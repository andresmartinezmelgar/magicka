import {ELEMENTS,COLORS,MODES,MAPS,STATUS_NAMES,CLASSES,dayRange,utcDay,deduplicate,filterReports,aggregate,metric,liveRuns,pingSummary,playersRanking,matchPlayers} from './model.mjs';
import * as cloud from './public-data.mjs';
const $=s=>document.querySelector(s);
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const fmt=(n,d=0)=>n===null||n===undefined?'—':new Intl.NumberFormat('es-ES',{maximumFractionDigits:d}).format(n);
const compact=n=>n>=1e6?fmt(n/1e6,2)+' M':n>=10000?fmt(n/1000,1)+' mil':fmt(n);
const duration=n=>n>=3600?fmt(n/3600,1)+' h':fmt(n/60,1)+' min';
const stamp=n=>n?new Date(n).toLocaleTimeString('es-ES',{hour:'2-digit',minute:'2-digit',second:'2-digit'}):'—';
const paths={overview:'M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z',live:'M3 12h4l3-8 4 16 3-8h4',matches:'M5 4h14v17H5z M9 2v4 M15 2v4 M8 10h8 M8 14h5',players:'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8 M17 4a4 4 0 0 1 0 7 M22 21v-2a4 4 0 0 0-3-3.9',elements:'m13 2-9 12h7l-1 8 10-13h-7z',connection:'M2 8a16 16 0 0 1 20 0 M5 12a11 11 0 0 1 14 0 M8 16a6 6 0 0 1 8 0 M12 20h.01'};
const icon=(name)=>`<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="${paths[name]||paths.elements}"/></svg>`;
const views={overview:['Resumen','Todo lo que ocurre.','Partidas, elementos y jugadores en una sola vista.'],live:['En directo','La arena, en este instante.','Partidas activas, participantes y daño acumulado.'],matches:['Partidas','Cada partida cuenta.','Consulta la actividad y abre una partida para ver su desglose.'],players:['Jugadores','Quienes marcan la diferencia.','Daño, bajas y participación dentro de cada partida.'],elements:['Elementos y ataques','El poder de cada elemento.','Compara el daño efectivo, las formas de ataque y su origen.'],connection:['Conexión','El ritmo de la conexión.','Ping ponderado, muestras recibidas y rendimiento del juego.']};
let remembered={};try{remembered=JSON.parse(sessionStorage.getItem('arcana-dashboard-view')||'{}');}catch{}
const state={view:location.hash.slice(1) in views?location.hash.slice(1):remembered.view||'overview',days:remembered.days||7,range:dayRange(remembered.days===30?30:7),mode:remembered.mode||'',status:remembered.status||'completed',bots:remembered.bots!==false,raw:[],telemetry:[],source:'public',historyReady:false,liveReady:false,cache:false,updated:0,error:'',liveError:'',loading:true,search:'',extraRows:0};
if(!(state.view in views))state.view='overview';
const PAGE_SIZE = 50;
const rowLimit = () => (state.view === 'overview' ? 5 : PAGE_SIZE) + state.extraRows;
function persist(){try{sessionStorage.setItem('arcana-dashboard-view',JSON.stringify({view:state.view,days:state.days,mode:state.mode,status:state.status,bots:state.bots}));}catch{}}
function data(){const d=deduplicate(state.raw),reports=filterReports(d.reports,state);const live=liveRuns(state.telemetry);const streams=live.streams.filter(r=>r.day>=state.range.start&&r.day<=state.range.end&&(!state.mode||r.mode===state.mode));const sessions=live.runs.filter(r=>r.day>=state.range.start&&r.day<=state.range.end&&(!state.mode||r.mode===state.mode));return {...d,reports,totals:aggregate(reports,state.bots),sessions,streams,liveInvalid:live.invalid};}
// The historical report filter uses start/end fields; keep a single normalized range.
function normalize(){state.start=state.range.start;state.end=state.range.end;}
function panel(title,subtitle,body,action=''){return `<section class="panel"><div class="panel-head"><div><h2>${title}</h2>${subtitle?`<p>${subtitle}</p>`:''}</div>${action}</div>${body}</section>`;}
const kpi=(label,value,foot,type='',unit='')=>`<div class="kpi ${type}"><div class="kpi-label">${label}${icon(label.includes('Ping')?'connection':label.includes('Daño')?'elements':label.includes('Particip')?'players':'matches')}</div><div class="kpi-value">${value}${unit?` <em>${unit}</em>`:''}</div><div class="kpi-foot">${foot}</div></div>`;
const tag=r=>`<span class="tag ${r.status==='completed'?'':r.active?'live':'stale'}">${r.active?'En directo':r.status==='playing'?'Sin señal reciente':STATUS_NAMES[r.status]}</span>`;
function empty(title,text,buttons=false){return `<section class="panel empty"><div class="empty-mark">${icon('live')}</div><div class="eyebrow">OBSERVATORIO DE ARCANA ARENA</div><h2>${title}</h2><p>${text}</p>${buttons?'<div class="empty-actions"><button class="button primary" data-action="connect">Reintentar</button></div>':''}</section>`;}
function elementBars(t){const offensive=t.elements.filter(e=>![1,2].includes(e.key));const max=Math.max(1,...offensive.map(e=>e.health+e.shield));const total=offensive.reduce((s,e)=>s+e.health+e.shield,0);return `<div class="panel-body">${offensive.sort((a,b)=>b.health+b.shield-a.health-a.shield).map(e=>`<div class="element-row"><span><i class="element-dot" style="background:${e.color}"></i>${e.name}</span><div class="track"><div class="fill" style="width:${(e.health+e.shield)/max*100}%;background:${e.color}"></div></div><span class="element-value">${compact(e.health+e.shield)}<small>${total?fmt((e.health+e.shield)/total*100):0}%</small></span></div>`).join('')}<div class="element-support"><span><i class="element-dot" style="background:${COLORS[1]}"></i>Curación <b>${compact(t.elements[1].healing)}</b></span><span><i class="element-dot" style="background:${COLORS[2]}"></i>Absorción <b>${compact(t.elements[2].absorbed)}</b></span></div></div>`;}
function trend(t){const days=[];let dt=new Date(state.start+'T00:00:00Z');for(let i=0;i<367&&utcDay(dt)<=state.end;i++,dt.setUTCDate(dt.getUTCDate()+1)){const day=utcDay(dt);days.push(t.timeline.find(x=>x.day===day)||{day,matches:0,damage:0});}const w=600,h=162,left=35,right=12,top=16,bottom=25;const max=Math.max(4,...days.map(d=>d.matches));const x=i=>left+i*(w-left-right)/Math.max(1,days.length-1);const y=v=>h-bottom-v/max*(h-top-bottom);const line=days.map((d,i)=>`${i?'L':'M'}${x(i).toFixed(1)},${y(d.matches).toFixed(1)}`).join(' ');const area=line+` L${x(days.length-1)},${h-bottom} L${left},${h-bottom} Z`;return `<div class="panel-body"><div class="legend"><span><i></i>Partidas finalizadas${state.status==='all'?' y otros cierres':''}</span><span>Fechas UTC</span></div><div class="chart"><svg viewBox="0 0 ${w} ${h}" role="img" aria-label="Partidas por día"><defs><linearGradient id="chart-gold" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#c5aa72" stop-opacity=".23"/><stop offset="1" stop-color="#c5aa72" stop-opacity="0"/></linearGradient></defs>${[0,.25,.5,.75,1].map(q=>`<line x1="${left}" x2="${w-right}" y1="${y(max*q)}" y2="${y(max*q)}"/><text x="${left-12}" y="${y(max*q)+3}" text-anchor="end">${fmt(max*q,1)}</text>`).join('')}<path d="${area}" fill="url(#chart-gold)"/><path class="trend-line" d="${line}"/>${days.map((d,i)=>`<circle cx="${x(i)}" cy="${y(d.matches)}" r="${days.length>14?2:3}" fill="#d3b57b"><title>${esc(d.day)}: ${d.matches} partidas</title></circle>`).join('')}${days.filter((d,i)=>i===0||i===days.length-1||i%Math.max(1,Math.ceil(days.length/6))===0).map(d=>`<text x="${x(days.indexOf(d))}" y="${h-3}" text-anchor="middle">${d.day.slice(8)}/${d.day.slice(5,7)}</text>`).join('')}</svg></div><div class="chart-summary"><span>Total <b>${fmt(t.matches)}</b></span><span>Tiempo jugado <b>${duration(t.seconds)}</b></span><span>Media por partida <b>${duration(t.matches?t.seconds/t.matches:0)}</b></span></div></div>`;}
function leaders(d){const rows=playersRanking(d.sessions.filter(r=>r.status!=='playing'&&(state.status==='all'||r.status===state.status)),state.bots);const top=rows.slice(0,4);return `<div class="panel-body">${top.length?top.map((p,i)=>`<div class="rank-row"><span class="rank-number">0${i+1}</span><span class="avatar ${i===0?'gold':''}">${esc(p.name.slice(0,2).toUpperCase())}</span><span class="rank-name">${esc(p.name)}<small>${CLASSES[p.wizardType]||'Mago'} · ${fmt(p.kills)} bajas</small></span><span class="rank-value">${compact(p.damage)}<small>daño efectivo</small></span></div>`).join(''):'<p class="empty-inline">Los informes antiguos agrupan por clase. Los nombres aparecerán cuando el nuevo cliente envíe telemetría.</p>'}<div class="table-note" style="padding-left:0;padding-bottom:0">Mejores actuaciones por partida · ${fmt(rows.length)} participaciones con nombre</div></div>`;}
function matchTable(reports){const limit=rowLimit();return `<div class="table-scroll"><table class="table"><thead><tr><th>Partida / modo</th><th>Fecha UTC</th><th>Duración</th><th>Humanos / bots</th><th class="number">Daño de jugadores</th><th>Estado</th></tr></thead><tbody>${reports.slice(0,limit).map(r=>`<tr data-run="${r.runId}" tabindex="0" role="button" aria-label="Ver partida ${r.runId.slice(-6)}"><td><div class="run-label"><span class="mode-icon">${icon(r.mode==='defense'?'elements':'matches')}</span><span><strong>${MODES[r.mode]}</strong><small style="display:block">${MAPS[r.map]} · #${r.runId.slice(-6)}</small></span></div></td><td>${r.day.slice(8)}/${r.day.slice(5,7)}<small style="display:block">${r.gameVersion}</small></td><td>${duration(r.seconds)}</td><td>${r.humans} <small>/ ${r.bots}</small></td><td class="number">${compact(aggregate([r],state.bots).damage)}</td><td>${tag(r)}</td></tr>`).join('')}</tbody></table></div>${reports.length>limit?`<div class="table-note"><button class="button secondary" data-action="more">Mostrar más (${fmt(reports.length-limit)} restantes)</button></div>`:''}`;}
function balance(d){const values=[['Bajas','kills'],['Muertes','deaths'],['Lanzamientos','casts'],['Reanimaciones','revives'],['Oro obtenido','goldEarned'],['Oro gastado','goldSpent'],['Fuego amigo','friendlyDamage'],['Daño propio','selfDamage'],['Escudo concedido','shieldGranted'],['Escudo reparado','shieldRepaired']];return panel('Balance del periodo','Totales de todos los actores · partidas filtradas',`<div class="panel-body"><div class="balance-grid">${values.map(([name,key])=>`<div><span>${name}</span><b>${compact(d.totals.general[key]||0)}</b></div>`).join('')}</div></div>`);}
function overview(d){const t=d.totals;const ping=pingSummary(d.streams.filter(r=>state.status==='all'||r.status===state.status));return `<div class="kpis">${kpi('Partidas registradas',fmt(t.matches),state.status==='completed'?'Finalizadas en el periodo':'Todos los cierres en el periodo')}${kpi('Daño de jugadores',compact(t.damage),'Vida + escudo · '+(state.bots?'humanos y bots':'solo humanos'),'highlight')}${kpi('Participaciones humanas',fmt(t.humans),'Suma de máximos por partida')}${kpi('Ping medio',fmt(ping.mean,1),ping.samples?fmt(ping.samples)+' respuestas recibidas':'Sin muestras en los informes antiguos','',ping.samples?'ms':'')}</div><div class="grid-main">${panel('Actividad de la arena','Partidas a lo largo del periodo seleccionado',trend(t),'<span class="panel-badge">'+state.start.slice(8)+' '+new Date(state.start+'T12:00:00Z').toLocaleDateString('es-ES',{month:'short',timeZone:'UTC'})+' — '+state.end.slice(8)+' '+new Date(state.end+'T12:00:00Z').toLocaleDateString('es-ES',{month:'short',timeZone:'UTC'})+'</span>')}${panel('Actuaciones destacadas','Las partidas con mayor daño individual',leaders(d),'<button class="text-link" data-view="players">Ver todas ↗</button>')}</div><div class="grid-half">${panel('Daño por elemento','Daño efectivo de vida y escudo',elementBars(t),'<button class="text-link" data-view="elements">Explorar ↗</button>')}${panel('Clases con más daño','Agrupación histórica por clase y tipo de actor',`<div class="panel-body">${t.classes.slice(0,5).map((c,i)=>`<div class="rank-row"><span class="avatar ${i===0?'gold':''}">${icon('elements')}</span><span class="rank-name">${esc(c.name)}<small>Acumulado del periodo</small></span><span class="rank-value">${compact(c.damage)}</span></div>`).join('')||'<p class="empty-inline">Sin desglose por clases.</p>'}</div>`)}</div>${balance(d)}<div style="height:20px"></div>${panel('Partidas del periodo','Ordenadas por día UTC · selecciona una para ver su detalle',matchTable([...d.reports].sort((a,b)=>b.day.localeCompare(a.day)||b.runId.localeCompare(a.runId))),'<button class="text-link" data-view="matches">Ver historial ↗</button>')}`;}
function liveView(d){const active=d.sessions.filter(r=>r.active);const stale=d.sessions.filter(r=>r.status==='playing'&&!r.active);return `<div class="kpis">${kpi('Partidas activas',fmt(active.length),'Señal recibida hace menos de 90 s')}${kpi('Humanos en directo',fmt(active.reduce((n,r)=>n+r.players.filter(p=>!p.bot).length,0)),'En partidas con telemetría')}${kpi('Bots en directo',fmt(active.reduce((n,r)=>n+r.players.filter(p=>p.bot).length,0)),'Participantes del estado actual')}${kpi('Sesiones sin señal',fmt(stale.length),'No se cuentan como activas')}</div><p class="section-note">La telemetría se envía aproximadamente cada 15 segundos desde un cliente autorizado. El estado «sin señal» indica que no se ha recibido una actualización reciente.</p>${active.length||stale.length?`<div class="session-grid">${[...active,...stale].map(r=>`<section class="panel"><div class="session-head"><div><h2>${MODES[r.mode]} · ${MAPS[r.map]}</h2><div class="session-meta">#${r.runId.slice(-6)} · ${duration(r.seconds)} · ${r.network?'En red':'Local'}</div></div>${tag(r)}</div><div class="players-mini">${r.players.filter(p=>state.bots||!p.bot).sort((a,b)=>b.health.reduce((s,v)=>s+v,0)-a.health.reduce((s,v)=>s+v,0)).map(p=>`<div class="rank-row"><span class="avatar">${esc(p.name.slice(0,2).toUpperCase())}</span><span class="rank-name">${esc(p.name)}<small>${p.bot?'Bot':'Humano'} · ${p.alive?'Con vida':'Sin vida'} · ${p.kills} bajas</small></span><span class="rank-value">${compact([...p.health,...p.shield].reduce((s,v)=>s+v,0))}<small>daño acumulado</small></span></div>`).join('')}</div><div class="table-note">Última señal ${stamp(r.updatedAt)} · Actualización automática</div></section>`).join('')}</div>`:empty('La arena está en silencio.','No hay partidas activas con una señal reciente. Las partidas cerradas se mantienen en el historial.')}`;}
function matchesView(d){const reports=[...d.reports].sort((a,b)=>b.day.localeCompare(a.day)||b.runId.localeCompare(a.runId));return `<p class="section-note">Una fila por partida, aunque se haya guardado, reanudado o enviado varias veces. La fecha corresponde al día de inicio del registro en UTC.</p>${reports.length?panel('Historial de partidas',fmt(reports.length)+' partidas en el periodo',matchTable(reports),'<span class="panel-badge">'+fmt(d.duplicates)+' revisiones agrupadas</span>'):empty('Sin partidas en este periodo.','Cambia el periodo, el modo o el estado para consultar otros registros.')}`;}
function playersView(d){const sessions=d.sessions.filter(r=>r.status!=='playing'&&(state.status==='all'||r.status===state.status));const rows=playersRanking(sessions,state.bots).filter(p=>p.name.toLocaleLowerCase().includes(state.search.toLocaleLowerCase()));return `<p class="section-note">Cada fila es la actuación de un jugador en una partida. Los nombres pueden repetirse y no identifican por sí solos a una misma persona entre partidas. El daño individual excluye daño propio y fuego amigo.</p><div class="search-row"><input id="player-search" aria-label="Buscar jugador" placeholder="Buscar jugador…" value="${esc(state.search)}"><small>${fmt(rows.length)} actuaciones · ${sessions.length} partidas con nombres</small></div>${rows.length?panel('Clasificación por daño','Vida + escudo de cada participación',`<div class="table-scroll"><table class="table"><thead><tr><th>Pos.</th><th>Jugador</th><th>Partida</th><th>Clase</th><th class="number">Daño</th><th class="number">Bajas</th><th class="number">Muertes</th><th class="number">Daño / s</th></tr></thead><tbody>${rows.slice(0,rowLimit()).map((p,i)=>`<tr><td>${String(i+1).padStart(2,'0')}</td><td><strong>${esc(p.name)}</strong><small style="display:block">${p.bot?'Bot':'Humano'} · Equipo ${p.team+1}</small></td><td>${p.day.slice(8)}/${p.day.slice(5,7)}<small style="display:block">#${p.runId.slice(-6)}</small></td><td>${CLASSES[p.wizardType]||'Mago'}</td><td class="number">${compact(p.damage)}</td><td class="number">${fmt(p.kills)}</td><td class="number">${fmt(p.deaths)}</td><td class="number">${fmt(p.seconds?p.damage/p.seconds:0,1)}</td></tr>`).join('')}</tbody></table></div>${rows.length>rowLimit()?'<div class="table-note"><button class="button secondary" data-action="more">Mostrar más</button></div>':''}`):empty('Todavía no hay actuaciones con nombre.','El historial anterior es anónimo. Esta pestaña se completará con las partidas enviadas por el nuevo cliente.')}`;}
function elementsView(d){const t=d.totals;const groups=state.bots?['humanElementSource','botElementSource']:['humanElementSource'];const sources=['Directo','Área','Aura','Quemadura'];const values=sources.map((name,s)=>({name,value:d.reports.reduce((sum,r)=>sum+groups.reduce((n,g)=>n+ELEMENTS.reduce((a,_,e)=>a+metric(r,g,s*9+e,'damage')+metric(r,g,s*9+e,'shieldDamage'),0),0),0)}));const sourceSum=values.reduce((s,v)=>s+v.value,0);const unknown=Math.max(0,t.damage-sourceSum);const max=Math.max(1,...values.map(v=>v.value),unknown);return `<div class="kpis">${kpi('Daño efectivo',compact(t.damage),'Daño de vida + escudo','highlight')}${kpi('Curación total',compact(t.healing),'Todos los actores del registro')}${kpi('Daño absorbido',compact(t.absorbed),'Todos los actores del registro')}${kpi('Elementos ofensivos',fmt(t.elements.filter(e=>![1,2].includes(e.key)&&e.health+e.shield>0).length),'Vida y escudo se muestran como apoyo')}</div><div class="grid-half">${panel('Comparativa elemental',''+(state.bots?'Humanos y bots':'Solo humanos')+' · daño efectivo',elementBars(t))}${panel('Origen del daño','Cada impacto cuenta una sola vez',`<div class="panel-body">${[...values,{name:'Sin desglose',value:unknown}].map((v,i)=>`<div class="element-row" style="grid-template-columns:95px 1fr 65px"><span>${v.name}</span><div class="track"><div class="fill" style="width:${v.value/max*100}%;background:${['#d3b57b','#8fa5d5','#ad93cd','#ec9c7a','#63778f'][i]}"></div></div><span class="element-value">${compact(v.value)}</span></div>`).join('')}<p class="help">Los informes sin separación por origen permanecen en «Sin desglose». El exceso de daño no se suma al daño efectivo.</p></div>`)}</div>${panel('Formas de ataque','Ordenadas por daño de jugadores',`<div class="table-scroll"><table class="table"><thead><tr><th>Ataque</th><th class="number">Daño efectivo</th><th class="number">Lanzamientos</th><th class="number">Daño / lanzamiento</th></tr></thead><tbody>${t.attacks.map(a=>`<tr><td><strong>${a.name}</strong></td><td class="number">${compact(a.damage)}</td><td class="number">${fmt(a.casts)}</td><td class="number">${a.casts?fmt(a.damage/a.casts,1):'—'}</td></tr>`).join('')||'<tr><td colspan="4">Sin desglose de ataques.</td></tr>'}</tbody></table></div><div class="table-note">Un lanzamiento con varios elementos sigue siendo un único ataque. Las quemaduras y auras pueden causar daño sin un lanzamiento propio.</div>`)}`;}
function connectionView(d){const streams=d.streams.filter(r=>state.status==='all'||r.status===state.status);const p=pingSummary(streams),t=d.totals;const rows=streams.filter(r=>r.network&&r.ping?.samples).sort((a,b)=>b.updatedAt-a.updatedAt);return `<div class="kpis">${kpi('Ping medio',fmt(p.mean,1),'Ponderado por respuestas recibidas','highlight',p.samples?'ms':'')}${kpi('Ping mínimo',fmt(p.min,1),'Menor muestra registrada','',p.samples?'ms':'')}${kpi('Ping máximo',fmt(p.max,1),'Mayor muestra registrada','',p.samples?'ms':'')}${kpi('Respuestas recibidas',fmt(p.samples),fmt(p.reporters)+' conexiones de participantes')}</div><div class="grid-half">${panel('Rendimiento del juego','Media ponderada de los informes históricos',`<div class="panel-body"><div class="key-values"><span>FPS comunicados</span><b>${fmt(t.fps,1)}${t.fps!==null?' FPS':''}</b><span>Tiempo de simulación</span><b>${fmt(t.simulation,2)}${t.simulation!==null?' ms':''}</b><span>Partidas analizadas</span><b>${fmt(t.matches)}</b></div><p class="help">Los FPS proceden del juego, no de esta página. El tiempo de simulación no mide la latencia de la red.</p></div>`) }${panel('Lectura de la conexión','Ida y vuelta por el WebSocket de la partida',`<div class="panel-body"><div class="key-values"><span>Conexiones con muestras</span><b>${fmt(p.reporters)}</b><span>Sesiones locales</span><b>${fmt(streams.filter(r=>!r.network).length)}</b><span>Actualización del juego</span><b>Aprox. 15 segundos</b></div><p class="help">La media es la suma de milisegundos dividida entre el total de respuestas. Las partidas locales y las conexiones sin respuesta no cuentan como 0 ms.</p></div>`)}</div>${rows.length?panel('Conexiones registradas','Una muestra acumulada por cliente y partida',`<div class="table-scroll"><table class="table"><thead><tr><th>Participante</th><th>Partida</th><th class="number">Ping medio</th><th class="number">Mínimo</th><th class="number">Máximo</th><th class="number">Respuestas</th><th>Última señal</th></tr></thead><tbody>${rows.slice(0,rowLimit()).map(r=>`<tr><td><strong>${esc(r.players.find(p=>p.id===r.reporterId)?.name||'Participante')}</strong></td><td>#${r.runId.slice(-6)}</td><td class="number">${fmt(r.ping.sumMs/r.ping.samples,1)} ms</td><td class="number">${fmt(r.ping.minMs,1)} ms</td><td class="number">${fmt(r.ping.maxMs,1)} ms</td><td class="number">${fmt(r.ping.samples)}</td><td>${stamp(r.updatedAt)}</td></tr>`).join('')}</tbody></table></div>${rows.length>rowLimit()?'<div class="table-note"><button class="button secondary" data-action="more">Mostrar más</button></div>':''}`):empty('Aún no hay muestras de ping.','Las versiones anteriores no guardaban la latencia. Las nuevas conexiones en red aparecerán aquí cuando el cliente envíe telemetría.')}`;}
const renderers={overview,live:liveView,matches:matchesView,players:playersView,elements:elementsView,connection:connectionView};
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
 $('#coverage').textContent=hasHistory?`${state.start} — ${state.end} UTC · ${fmt(d.reports.length)} partidas · ${fmt(state.raw.length)} documentos${d.invalid+d.liveInvalid?' · '+fmt(d.invalid+d.liveInvalid)+' inválidos omitidos':''}${d.conflicts?' · '+d.conflicts+' cabeceras incompatibles':''}${d.totals.dropped?' · Desglose incompleto en origen':''}`:'Historial sin datos cargados';
 if(state.source==='public'&&state.publicCoverage){const c=state.publicCoverage;$('#coverage').textContent+=` · Disponible ${c.start} — ${c.end} UTC${c.partial?' · Cobertura parcial por límites o registros omitidos':''}${c.stale?' · Última copia disponible':''}`;}
 $('#updated').textContent=state.updated?'Última actualización '+stamp(state.updated):'Esperando conexión';
 $('#export').disabled=!hasHistory;
}
function setView(view){if(!(view in views))return;state.view=view;state.extraRows=0;state.search='';history.replaceState(null,'','#'+view);render();window.scrollTo({top:0,behavior:'instant'});}
function showSettings(){if(!$('#settings').open)$('#settings').showModal();}
function connect() {
  if (state.source === 'import') {
    Object.assign(state, {source:'public', raw:[], telemetry:[], historyReady:false,
      liveReady:false, publicCoverage:null, cache:false, updated:0});
  }
  state.loading = true;
  state.error = state.liveError = '';
  render();
  cloud.subscribe(state.range, {
    history(records, cache) {
      Object.assign(state, {raw:records, historyReady:true, cache, error:'', loading:false, updated:Date.now()});
      render();
    },
    live(records, available) {
      state.telemetry = records;
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
function playerDetails(report, players) {
  let content = '<p>Desglose individual no disponible para esta partida.</p>';
  if (players?.length === 0) content = '<p>Activa «Incluir bots» para ver a los participantes registrados.</p>';
  if (players?.length) content = `<div class="table-scroll"><table class="table" aria-label="Estadísticas por jugador">
    <thead><tr><th scope="col">Jugador</th><th scope="col" class="number">Daño</th><th scope="col" class="number">Bajas</th><th scope="col" class="number">Muertes</th></tr></thead>
    <tbody>${players.map(p => `<tr>
      <td><strong>${esc(p.name || 'Participante')}</strong><small style="display:block">${CLASSES[p.wizardType]} · Equipo ${p.team+1}${p.bot ? ' · Bot' : ''}</small></td>
      <td class="number"><strong>${fmt(p.damage,1)}</strong><small style="display:block">Vida ${fmt(p.healthDamage,1)} · Escudo ${fmt(p.shieldDamage,1)}</small></td>
      <td class="number">${fmt(p.kills)}</td><td class="number">${fmt(p.deaths)}</td>
    </tr>`).join('')}</tbody>
  </table></div>`;
  const partial = report.participantsPartial ? '<p class="help">El desglose de participantes es parcial.</p>' : '';
  return `<section class="detail-players"><h3>Por jugador</h3>${partial}${content}</section>`;
}

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

function exportCsv(){const d=data();let rows;if(state.view==='players')rows=[['jugador','partida','fecha_utc','bot','dano_vida_escudo','bajas','muertes'],...playersRanking(d.sessions.filter(r=>r.status!=='playing'&&(state.status==='all'||r.status===state.status)),state.bots).map(p=>[p.name,p.runId,p.day,p.bot,p.damage,p.kills,p.deaths])];else if(state.view==='elements')rows=[['elemento','dano_vida','dano_escudo','curacion','absorcion'],...d.totals.elements.map(e=>[e.name,e.health,e.shield,e.healing,e.absorbed])];else if(state.view==='connection')rows=[['partida','participante','muestras','ping_medio_ms','ping_min_ms','ping_max_ms'],...d.streams.filter(r=>r.network&&r.ping?.samples&&(state.status==='all'||r.status===state.status)).map(r=>[r.runId,r.players.find(p=>p.id===r.reporterId)?.name||'Participante',r.ping.samples,r.ping.sumMs/r.ping.samples,r.ping.minMs,r.ping.maxMs])];else rows=[['partida','fecha_utc','modo','mapa','estado','duracion_s','humanos','bots','dano_vida_escudo'],...d.reports.map(r=>[r.runId,r.day,r.mode,r.map,r.status,r.seconds,r.humans,r.bots,aggregate([r],state.bots).damage])];const safe=v=>{let s=String(v??'');if(/^[=+\-@\t\r]/.test(s))s="'"+s;return '"'+s.replace(/"/g,'""')+'"';};const blob=new Blob(['\uFEFF'+rows.map(row=>row.map(safe).join(';')).join('\r\n')],{type:'text/csv;charset=utf-8'});const url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download=`arcana-${state.view}-${state.start}.csv`;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}
$('#mode').value=state.mode;$('#record-status').value=state.status;$('#include-bots').checked=state.bots;$('#start-date').value=state.range.start;$('#end-date').value=state.range.end;
for(const b of document.querySelectorAll('[data-days]')){b.classList.toggle('selected',String(state.days)===b.dataset.days);b.setAttribute('aria-pressed',String(String(state.days)===b.dataset.days));b.addEventListener('click',()=>{state.days=b.dataset.days==='custom'?'custom':Number(b.dataset.days);document.querySelectorAll('[data-days]').forEach(el=>{el.classList.toggle('selected',el===b);el.setAttribute('aria-pressed',String(el===b));});$('#date-inputs').hidden=state.days!=='custom';changeRange();});}
$('#mode').onchange=e=>{state.mode=e.target.value;state.extraRows=0;render();};$('#record-status').onchange=e=>{state.status=e.target.value;state.extraRows=0;render();};$('#include-bots').onchange=e=>{state.bots=e.target.checked;render();};$('#start-date').onchange=changeRange;$('#end-date').onchange=changeRange;
$('#connection-button').onclick=showSettings;$('#refresh').onclick=()=>state.source==='import'?showSettings():connect();$('#export').onclick=exportCsv;
$('#fullscreen').onclick=async()=>{try{if(document.fullscreenElement)await document.exitFullscreen();else await document.documentElement.requestFullscreen();}catch{$('#notice').textContent='Usa F11 para abrir la pantalla completa en Windows.';}};
$('#server-data').onclick=()=>{$('#settings').close();connect();};
$('#import').onchange=async e=>{try{const f=e.target.files[0];if(!f)return;if(f.size>25*1024*1024)throw Error('El archivo supera 25 MB.');const v=JSON.parse(await f.text());const raw=Array.isArray(v)?v:Array.isArray(v.reports)?v.reports:v.payload||v.runId?[v]:null;if(!raw)throw Error('El archivo no contiene informes.');const parsed=deduplicate(raw);if(!parsed.reports.length)throw Error('No hay informes válidos en el archivo.');cloud.stop();state.raw=raw;state.telemetry=Array.isArray(v.telemetry)?v.telemetry:[];state.source='import';state.error='';state.liveError='';state.updated=Date.now();state.loading=false;$('#settings').close();render();}catch(e){$('#settings-error').textContent=e.message;}finally{e.target.value='';}};
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
