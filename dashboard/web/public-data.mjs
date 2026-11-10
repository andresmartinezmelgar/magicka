const PUBLIC_SERVER = 'https://familiamelgar.duckdns.org';
export function defaultApiRoot(location = globalThis.location) {
  const origin = location && !['localhost', '127.0.0.1', '[::1]'].includes(location.hostname) && location.protocol !== 'file:'
    ? location.origin : PUBLIC_SERVER;
  return origin + '/api/dashboard';
}

// No Firebase SDK, accounts, tokens or browser persistence in this read-only client.
export function createPublicSource({fetchImpl = globalThis.fetch, documentImpl = globalThis.document,
  setTimer = globalThis.setTimeout, clearTimer = globalThis.clearTimeout, apiRoot = defaultApiRoot()} = {}) {
  let generation = 0, timer, controller, visibility;
  function stop() {
    generation++; clearTimer(timer); controller?.abort(); controller = null;
    if (visibility) documentImpl?.removeEventListener('visibilitychange', visibility);
    visibility = null;
  }
  function subscribe(_range, callbacks) {
    stop(); const current = generation, pages = {}, etags = {};
    const active = () => current === generation;
    async function read(part, signal) {
      const response = await fetchImpl(apiRoot + '/' + part, {signal, credentials:'omit', cache:'no-cache',
        headers: etags[part] ? {'If-None-Match':etags[part]} : {}});
      if (response.status === 304 && pages[part]) return pages[part];
      if (!response.ok) throw Error(response.status === 503 ? 'El servidor aún no dispone de estadísticas públicas. Se reintentará automáticamente.' : 'No se han podido leer las estadísticas del servidor.');
      const body = await response.text();
      if (body.length > 5 * 1024 * 1024) throw Error('La respuesta de estadísticas supera el límite permitido.');
      let page;
      try { page = JSON.parse(body); }
      catch { throw Error('El servidor aún no publica estadísticas compatibles. Se reintentará automáticamente.'); }
      if (page.schemaVersion !== 1 || !Array.isArray(page.reports) || !Array.isArray(page.telemetry) ||
          page.reports.length > 500 || page.telemetry.length > 250 ||
          !/^\d{4}-\d{2}-\d{2}$/.test(page.start) || !/^\d{4}-\d{2}-\d{2}$/.test(page.end))
        throw Error('El servidor ha devuelto estadísticas incompatibles.');
      if (!active()) return null;
      pages[part] = page; etags[part] = response.headers.get('ETag'); return page;
    }
    async function poll() {
      if (!active() || documentImpl?.hidden) return;
      clearTimer(timer); controller = new AbortController();
      const request = controller, timeout = setTimer(() => request.abort(), 12000);
      try {
        const results = await Promise.allSettled(['history','live'].map(part => read(part, request.signal)));
        if (!active() || documentImpl?.hidden) return;
        const history = pages.history, live = pages.live;
        if (history && results[0].status === 'fulfilled') callbacks.history(history.reports, !!history.stale);
        if (history || live) {
          const telemetry = [...(history?.telemetry || []), ...(live?.telemetry || [])]
            .filter(entry => entry?.report && Number.isFinite(entry.updatedAt))
            .map(entry => ({...entry.report, updatedAt:entry.updatedAt}));
          if (results.some(result => result.status === 'fulfilled'))
            callbacks.live(telemetry, results[1].status === 'fulfilled');
          callbacks.coverage?.({start:history?.start || live.start, end:history?.end || live.end,
            partial:!!(history?.partial || live?.partial), stale:!!(history?.stale || live?.stale)});
        }
        // A good history response must not erase an error in the live channel.
        results.forEach((result, index) => {
          if (result.status === 'rejected') callbacks.error(result.reason, index === 1);
        });
      } finally {
        clearTimer(timeout);
        if (controller === request) controller = null;
        if (active() && !documentImpl?.hidden) timer = setTimer(poll, 15000);
      }
    }
    visibility = () => {
      clearTimer(timer);
      if (documentImpl.hidden) controller?.abort();
      else if (!controller) poll();
    };
    documentImpl?.addEventListener('visibilitychange', visibility);
    poll();
  }
  return {subscribe, stop};
}
const source = createPublicSource();
export const subscribe = source.subscribe, stop = source.stop;
export function friendlyError(error) {
  return error?.name === 'AbortError' ? 'La consulta ha tardado demasiado. Se reintentará automáticamente.'
    : error instanceof TypeError ? 'No hay conexión con las estadísticas públicas. Se reintentará automáticamente.'
    : error?.message || 'No se han podido leer las estadísticas.';
}
