import {MODES,MAPS,STATUSES,CLASSES,GROUPS,MEASURES,SELECTION_FIELDS} from './schema.mjs';

const number = v => typeof v === 'number' && Number.isFinite(v) && v >= 0 && v <= 1e18;
const run = v => typeof v==='string' && /^[0-9a-f]{32}$/.test(v);
const MAX_PARTICIPANTS = 64;
const PARTICIPANT_FIELDS = ['slot','bot','wizardType','team','healthDamage','shieldDamage','kills','deaths','elementSelection'];
function validSelection(usage, maximum) {
  return usage && Object.keys(usage).every(key => SELECTION_FIELDS.includes(key)) &&
    SELECTION_FIELDS.every(key => number(usage[key])) &&
    SELECTION_FIELDS.reduce((sum, key) => sum + usage[key], 0) <= maximum + .05;
}
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
          Object.keys(p).some(key => !PARTICIPANT_FIELDS.includes(key)) ||
          p.elementSelection != null && (p.bot || !validSelection(p.elementSelection,r.seconds))) throw Error('Participante inválido');
      slots.add(p.slot);
    }
  }
  return r;
}

export function parseTelemetry(raw){
 const envelope=raw;let r=typeof raw.payload==='string'?JSON.parse(raw.payload):raw;
 if(r.schemaVersion!==1||!run(r.runId)||!validDay(r.day)||!STATUSES.includes(r.status)||!Object.hasOwn(MODES,r.mode)||!Object.hasOwn(MAPS,r.map)||!number(r.seconds)||!Number.isSafeInteger(r.revision)||r.revision<1||r.revision>1892160000||!Array.isArray(r.players)||r.players.length>32||!Number.isInteger(r.reporterId))throw Error('Sesión inválida');
 if(!/^\d{1,3}\.\d{1,3}\.\d{1,3}$/.test(r.gameVersion)||typeof r.network!=='boolean'||Math.abs(r.seconds-r.revision/60)>.001)throw Error('Cabecera de sesión inválida');
 if(r.elementSelection != null && !validSelection(r.elementSelection,r.seconds*16))throw Error('Tiempo de selección inválido');
 const ids=new Set();for(const p of r.players){if(!Number.isInteger(p.id)||ids.has(p.id)||typeof p.name!=='string'||p.name.length>64||typeof p.bot!=='boolean'||typeof p.alive!=='boolean'||!Number.isInteger(p.team)||!Number.isInteger(p.wizardType)||p.wizardType<0||p.wizardType>4||p.team<0||p.team>1||!Number.isInteger(p.kills)||!number(p.kills)||!Number.isInteger(p.deaths)||!number(p.deaths)||!Array.isArray(p.health)||p.health.length!==9||!Array.isArray(p.shield)||p.shield.length!==9||!p.health.every(number)||!p.shield.every(number))throw Error('Jugador inválido');ids.add(p.id);}
 if(r.ping && (!Number.isSafeInteger(r.ping.samples)||r.ping.samples<0||!number(r.ping.sumMs)||!number(r.ping.minMs)||!number(r.ping.maxMs)||r.ping.maxMs<r.ping.minMs||r.ping.samples>0&&(r.ping.sumMs/r.ping.samples<r.ping.minMs-.001||r.ping.sumMs/r.ping.samples>r.ping.maxMs+.001)))throw Error('Ping inválido');
 const updated=envelope.updatedAt?.toMillis?.()??(typeof envelope.updatedAt==='number'?envelope.updatedAt:Date.parse(envelope.updatedAt||r.updatedAt||''));
 return {...r,updatedAt:Number.isFinite(updated)?updated:0,streamId:envelope.id||r.runId+':'+r.reporterId};
}
