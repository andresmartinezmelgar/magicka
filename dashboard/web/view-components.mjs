import {STATUS_NAMES} from './schema.mjs';

const paths={overview:'M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z',live:'M3 12h4l3-8 4 16 3-8h4',matches:'M5 4h14v17H5z M9 2v4 M15 2v4 M8 10h8 M8 14h5',players:'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8 M17 4a4 4 0 0 1 0 7 M22 21v-2a4 4 0 0 0-3-3.9',elements:'m13 2-9 12h7l-1 8 10-13h-7z',connection:'M2 8a16 16 0 0 1 20 0 M5 12a11 11 0 0 1 14 0 M8 16a6 6 0 0 1 8 0 M12 20h.01'};

export const icon=(name)=>`<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="${paths[name]||paths.elements}"/></svg>`;

export function panel(title,subtitle,body,action=''){return `<section class="panel"><div class="panel-head"><div><h2>${title}</h2>${subtitle?`<p>${subtitle}</p>`:''}</div>${action}</div>${body}</section>`;}

export const kpi=(label,value,foot,type='',unit='')=>value===null||value===undefined||value==='—'?'':`<div class="kpi ${type}"><div class="kpi-label">${label}${icon(label.includes('Ping')?'connection':label.includes('Daño')?'elements':label.includes('Particip')?'players':'matches')}</div><div class="kpi-value">${value}${unit?` <em>${unit}</em>`:''}</div><div class="kpi-foot">${foot}</div></div>`;

export const tag=r=>`<span class="tag ${r.status==='completed'?'':r.active?'live':'stale'}">${r.active?'En directo':r.status==='playing'?'Sin señal reciente':STATUS_NAMES[r.status]}</span>`;

export function empty(title,text,buttons=false){return `<section class="panel empty"><div class="empty-mark">${icon('live')}</div><div class="eyebrow">OBSERVATORIO DE ARCANA ARENA</div><h2>${title}</h2><p>${text}</p>${buttons?'<div class="empty-actions"><button class="button primary" data-action="connect">Reintentar</button></div>':''}</section>`;}
