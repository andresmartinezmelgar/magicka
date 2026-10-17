#!/usr/bin/env python3
import json
import sys

with open(sys.argv[1], encoding='utf-8') as source:
    data = json.load(source)
assert data['scope'] == 'integrated_survival_scene' and not data['canceled']
assert data['samples'] >= 3 and data['survival']['players'] == 4
assert data['survival']['simulatedTicks'] > 60 and data['survival']['portalCrossings'] >= 2
assert data['survival']['inventoryItems'] == 96 and data['survival']['liveSimulation']
assert data['metrics']['frameMs']['p95'] > 0 and data['metrics']['nodes']['maximum'] > 0
print('SURVIVAL_PROFILE_OK: simulación, cuatro defensores, inventario, portales y percentil 95.')
