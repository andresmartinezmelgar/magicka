"""Cambio de destino nginx con dos contenedores y retirada solo tras vaciar partidas y diarios."""
import json
import os
from pathlib import Path
import re
import urllib.request


class RollingDeployment:
    def __init__(self, updater, command, get_json, write_json, log):
        self.updater = updater
        self.command, self.get_json, self.write_json, self.log = command, get_json, write_json, log
        self.file = updater.state / 'rolling.json'
        self.proxy = Path(updater.config['rolling_proxy'])
        key_file = Path(updater.config['deployment_key_file'])
        if key_file.stat().st_mode & 0o077:
            raise RuntimeError('La clave de despliegue debe tener permisos 0600.')
        self.key = key_file.read_text().strip()
        if not 32 <= len(self.key) <= 256:
            raise RuntimeError('La clave de despliegue no es válida.')

    def lifecycle(self, instance, draining=None):
        endpoint = f"http://127.0.0.1:{instance['port']}/internal/"
        payload = None if draining is None else json.dumps({'enabled': draining}).encode()
        request = urllib.request.Request(endpoint + ('lifecycle' if draining is None else 'drain'), data=payload,
            headers={'X-Arcana-Deployment-Key': self.key, 'Content-Type': 'application/json'})
        with urllib.request.urlopen(request, timeout=15) as response:
            raw = response.read(8193)
        if len(raw) > 8192:
            raise RuntimeError('Respuesta de ciclo de vida demasiado grande.')
        state = json.loads(raw)
        if state.get('version') != instance['version'] or type(state.get('draining')) is not bool or \
            type(state.get('connections')) is not int or type(state.get('reservations')) is not int or \
            type(state.get('pendingDeliveries')) is not bool or min(state['connections'], state['reservations']) < 0:
            raise RuntimeError('Estado de servidor incompatible; no se retirará ningún contenedor.')
        return state

    def switch(self, active, retiring):
        instances = [active, *retiring]
        for instance in instances:
            if not re.fullmatch(r'\d+\.\d+\.\d+', instance['version']) or not 1024 <= instance['port'] <= 65535:
                raise RuntimeError('Destino de proxy inválido.')
        content = f"default 127.0.0.1:{active['port']};\n" + ''.join(
            f'"{item["version"]}" 127.0.0.1:{item["port"]};\n' for item in instances)
        previous = self.proxy.read_text()
        temporary = self.proxy.with_suffix('.next')
        temporary.write_text(content); temporary.replace(self.proxy)
        try:
            self.command(['nginx', '-t'])
            self.command(['systemctl', 'reload', 'nginx'])
        except Exception:
            temporary.write_text(previous); temporary.replace(self.proxy)
            # Un reload fallido puede haber aplicado la configuración: restaurar el destino también en memoria.
            self.command(['nginx', '-t'])
            self.command(['systemctl', 'reload', 'nginx'])
            raise

    def remove(self, instance):
        # Nunca borrar volúmenes ni otros servicios del proyecto Compose original.
        identifier = instance['container']
        if not re.fullmatch(r'[0-9a-f]{12,64}', identifier):
            raise RuntimeError('Identificador de contenedor inválido.')
        present = self.command(['docker', 'ps', '-aq', '--no-trunc', '--filter', 'id=' + identifier], capture=True)
        if not present:
            return
        self.command(['docker', 'stop', '--time', '30', identifier])
        self.command(['docker', 'rm', identifier])

    def candidate(self, old, image, version):
        port = 28017 if old['port'] == 28015 else 28015
        project = 'arcana-roll-' + version.replace('.', '-')
        config = json.loads(self.command([*self.updater.base_command(), 'config', '--format', 'json'], capture=True))
        server = config['services']['server']
        server.pop('network_mode', None); server.pop('container_name', None); server.pop('depends_on', None)
        for mount in server.get('volumes', []):
            if mount.get('type') == 'bind' and not mount.get('read_only', False) and mount.get('target', '').startswith('/app/data'):
                raise RuntimeError('El despliegue progresivo necesita diarios separados: usa un volumen Docker para /app/data.')
            if mount.get('type') == 'volume' and not mount.get('read_only', False) and mount.get('target', '').startswith('/app/data') and \
                config.get('volumes', {}).get(mount.get('source'), {}).get('external'):
                raise RuntimeError('Un volumen externo de diarios no se puede compartir entre versiones.')
        # El puerto solo se publica en loopback. nginx conecta mediante el
        # gateway exacto de Docker, que se verifica antes de confiar en XFF.
        network = json.loads(self.command(['docker', 'network', 'inspect', 'bridge'], capture=True))[0]
        import ipaddress
        gateways = network.get('IPAM', {}).get('Config', [])
        gateway = next((entry.get('Gateway') for entry in gateways if entry.get('Gateway') and ipaddress.ip_address(entry['Gateway']).version == 4), None)
        if not gateway:
            raise RuntimeError('No se pudo determinar la IP exacta del proxy de Docker.')
        server.pop('networks', None); config.pop('networks', None); server['network_mode'] = 'bridge'
        server['image'] = image; server.pop('build', None)
        server['ports'] = [{'target': port, 'published': str(port), 'host_ip': '127.0.0.1', 'protocol': 'tcp'}]
        server['environment']['GAME_PORT'] = str(port)
        server['environment']['DISCOVERY_PORT'] = str(port + 1)
        server['environment']['ARCANA_DEPLOYMENT_KEY'] = self.key
        server['environment']['ARCANA_TRUSTED_PROXY_IP'] = gateway
        # Cada versión conserva sus propios diarios; no hay dos escritores sobre los mismos archivos.
        for volume in config.get('volumes', {}).values():
            if isinstance(volume, dict) and not volume.get('external'):
                volume.pop('name', None)
        config['services'] = {'server': server}; config['name'] = project
        path = self.updater.state / (project + '.json')
        self.write_json(path, config); path.chmod(0o600)
        compose = [*self.updater.compose, '--project-name', project, '-f', str(path)]
        self.command([*compose, 'up', '-d', '--no-build', '--no-deps', 'server'])
        container = self.command([*compose, 'ps', '-q', 'server'], capture=True)
        if not re.fullmatch(r'[0-9a-f]{12,64}', container):
            raise RuntimeError('No se pudo identificar el contenedor candidato.')
        return {'version': version, 'port': port, 'container': container, 'config': str(path)}

    def recover(self, state):
        pending = state.get('pending')
        if pending:
            # Recuperación de un cierre en mitad del cambio. Volver a la instancia
            # anterior es conservador; nunca detener un candidato que ya admite partidas.
            candidate = pending['candidate']; old = state['active']
            if pending.get('phase') == 'prepared':
                status = self.lifecycle(candidate)
                if status['connections'] or status['reservations'] or status['pendingDeliveries']:
                    raise RuntimeError('El candidato sin publicar tiene actividad; se conserva para revisión.')
                self.remove(candidate)
                state = {'active': old, 'retiring': []}
                self.write_json(self.file, state)
                return state
            status = self.lifecycle(candidate)
            if status['connections'] or status['reservations'] or status['pendingDeliveries']:
                self.switch(candidate, [old]); self.lifecycle(old, True)
                state = {'active': candidate, 'retiring': [old]}
            else:
                self.switch(old, []); self.lifecycle(old, False); self.remove(candidate)
                state = {'active': old, 'retiring': []}
            self.write_json(self.file, state)
        return state

    def reap(self, state):
        remaining = []
        for instance in state.get('retiring', []):
            if instance.get('removing'):
                # La decisión de retirada ya se persistió tras vaciar y quitar
                # su ruta. Reintentar rm también después de un corte de energía.
                self.switch(state['active'], remaining)
                self.remove(instance)
                continue
            status = self.lifecycle(instance)
            if status['connections'] or status['reservations'] or status['pendingDeliveries']:
                remaining.append(instance)
            else:
                # Quitar primero su ruta; una reconexión tardía ya no puede entrar mientras se detiene.
                self.switch(state['active'], remaining)
                instance['removing'] = True
                self.write_json(self.file, state)
                self.remove(instance)
        state['retiring'] = remaining
        self.write_json(self.file, state)
        return state

    def run(self, tag):
        if self.file.exists():
            state = self.reap(self.recover(json.loads(self.file.read_text())))
        else:
            container = self.updater.container()
            if not container or not container['State']['Running']:
                self.log('Servidor detenido; se respeta su estado.'); return
            current, _, base = self.updater.status(container)
            state = {'active': {'version': current, 'port': int(base.rsplit(':', 1)[1]), 'container': container['Id']}, 'retiring': []}
            self.lifecycle(state['active'])  # Sin soporte drain o sin clave correcta no se modifica el servidor.
            self.write_json(self.file, state)
        old = state['active']
        if tuple(map(int, tag[1:].split('.'))) <= tuple(map(int, old['version'].split('.'))):
            self.log('Sin novedades para el despliegue progresivo.'); return
        if state['retiring']:
            self.log('Actualización aplazada: todavía quedan partidas o recibos de la versión anterior.'); return
        self.updater.fetch_source(tag)
        image = 'arcana-arena-server:auto-' + tag
        self.updater.build(image, tag)
        candidate = self.candidate(old, image, tag[1:])
        state['pending'] = {'candidate': candidate, 'phase': 'prepared'}
        self.write_json(self.file, state)
        try:
            self.updater.wait_healthy(f"http://127.0.0.1:{candidate['port']}", candidate['version'])
            self.lifecycle(candidate)
            # Escribir la transacción antes de cambiar admisiones o proxy.
            state['pending'] = {'candidate': candidate, 'phase': 'switching'}
            self.write_json(self.file, state)
            self.lifecycle(old, True)
            self.switch(candidate, [old])
        except Exception:
            # Una recuperación incierta se conserva para el siguiente intento; no
            # cerrar contenedores si el proxy puede haberles entregado jugadores.
            self.write_json(self.file, state)
            raise
        state = {'active': candidate, 'retiring': [old]}
        self.write_json(self.file, state)
        self.reap(state)
        self.log('Nueva versión activa; las partidas anteriores conservan su conexión y destino de reconexión.')
