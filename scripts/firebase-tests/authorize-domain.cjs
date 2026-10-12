const fs = require('node:fs');
const path = require('node:path');
const root = path.dirname(require.resolve('firebase-tools/package.json'));
const { requireAuth } = require(path.join(root, 'lib/requireAuth.js'));
const { getAuthDomains, updateAuthDomains } = require(path.join(root, 'lib/gcp/auth.js'));
(async () => {
  const [configPath, hostname] = process.argv.slice(2);
  if (!configPath || !/^[a-z0-9-]+\.trycloudflare\.com$/.test(hostname || '')) throw new Error('Expected a config file and a Quick Tunnel hostname.');
  const { projectId } = JSON.parse(fs.readFileSync(configPath, 'utf8').replace(/^\uFEFF/, ''));
  if (!/^[a-z][a-z0-9-]{5,29}$/.test(projectId || '')) throw new Error('Invalid Firebase projectId.');
  await requireAuth({ project: projectId });
  const before = await getAuthDomains(projectId);
  if (!before.includes(hostname)) await updateAuthDomains(projectId, [...before, hostname]);
  if (!(await getAuthDomains(projectId)).includes(hostname)) throw new Error('Domain authorization was not confirmed.');
  console.log('Firebase: HTTPS domain authorized; existing domains preserved.');
  process.exit(0);
})().catch(error => { console.error(error.message); process.exit(1); });
