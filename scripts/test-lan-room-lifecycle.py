#!/usr/bin/env python3
"""E2E HTTP: creación repetida, credenciales, validación y reciclado de salas vacías."""
import concurrent.futures
import json
import os
import urllib.error
import urllib.request

port = int(os.environ.get("GAME_PORT", "29051"))
password = os.environ.get("TEST_PASSWORD", "")
base = f"http://127.0.0.1:{port}/rooms"


def call(method="GET", payload=None):
    data = json.dumps(payload).encode("utf-8") if payload is not None else None
    request = urllib.request.Request(base, data=data, method=method)
    if data is not None:
        request.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(request, timeout=8) as response:
            return response.status, json.load(response)
    except urllib.error.HTTPError as error:
        return error.code, json.load(error)


def create(number):
    return call("POST", {
        "name": f"Sala reciclable {number}",
        "serverPassword": password,
        "mode": "duel",
        "maxPlayers": 2,
        "bots": 0,
        "bestOf": 5,
        "tickets": 25,
    })


status, listing = call()
assert status == 200 and isinstance(listing["rooms"], list), "No se pudo enumerar las salas"

wrong, _ = call("POST", {"name": "Sin permiso", "serverPassword": password + "-incorrecta"})
assert wrong == 403, "El servidor admitió una contraseña incorrecta"

invalid, _ = call("POST", {"name": "Imposible", "serverPassword": password, "maxPlayers": 1})
assert invalid == 400, "Se creó una sala inválida"

# Ejercita la saturación por encima del máximo de 15 salas visibles.
for number in range(24):
    code, room = create(number)
    assert code == 201, f"No se pudo crear la sala {number}: {code}: {room}"
    assert room["bestOf"] == 5 and room["inProgress"] is False, "Opciones de sala inesperadas"

# También simula varios jugadores creando salas casi simultáneamente.
with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
    responses = list(pool.map(create, range(24, 36)))
assert all(code == 201 for code, _ in responses), f"Error al crear salas concurrentes: {responses}"

status, directory = call()
assert status == 200
rooms = directory["rooms"]
assert len(rooms) == 15, f"Las salas vacías no se reciclan: {len(rooms)}"
assert len({room["id"] for room in rooms}) == len(rooms), "IDs de salas repetidos"
assert all(room["players"] == 0 for room in rooms), "Quedó una sesión fantasma en el servidor"
assert any(room["name"] == "Sala reciclable 35" for room in rooms), "Falta una sala recién creada"
print("LAN E2E: credenciales, validación, 36 creaciones y reciclado concurrente correctos.")
