import {aggregate,playersRanking} from './model.mjs';
import {selectionCsv} from './selection-view.mjs';
import {csvCell} from './format.mjs';

export function exportRows(view, d, filters) {
  if (view==='players') {
    const sessions=d.sessions.filter(r=>r.status!=='playing'&&(filters.status==='all'||r.status===filters.status));
    return [['jugador','partida','fecha_utc','bot','dano_vida_escudo','bajas','muertes'],
      ...playersRanking(sessions,filters.bots).map(p=>[p.name,p.runId,p.day,p.bot,p.damage,p.kills,p.deaths])];
  }
  if (view==='elements') {
    return [['elemento','dano_vida','dano_escudo','curacion','absorcion'],
      ...d.totals.elements.map(e=>[e.name,e.health,e.shield,e.healing,e.absorbed])];
  }
  if (view==='connection') {
    const streams=d.streams.filter(r=>r.network&&r.ping?.samples&&(filters.status==='all'||r.status===filters.status));
    return [['partida','participante','muestras','ping_medio_ms','ping_min_ms','ping_max_ms'],
      ...streams.map(r=>[r.runId,r.players.find(p=>p.id===r.reporterId)?.name||'Participante',
        r.ping.samples,r.ping.sumMs/r.ping.samples,r.ping.minMs,r.ping.maxMs])];
  }
  return [['partida','fecha_utc','modo','mapa','estado','duracion_s','humanos','bots','dano_vida_escudo',
    'seleccion_manual_s','seleccion_automatica_s','seleccion_manual_pct','seleccion_automatica_pct'],
    ...d.reports.map(r=>[r.runId,r.day,r.mode,r.map,r.status,r.seconds,r.humans,r.bots,
      aggregate([r],filters.bots).damage,...selectionCsv(r)])];
}

export const encodeCsv = rows => '\uFEFF' + rows.map(row => row.map(csvCell).join(';')).join('\r\n');
