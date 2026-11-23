import {parseTelemetry} from './report-validation.mjs';
import {isNewer} from './record-order.mjs';

// Parsing and revision selection depend on the dataset; liveness depends on the clock.
export function indexLiveStreams(raw) {
 const streams=[];let invalid=0;for(const v of raw){try{streams.push(parseTelemetry(v));}catch{invalid++;}}
 const map=new Map();for(const r of streams){const p=map.get(r.runId);if(p&&['mode','map','gameVersion','day'].some(k=>r[k]!==p[k])){invalid++;continue;}if(!p||isNewer(r,p)||r.revision===p.revision&&r.status===p.status&&r.updatedAt>p.updatedAt)map.set(r.runId,r);}
 return {runs:[...map.values()],streams,invalid};
}

export function projectLiveRuns(indexed, now=Date.now()) {
  return {...indexed,runs:indexed.runs.map(r=>({...r,active:r.status==='playing'&&now-r.updatedAt<90000&&now>=r.updatedAt-30000})).sort((a,b)=>b.updatedAt-a.updatedAt)};
}

export function liveRuns(raw, now=Date.now()) {
  return projectLiveRuns(indexLiveStreams(raw),now);
}

export function pingSummary(streams){const latest=new Map();for(const r of streams){if(!r.network||!r.ping?.samples)continue;const key=r.runId+':'+r.reporterId;const p=latest.get(key);if(!p||r.ping.samples>p.ping.samples||r.ping.samples===p.ping.samples&&r.updatedAt>p.updatedAt)latest.set(key,r);}
 let samples=0,sum=0,min=Infinity,max=0;for(const r of latest.values()){samples+=r.ping.samples;sum+=r.ping.sumMs;min=Math.min(min,r.ping.minMs);max=Math.max(max,r.ping.maxMs);}return {samples,mean:samples?sum/samples:null,min:samples?min:null,max:samples?max:null,reporters:latest.size};}

export function playersRanking(runs, includeBots = true) {
  const rows = [];
  for (const r of runs) {
    for (const p of r.players) {
      if (p.bot && !includeBots) continue;
      const healthDamage = p.health.reduce((sum, value) => sum + value, 0);
      const shieldDamage = p.shield.reduce((sum, value) => sum + value, 0);
      rows.push({...p, runId:r.runId, day:r.day, mode:r.mode, seconds:r.seconds,
        damage:healthDamage + shieldDamage, healthDamage, shieldDamage});
    }
  }
  return rows.sort((a,b) => b.damage - a.damage);
}

export function matchPlayers(report, streams, includeBots = true) {
  if (report.participants?.length) {
    return report.participants.filter(p => includeBots || !p.bot)
      .map(p => ({...p, name:`${p.bot ? 'Bot' : 'Jugador'} ${p.slot}`, damage:p.healthDamage + p.shieldDamage}))
      .sort((a,b) => b.damage - a.damage);
  }
  const keys = ['runId', 'day', 'mode', 'map', 'gameVersion', 'network', 'status', 'revision'];
  let match;
  for (const stream of streams) {
    if (!stream.players.length || !keys.every(key => stream[key] === report[key])) continue;
    if (!match || stream.updatedAt > match.updatedAt) match = stream;
  }
  return match ? playersRanking([match], includeBots) : null;
}
