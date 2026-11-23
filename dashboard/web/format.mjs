const numberFormats = new Map();
const htmlEntities = {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'};

export function numberFormatter(digits=0) {
  if (digits !== 0 && digits !== 1 && digits !== 2)
    return new Intl.NumberFormat('es-ES',{maximumFractionDigits:digits});
  if (!numberFormats.has(digits))
    numberFormats.set(digits,new Intl.NumberFormat('es-ES',{maximumFractionDigits:digits}));
  return numberFormats.get(digits);
}

export const escapeHtml = value => String(value??'').replace(/[&<>"']/g,character=>htmlEntities[character]);
export const formatNumber = (value,digits=0) => value===null||value===undefined ? '—' : numberFormatter(digits).format(value);
export const compactNumber = value => value>=1e6 ? formatNumber(value/1e6,2)+' M' : value>=10000 ? formatNumber(value/1000,1)+' mil' : formatNumber(value);
export const formatDuration = value => value>=3600 ? formatNumber(value/3600,1)+' h' : formatNumber(value/60,1)+' min';
export const formatTime = value => value ? new Date(value).toLocaleTimeString('es-ES',{hour:'2-digit',minute:'2-digit',second:'2-digit'}) : '—';

export function csvCell(value) {
  let text = String(value??'');
  if (/^[=+\-@\t\r]/.test(text)) text = "'"+text;
  return '"'+text.replace(/"/g,'""')+'"';
}
