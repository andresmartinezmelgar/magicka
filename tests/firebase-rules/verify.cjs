const fs = require("node:fs");
const path = require("node:path");
const { initializeTestEnvironment, assertSucceeds, assertFails } = require("@firebase/rules-unit-testing");
const { doc, collection, setDoc, getDoc, getDocs, updateDoc, deleteDoc, query, orderBy, limit, deleteField } = require("firebase/firestore");
const { createMockUserToken } = require("@firebase/util");

async function main() {
  const projectId = "demo-arcana-arena";
  const env = await initializeTestEnvironment({
    projectId,
    firestore: { host: "127.0.0.1", port: 8085,
      rules: fs.readFileSync(path.resolve(__dirname, "../../firebase/firestore.rules"), "utf8") }
  });
  try {
    const google = env.authenticatedContext("alice", { firebase: { sign_in_provider: "google.com" } }).firestore();
    const other = env.authenticatedContext("bob", { firebase: { sign_in_provider: "google.com" } }).firestore();
    const password = env.authenticatedContext("alice", { firebase: { sign_in_provider: "password" } }).firestore();
    const anonymous = env.unauthenticatedContext().firestore();
    const user = doc(google, "users/alice");
    const initial = { uid: "alice", name: "Mago", gold: 0, wins: 0, losses: 0, itemCount: 0, schemaVersion: 1 };

    // REST conserva doubleValue incluso cuando es 0.0; el SDK JS lo enviaría como int.
    async function restHeader(uid, field, typedValue) {
      const fields = { uid: { stringValue: uid }, name: { stringValue: "Mago" } };
      for (const key of ["gold", "wins", "losses", "itemCount", "schemaVersion"])
        fields[key] = { integerValue: key === "schemaVersion" ? "1" : "0" };
      if (field) fields[field] = typedValue;
      const token = createMockUserToken({ sub: uid, iat: Math.floor(Date.now() / 1000),
        firebase: { sign_in_provider: "google.com", identities: {} } }, projectId);
      return fetch(`http://127.0.0.1:8085/v1/projects/${projectId}/databases/(default)/documents/users/${uid}`, {
        method: "PATCH", headers: { Authorization: "Bearer " + token, "Content-Type": "application/json" },
        body: JSON.stringify({ fields })
      });
    }
    const typedHeader = await restHeader("typed_valid");
    if (!typedHeader.ok) throw new Error("La cabecera REST entera válida fue rechazada: " + typedHeader.status);
    for (const field of ["gold", "wins", "losses", "itemCount", "schemaVersion"]) {
      const response = await restHeader("typed_" + field, field, { doubleValue: field === "schemaVersion" ? 1 : 0 });
      if (response.status !== 403) throw new Error("Se admitió un double en el contador entero " + field + ": " + response.status);
    }
    console.log("ACCOUNT_HEADER_TYPES_OK: REST integer header allowed; double zero/one counter bypass rejected.");

    await assertSucceeds(setDoc(user, initial));
    await assertSucceeds(getDoc(user));
    await assertFails(getDoc(doc(other, "users/alice")));
    await assertFails(getDoc(doc(password, "users/alice")));
    await assertFails(getDoc(doc(anonymous, "users/alice")));
    await assertFails(setDoc(doc(other, "users/bob"), { ...initial, uid: "alice" }));
    await assertFails(setDoc(user, { ...initial, gold: 999999 }));
    await assertFails(setDoc(user, { ...initial, wins: 42 }));
    for (const entry of ["shop/current", "shop/simulatedAd", "shopPurchases/fake-receipt"]) {
      await assertFails(setDoc(doc(google, "users/alice/" + entry), { gold: 0, price: 0, completed: true }));
      await assertFails(getDoc(doc(google, "users/alice/" + entry)));
    }

    const bootsId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const hatId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    await env.withSecurityRulesDisabled(async context => {
      await setDoc(doc(context.firestore(), "users/alice/items/" + bootsId), { id: bootsId, slot: "boots" });
      await setDoc(doc(context.firestore(), "users/bob/items/" + hatId), { id: hatId, slot: "hat" });
    });
    await assertSucceeds(getDocs(collection(google, "users/alice/items")));
    await assertFails(getDocs(collection(other, "users/alice/items")));
    await assertFails(setDoc(doc(google, "users/alice/items/zz"), { id: "zz", slot: "boots" }));
    await assertSucceeds(setDoc(doc(google, "users/alice/loadouts/active"),
      { boots: bootsId, hat: "", chest: "", weapon: "" }));
    await assertFails(setDoc(doc(google, "users/alice/loadouts/active"),
      { boots: "", hat: hatId, chest: "", weapon: "" }));
    await assertFails(setDoc(doc(google, "users/alice/loadouts/active"),
      { boots: "", hat: "", chest: "", weapon: "", gold: 100 }));
    // Anillos: misma validación en creación y actualizaciones, seis lecturas máximas.
    const ringId = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
    const magicId = "ffffffffffffffffffffffffffffffff";
    const chestId = "11111111111111111111111111111111";
    const weaponId = "22222222222222222222222222222222";
    const ownHatId = "33333333333333333333333333333333";
    await env.withSecurityRulesDisabled(async context => {
      for (const [id, slot] of [[ringId, "goldRing"], [magicId, "magicRing"], [chestId, "chest"], [weaponId, "weapon"], [ownHatId, "hat"]])
        await setDoc(doc(context.firestore(), "users/alice/items/" + id), { id, slot });
    });
    const six = { boots: bootsId, hat: ownHatId, chest: chestId, weapon: weaponId, goldRing: ringId, magicRing: magicId };
    const active = doc(google, "users/alice/loadouts/active");
    await assertSucceeds(setDoc(active, six));
    await assertSucceeds(updateDoc(active, { goldRing: "" }));
    await assertSucceeds(updateDoc(active, { goldRing: ringId }));
    await assertFails(setDoc(doc(other, "users/alice/loadouts/active"), six));
    await assertFails(setDoc(doc(anonymous, "users/alice/loadouts/active"), six));
    await assertFails(setDoc(doc(password, "users/alice/loadouts/active"), six));
    await assertFails(setDoc(doc(google, "users/alice/loadouts/other"), six));
    for (const invalid of [magicId, hatId, "99999999999999999999999999999999", 42, null, "../" + ringId, "a".repeat(100000)]) {
      await assertFails(setDoc(active, { ...six, goldRing: invalid }));
      await assertFails(updateDoc(active, { goldRing: invalid }));
    }
    for (const bad of [{ gold: 100 }, { admin: true }, { boots: deleteField() }]) await assertFails(updateDoc(active, bad));
    await assertFails(updateDoc(doc(google, "users/alice/items/" + ringId), { upgradeLevel: 10 }));
    await assertSucceeds(setDoc(active, { boots: "", hat: "", chest: "", weapon: "" }));
    const cleared = (await getDoc(active)).data();
    if ("goldRing" in cleared || "magicRing" in cleared) throw new Error("Los anillos no se desequipan al sustituir el documento.");
    // El cliente C# usa PATCH sin máscara: sustituye las claves, no las fusiona.
    await assertSucceeds(setDoc(active, six));
    const patched = await fetch("http://127.0.0.1:8085/v1/projects/demo-arcana-arena/databases/(default)/documents/users/alice/loadouts/active", {
      method: "PATCH", headers: { Authorization: "Bearer owner", "Content-Type": "application/json" },
      body: JSON.stringify({ fields: { boots: { stringValue: bootsId }, hat: { stringValue: "" }, chest: { stringValue: "" }, weapon: { stringValue: "" } } })
    });
    if (!patched.ok) throw new Error("El PATCH real del emulador falló: " + patched.status);
    const restCleared = (await getDoc(active)).data();
    if ("goldRing" in restCleared || "magicRing" in restCleared) throw new Error("El PATCH conserva anillos que el usuario quitó.");
    console.log("RING_RULES_OK: six owned slots, old loadouts, replacement removal; ownership/type/slot/update bypass attacks rejected.");
    const readerA = env.authenticatedContext("readerA", { firebase: { sign_in_provider: "google.com" } }).firestore();
    const readerB = env.authenticatedContext("readerB", { firebase: { sign_in_provider: "google.com" } }).firestore();
    const wrongProvider = env.authenticatedContext("readerA", { firebase: { sign_in_provider: "password" } }).firestore();
    await env.withSecurityRulesDisabled(async context => {
      for (const uid of ["readerA", "readerB"]) await setDoc(doc(context.firestore(), "statisticsReaders/" + uid), { enabled: true });
      await setDoc(doc(context.firestore(), "matchStatistics/fixture"), { day: "2026-10-21", payload: "{}" });
    });
    for (const reader of [readerA, readerB]) {
      await assertSucceeds(getDoc(doc(reader, "matchStatistics/fixture")));
      await assertSucceeds(getDocs(query(collection(reader, "matchStatistics"), orderBy("day", "desc"), limit(10))));
    }
    await assertSucceeds(getDoc(doc(readerA, "statisticsReaders/readerA")));
    for (const denied of [google, other, anonymous, wrongProvider]) {
      await assertFails(getDoc(doc(denied, "matchStatistics/fixture")));
      await assertFails(getDocs(collection(denied, "matchStatistics")));
    }
    for (const client of [google, anonymous, readerA, readerB]) {
      await assertFails(setDoc(doc(client, "matchStatistics/new"), { payload: "identity" }));
      await assertFails(updateDoc(doc(client, "matchStatistics/fixture"), { payload: "changed" }));
      await assertFails(deleteDoc(doc(client, "matchStatistics/fixture")));
      await assertFails(setDoc(doc(client, "statisticsReaders/alice"), { enabled: true }));
      await assertFails(updateDoc(doc(client, "statisticsReaders/readerA"), { enabled: true }));
      await assertFails(getDocs(collection(client, "statisticsReaders")));
    }
    await assertFails(getDoc(doc(readerA, "statisticsReaders/readerB")));
    const runId = "cccccccccccccccccccccccccccccccc";
    const statistics = { schemaVersion: 1, runId, revision: 60, status: 1, day: "2026-10-22",
      mode: "Arena", map: "Classic", gameVersion: "0.29.8", complete: true, payload: "{}" };
    const statisticsId = data => `${data.runId}-${data.revision}-${data.status}`;
    const direct = doc(anonymous, "matchStatistics/" + statisticsId(statistics));
    await assertSucceeds(setDoc(direct, statistics));
    await assertSucceeds(setDoc(direct, statistics));
    await assertFails(getDoc(direct));
    await assertFails(getDocs(collection(anonymous, "matchStatistics")));
    await assertFails(updateDoc(direct, { payload: "changed" }));
    await assertFails(updateDoc(direct, { revision: 120 }));
    await assertFails(deleteDoc(direct));
    const later = { ...statistics, revision: 120 };
    await assertSucceeds(setDoc(doc(anonymous, "matchStatistics/" + statisticsId(later)), later));
    const before = { ...statistics, revision: 30, status: 2, complete: false };
    await assertSucceeds(setDoc(doc(anonymous, "matchStatistics/" + statisticsId(before)), before));
    await assertSucceeds(getDoc(doc(readerA, "matchStatistics/" + statisticsId(statistics))));
    const invalid = [
      { schemaVersion: 2 }, { runId: "not-a-guid" }, { revision: 0 }, { revision: -1 }, { revision: 1892160001 },
      { revision: "60" }, { status: 0, complete: false }, { status: 6 }, { complete: false }, { day: "2026-99-99" },
      { mode: "invented" }, { map: "invented" }, { gameVersion: "1.2.3-private" }, { payload: "" },
      { payload: "x".repeat(245761) }, { payload: {} }, { uid: "alice" }, { email: "private@example.com" }
    ];
    for (let i = 0; i < invalid.length; i++) {
      const data = { ...statistics, runId: i.toString(16).padStart(32, "0"), ...invalid[i] };
      await assertFails(setDoc(doc(anonymous, "matchStatistics/" + statisticsId(data)), data));
    }
    const missing = { ...statistics, runId: "dddddddddddddddddddddddddddddddd" }; delete missing.payload;
    await assertFails(setDoc(doc(anonymous, "matchStatistics/" + statisticsId(missing)), missing));
    await assertFails(setDoc(doc(anonymous, "matchStatistics/mismatched-id"), statistics));
    await env.withSecurityRulesDisabled(async context => { await updateDoc(doc(context.firestore(), "statisticsReaders/readerA"), { enabled: false }); });
    await assertFails(getDocs(collection(readerA, "matchStatistics")));
    console.log("MATCH_STATISTICS_RULES_OK: private reads, anonymous immutable reports, exact retries, bounded schema, no overwrite, role escalation rejected.");
    console.log("FIRESTORE_RULES_OK: Google owner reads, zero-balance registration, owned equipment; illegal writes rejected.");
  } finally { await env.cleanup(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
