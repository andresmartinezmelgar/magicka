import {ELEMENTS,COLORS,STATUSES,ATTACKS,CLASSES,SELECTION_FIELDS} from './schema.mjs';
import {parseReport,parseTelemetry} from './report-validation.mjs';
export {ELEMENTS,COLORS,MODES,MAPS,STATUSES,STATUS_NAMES,ATTACKS,CLASSES} from './schema.mjs';
export {parseReport,parseTelemetry} from './report-validation.mjs';

export function selectionUsage(record) {
  const usage = record.elementSelection ?? (record.metrics ? {
    manualSeconds:metric(record,'total',0,'manualSelectionSeconds'),
    automaticSeconds:metric(record,'total',0,'automaticSelectionSeconds'),
    unreportedSeconds:metric(record,'total',0,'unreportedSelectionSeconds')
  } : {});
  const manualSeconds = usage.manualSeconds || 0, automaticSeconds = usage.automaticSeconds || 0;
  const unreportedSeconds = usage.unreportedSeconds || 0, knownSeconds = manualSeconds + automaticSeconds;
  return {manualSeconds, automaticSeconds, unreportedSeconds, available:knownSeconds + unreportedSeconds > 0,
    manualPercent:knownSeconds ? 100 * manualSeconds / knownSeconds : null,
    automaticPercent:knownSeconds ? 100 * automaticSeconds / knownSeconds : null};
}
export function selectionSummary(reports) {
  const usage = {manualSeconds:0, automaticSeconds:0, unreportedSeconds:0};
  let recorded = 0;
  for (const report of reports) {
    const current = selectionUsage(report);
    if (current.available) recorded++;
    for (const key of SELECTION_FIELDS) usage[key] += current[key];
  }
  return {...selectionUsage({elementSelection:usage}), recorded, missing:reports.length-recorded};
}
export const utcDay = (now=new Date()) => now.toISOString().slice(0,10);
export function dayRange(days, now=new Date()) {const end=utcDay(now); const d=new Date(end+'T00:00:00Z');d.setUTCDate(d.getUTCDate()-days+1);return {start:utcDay(d),end};}
export function isNewer(next,prev) {
  const rank = s => s === 'completed' ? 6 : STATUSES.indexOf(s);
  return ['runId','mode','map','gameVersion','day'].every(k => next[k] === prev[k]) &&
    !(prev.status === 'completed' && next.status !== 'completed') &&
    (next.revision > prev.revision || next.revision === prev.revision &&
      (rank(next.status) > rank(prev.status) || next.status === prev.status &&
        next.participants?.length > 0 && !prev.participants?.length));
}
export function deduplicate(raw){const map=new Map();let invalid=0,duplicates=0,conflicts=0;for(const v of raw){try{const r=parseReport(v),p=map.get(r.runId);if(!p)map.set(r.runId,r);else{duplicates++;if(isNewer(r,p))map.set(r.runId,r);else if(['mode','map','day','gameVersion'].some(k=>p[k]!==r[k]))conflicts++;}}catch{invalid++;}}return {reports:[...map.values()],invalid,duplicates,conflicts};}
const metricIndexes=new WeakMap();
export function metric(r,group,key,measure){let index=metricIndexes.get(r);if(!index){index=new Map(r.metrics.map(m=>[m.group+':'+m.key+':'+m.measure,m.value]));metricIndexes.set(r,index);}return index.get(group+':'+key+':'+measure)??0;}
export function filterReports(reports,f){return reports.filter(r=>r.day>=f.start&&r.day<=f.end&&(!f.mode||r.mode===f.mode)&&(!f.version||r.gameVersion===f.version)&&(f.status==='all'||r.status===f.status));}
export function aggregate(reports,includeBots=true){
 const elements=ELEMENTS.map((name,key)=>({name,key,color:COLORS[key],health:0,shield:0,healing:0,absorbed:0,casts:0}));
 const attacks=ATTACKS.map((name,key)=>({name,key,damage:0,casts:0}));const timeline=new Map();const classes=new Map();const general={};
 let seconds=0,humans=0,bots=0,kills=0,damage=0,healing=0,absorbed=0,fpsSum=0,fpsSamples=0,simSum=0,simSamples=0,dropped=0;
 const prefixes=includeBots?['human','bot']:['human'];
 for(const r of reports){seconds+=r.seconds;humans+=r.humans;bots+=r.bots;kills+=metric(r,'total',0,'kills');healing+=metric(r,'total',0,'healing');absorbed+=metric(r,'total',0,'shieldDamage');for(const m of r.metrics)if(m.group==='total')general[m.measure]=(general[m.measure]||0)+m.value;dropped+=r.droppedBuckets||0;
  let rd=0;for(const e of elements)for(const prefix of prefixes){e.health+=metric(r,prefix+'Element',e.key,'damage');e.shield+=metric(r,prefix+'Element',e.key,'shieldDamage');e.casts+=metric(r,prefix+'Element',e.key,'casts');rd+=metric(r,prefix+'Element',e.key,'damage')+metric(r,prefix+'Element',e.key,'shieldDamage');}
  for(const m of r.metrics)if(m.group==='actor'&&m.key<64&&(includeBots||m.key%32<16)){if(m.measure==='healing')elements[1].healing+=m.value;if(m.measure==='shieldAbsorbed')elements[2].absorbed+=m.value;}
  for(const a of attacks)for(const prefix of prefixes){a.damage+=metric(r,prefix+'Attack',a.key,'damage')+metric(r,prefix+'Attack',a.key,'shieldDamage');a.casts+=metric(r,prefix+'Attack',a.key,'casts');}
  damage+=rd;const t=timeline.get(r.day)||{day:r.day,matches:0,damage:0,seconds:0};t.matches++;t.damage+=rd;t.seconds+=r.seconds;timeline.set(r.day,t);
  fpsSum+=metric(r,'performance',2,'valueSum');fpsSamples+=metric(r,'performance',2,'samples');simSum+=metric(r,'performance',1,'valueSum');simSamples+=metric(r,'performance',1,'samples');
  for(const m of r.metrics){if(m.group!=='actor'||m.key>=64||!['damage','shieldDamage'].includes(m.measure))continue;const bot=m.key%32>=16;if(bot&&!includeBots)continue;const idx=m.key%16;const key=idx+':'+bot;const c=classes.get(key)||{name:(CLASSES[idx]||'Clase '+idx)+(bot?' · bot':''),damage:0};c.damage+=m.value;classes.set(key,c);}
 }
 return {matches:reports.length,seconds,humans,bots,kills,damage,healing,absorbed,general,elements,selection:selectionSummary(reports),attacks:attacks.filter(a=>a.damage||a.casts).sort((a,b)=>b.damage-a.damage),timeline:[...timeline.values()].sort((a,b)=>a.day.localeCompare(b.day)),classes:[...classes.values()].sort((a,b)=>b.damage-a.damage),fps:fpsSamples?fpsSum/fpsSamples:null,simulation:simSamples?simSum/simSamples:null,dropped};
}
export function liveRuns(raw,now=Date.now()){
 const streams=[];let invalid=0;for(const v of raw){try{streams.push(parseTelemetry(v));}catch{invalid++;}}
 const map=new Map();for(const r of streams){const p=map.get(r.runId);if(p&&['mode','map','gameVersion','day'].some(k=>r[k]!==p[k])){invalid++;continue;}if(!p||isNewer(r,p)||r.revision===p.revision&&r.status===p.status&&r.updatedAt>p.updatedAt)map.set(r.runId,r);}
 return {runs:[...map.values()].map(r=>({...r,active:r.status==='playing'&&now-r.updatedAt<90000&&now>=r.updatedAt-30000})).sort((a,b)=>b.updatedAt-a.updatedAt),streams,invalid};
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
