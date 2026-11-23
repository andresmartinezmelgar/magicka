import {STATUSES} from './schema.mjs';

export function isNewer(next,prev) {
  const rank = s => s === 'completed' ? 6 : STATUSES.indexOf(s);
  return ['runId','mode','map','gameVersion','day'].every(k => next[k] === prev[k]) &&
    !(prev.status === 'completed' && next.status !== 'completed') &&
    (next.revision > prev.revision || next.revision === prev.revision &&
      (rank(next.status) > rank(prev.status) || next.status === prev.status &&
        next.participants?.length > 0 && !prev.participants?.length));
}
