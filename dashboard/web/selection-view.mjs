import {selectionUsage} from './model.mjs';

const fmt = (value, digits=0) => new Intl.NumberFormat('es-ES',{maximumFractionDigits:digits}).format(value);
export const hasSelectionData = usage => usage.manualSeconds + usage.automaticSeconds > 0;
export function selectionTime(seconds) {
  const total = Math.round(seconds);
  if (total < 60) return fmt(seconds,1) + ' s';
  return [Math.floor(total/3600) ? fmt(Math.floor(total/3600))+' h' : '',
    Math.floor(total%3600/60) ? fmt(Math.floor(total%3600/60))+' min' : '',
    total%60 ? fmt(total%60)+' s' : ''].filter(Boolean).join(' ');
}
export function selectionDetails(usage, compact=false) {
  if (!hasSelectionData(usage)) return '';
  const cell = (label, seconds, percent) => `<div><span>${label}</span><b>${selectionTime(seconds)}</b><small>${fmt(percent,1)} %</small></div>`;
  return `<div class="selection-usage ${compact ? 'compact' : ''}" aria-label="Tiempo de selección de elementos">
    ${cell('Manual',usage.manualSeconds,usage.manualPercent)}${cell('Automática',usage.automaticSeconds,usage.automaticPercent)}</div>`;
}
export function playerSelection(player) {
  if (player.bot) return '';
  const usage = selectionUsage(player);
  if (!hasSelectionData(usage)) return '';
  return `Manual ${selectionTime(usage.manualSeconds)} · ${fmt(usage.manualPercent,1)} %<small style="display:block">Automática ${selectionTime(usage.automaticSeconds)} · ${fmt(usage.automaticPercent,1)} %</small>`;
}
export function selectionCsv(report) {
  const usage = selectionUsage(report);
  return hasSelectionData(usage) ? [usage.manualSeconds,usage.automaticSeconds,usage.manualPercent,usage.automaticPercent] : ['','','',''];
}
