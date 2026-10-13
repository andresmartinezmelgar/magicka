const fs = require("node:fs");
const path = require("node:path");
const { initializeTestEnvironment, assertSucceeds, assertFails } = require("@firebase/rules-unit-testing");
const { doc, collection, setDoc, getDoc, getDocs } = require("firebase/firestore");

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

    await assertSucceeds(setDoc(user, initial));
    await assertSucceeds(getDoc(user));
    await assertFails(getDoc(doc(other, "users/alice")));
    await assertFails(getDoc(doc(password, "users/alice")));
    await assertFails(getDoc(doc(anonymous, "users/alice")));
    await assertFails(setDoc(doc(other, "users/bob"), { ...initial, uid: "alice" }));
    await assertFails(setDoc(user, { ...initial, gold: 999999 }));
    await assertFails(setDoc(user, { ...initial, wins: 42 }));

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
    console.log("FIRESTORE_RULES_OK: Google owner reads, zero-balance registration, owned equipment; illegal writes rejected.");
  } finally { await env.cleanup(); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
