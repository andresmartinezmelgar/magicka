export const ELEMENTS = ['Agua','Vida','Escudo','Frío','Rayo','Arcano','Piedra','Fuego','Físico'];
export const COLORS = ['#55c1ff','#77d8a1','#c8b7ff','#abe9f6','#f1cf70','#b68bff','#d6ac7b','#fa886d','#95a6bd'];
export const MODES = {duel:'Duelo',arena:'Arena',warfare:'Guerra',defense:'Supervivencia'};
export const MAPS = {classic:'Clásico',citadel:'Ciudadela',ascension:'Ascensión',relics:'Reliquias',frontline:'Frente',practice:'Prácticas',bastion:'Bastión'};
export const STATUSES = ['playing','completed','left','disconnected','suspended','interrupted'];
export const STATUS_NAMES = {playing:'En curso',completed:'Finalizada',left:'Abandonada',disconnected:'Desconectada',suspended:'Suspendida',interrupted:'Interrumpida'};
export const ATTACKS = ['Haz','Rayo','Chorro','Roca','Fragmentos','Fuerza','Barrera','Cuerpo a cuerpo','Sobre sí mismo','Nova','Mina','Meteorito','Prisa','Teletransporte','Reanimación','Aura de fuego','Aura de rayo','Aura regeneradora','Roca automática','Nova de frío','Cadena de rayos','Quemadura','Golpe enemigo','Área enemiga','Bombardeo del oráculo','Regeneración','Escudo de anillo','Pulso de anillo','Robo de escudo','Reflejo de escudo','Aura arcana','Aura de agua','Marca arcana','Eco arcano'];
export const CLASSES = ['Equilibrado','Piromante','Criomante','Invocador de tormentas','Geomante'];
const GROUPS=['total','actor','element','attack','recipe','enemy','enemyElement','difficulty','equipment','relic','performance','humanElement','botElement','enemyElementDealt','humanAttack','botAttack','enemyAttack','environmentElement','environmentAttack','humanElementSource','botElementSource','enemyElementSource','environmentElementSource','actorShieldSource'];
const MEASURES=['damage','shieldDamage','damageReceived','shieldAbsorbed','overkill','immuneHits','hits','kills','deaths','spawns','casts','healing','healingReceived','shieldGranted','shieldRepaired','aliveSeconds','wetSeconds','burningSeconds','frozenSeconds','friendlyDamage','selfDamage','revives','revived','collected','goldEarned','goldSpent','levels','spawnHealth','samples','valueSum','distance','captures'];
const number = v => typeof v === 'number' && Number.isFinite(v) && v >= 0 && v <= 1e18;
const run = v => typeof v==='string' && /^[0-9a-f]{32}$/.test(v);
const MAX_PARTICIPANTS = 64;
const PARTICIPANT_FIELDS = ['slot','bot','wizardType','team','healthDamage','shieldDamage','kills','deaths'];
export const utcDay = (now=new Date()) => now.toISOString().slice(0,10);
export function dayRange(days, now=new Date()) {const end=utcDay(now); const d=new Date(end+'T00:00:00Z');d.setUTCDate(d.getUTCDate()-days+1);return {start:utcDay(d),end};}
function validDay(v){return typeof v==='string' && /^(20[2-9][0-9]|2100)-\d\d-\d\d$/.test(v) && Number.isFinite(Date.parse(v)) && new Date(v+'T00:00:00Z').toISOString().slice(0,10)===v;}
function validKey(group,key){
 if(group==='total')return key===0;
 if(group==='actorShieldSource')return validKey('actor',Math.floor(key/2));
 if(group==='actor')return key>=64?key<=78:key<64&&key%16<5;
 if(['element','humanElement','botElement','enemyElementDealt','environmentElement'].includes(group))return key<=8;
 if(group.endsWith('ElementSource'))return key<36;
 if(group==='attack'||group.endsWith('Attack'))return key<34;
 if(group==='recipe'){if(key>728)return false;let last=9;while(key>0){const digit=key%9;if(digit===0||digit>last)return false;last=digit;key=Math.floor(key/9);}return true;}
 if(group==='enemy')return key<15;if(group==='enemyElement')return key<135;
 if(group==='difficulty')return key<=1000000;if(group==='equipment')return key<=10;
 if(group==='relic')return key<7;if(group==='performance')return key<=4;return false;
}
export function parseReport(raw) {
  let r=typeof raw==='string'?JSON.parse(raw):raw;
  if (r?.payload !== undefined) r=typeof r.payload==='string'?JSON.parse(r.payload):r.payload;
  if (!r || r.schemaVersion!==1 || !run(r.runId) || !Number.isSafeInteger(r.revision) || r.revision<1 || r.revision>1892160000 || !validDay(r.day) || !Object.hasOwn(MODES,r.mode) || !Object.hasOwn(MAPS,r.map) || !STATUSES.includes(r.status) || r.status==='playing' || !/^\d{1,3}\.\d{1,3}\.\d{1,3}$/.test(r.gameVersion) || !number(r.seconds) || Math.abs(r.seconds-r.revision/60)>.001 || !Number.isInteger(r.humans) || r.humans<0 || r.humans>16 || !Number.isInteger(r.bots) || r.bots<0 || r.bots>16 || !Array.isArray(r.metrics) || r.metrics.length>2048) throw Error('Informe inválido');
  const allowed=['schemaVersion','runId','revision','gameVersion','day','mode','map','network','status','seconds','humans','bots','difficulty','winnerTeam','scores','metrics','droppedBuckets','continuation','participants','participantsPartial'];
  if(Object.keys(r).some(k=>!allowed.includes(k))||typeof r.network!=='boolean'||!Number.isInteger(r.difficulty)||r.difficulty<0||r.difficulty>1000000||!Number.isInteger(r.winnerTeam)||r.winnerTeam< -1||r.winnerTeam>1||!Array.isArray(r.scores)||r.scores.length!==2||!r.scores.every(s=>Number.isInteger(s)&&s>=0&&s<=1e9)||r.droppedBuckets!==undefined&&(!Number.isInteger(r.droppedBuckets)||r.droppedBuckets<0))throw Error('Cabecera inválida');
  const seen=new Set();
  for(const m of r.metrics){const k=m.group+':'+m.key+':'+m.measure;if(!GROUPS.includes(m.group)||!MEASURES.includes(m.measure)||!Number.isSafeInteger(m.key)||m.key<0||m.key>1e9||!validKey(m.group,m.key)||!number(m.value)||seen.has(k))throw Error('Métrica inválida');seen.add(k);}
  if (r.participantsPartial !== undefined && typeof r.participantsPartial !== 'boolean' ||
      r.participantsPartial && r.participants == null) throw Error('Desglose inválido');
  if (r.participants != null) {
    if (!Array.isArray(r.participants) || r.participants.length > MAX_PARTICIPANTS) throw Error('Desglose inválido');
    const slots = new Set();
    for (const p of r.participants) {
      if (!p || !Number.isInteger(p.slot) || p.slot < 1 || p.slot > MAX_PARTICIPANTS || slots.has(p.slot) ||
          typeof p.bot !== 'boolean' || !Number.isInteger(p.wizardType) || p.wizardType < 0 || p.wizardType >= CLASSES.length ||
          (p.team !== 0 && p.team !== 1) || !number(p.healthDamage) || !number(p.shieldDamage) ||
          ![p.kills,p.deaths].every(n => Number.isInteger(n) && n >= 0 && n <= 2147483647) ||
          Object.keys(p).some(key => !PARTICIPANT_FIELDS.includes(key))) throw Error('Participante inválido');
      slots.add(p.slot);
    }
  }
  return r;
}
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
 return {matches:reports.length,seconds,humans,bots,kills,damage,healing,absorbed,general,elements,attacks:attacks.filter(a=>a.damage||a.casts).sort((a,b)=>b.damage-a.damage),timeline:[...timeline.values()].sort((a,b)=>a.day.localeCompare(b.day)),classes:[...classes.values()].sort((a,b)=>b.damage-a.damage),fps:fpsSamples?fpsSum/fpsSamples:null,simulation:simSamples?simSum/simSamples:null,dropped};
}
export function parseTelemetry(raw){
 const envelope=raw;let r=typeof raw.payload==='string'?JSON.parse(raw.payload):raw;
 if(r.schemaVersion!==1||!run(r.runId)||!validDay(r.day)||!STATUSES.includes(r.status)||!Object.hasOwn(MODES,r.mode)||!Object.hasOwn(MAPS,r.map)||!number(r.seconds)||!Number.isSafeInteger(r.revision)||r.revision<1||r.revision>1892160000||!Array.isArray(r.players)||r.players.length>32||!Number.isInteger(r.reporterId))throw Error('Sesión inválida');
 if(!/^\d{1,3}\.\d{1,3}\.\d{1,3}$/.test(r.gameVersion)||typeof r.network!=='boolean'||Math.abs(r.seconds-r.revision/60)>.001)throw Error('Cabecera de sesión inválida');
 const ids=new Set();for(const p of r.players){if(!Number.isInteger(p.id)||ids.has(p.id)||typeof p.name!=='string'||p.name.length>64||typeof p.bot!=='boolean'||typeof p.alive!=='boolean'||!Number.isInteger(p.team)||!Number.isInteger(p.wizardType)||p.wizardType<0||p.wizardType>4||p.team<0||p.team>1||!Number.isInteger(p.kills)||!number(p.kills)||!Number.isInteger(p.deaths)||!number(p.deaths)||!Array.isArray(p.health)||p.health.length!==9||!Array.isArray(p.shield)||p.shield.length!==9||!p.health.every(number)||!p.shield.every(number))throw Error('Jugador inválido');ids.add(p.id);}
 if(r.ping && (!Number.isSafeInteger(r.ping.samples)||r.ping.samples<0||!number(r.ping.sumMs)||!number(r.ping.minMs)||!number(r.ping.maxMs)||r.ping.maxMs<r.ping.minMs||r.ping.samples>0&&(r.ping.sumMs/r.ping.samples<r.ping.minMs-.001||r.ping.sumMs/r.ping.samples>r.ping.maxMs+.001)))throw Error('Ping inválido');
 const updated=envelope.updatedAt?.toMillis?.()??(typeof envelope.updatedAt==='number'?envelope.updatedAt:Date.parse(envelope.updatedAt||r.updatedAt||''));
 return {...r,updatedAt:Number.isFinite(updated)?updated:0,streamId:envelope.id||r.runId+':'+r.reporterId};
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
