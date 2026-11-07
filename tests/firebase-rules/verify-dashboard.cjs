const fs=require('node:fs');
const path=require('node:path');
const {initializeTestEnvironment,assertFails,assertSucceeds}=require('@firebase/rules-unit-testing');
const {doc,setDoc,getDoc,getDocs,collection,updateDoc,deleteDoc,serverTimestamp,Timestamp,deleteField,query,where,orderBy}=require('firebase/firestore');
async function main(){
 const env=await initializeTestEnvironment({projectId:'demo-arcana-arena',firestore:{host:'127.0.0.1',port:8085,rules:fs.readFileSync(path.resolve(__dirname,'../../firebase/firestore.rules'),'utf8')}});
 try{
 const role={firebase:{sign_in_provider:'google.com'}};
 const a=env.authenticatedContext('dashA',role).firestore(),b=env.authenticatedContext('dashB',role).firestore(),guest=env.authenticatedContext('dashGuest',role).firestore(),anon=env.unauthenticatedContext().firestore(),password=env.authenticatedContext('dashA',{firebase:{sign_in_provider:'password'}}).firestore();
 await env.withSecurityRulesDisabled(async c=>{for(const uid of ['dashA','dashB'])await setDoc(doc(c.firestore(),'statisticsReaders/'+uid),{enabled:true});});
 const run='a'.repeat(32),name='dashboardTelemetry/'+run+'-dashA';
 const fresh=()=>({schemaVersion:1,runId:run,ownerUid:'dashA',day:'2026-11-07',revision:60,status:0,updatedAt:serverTimestamp(),payload:'{"schemaVersion":1}'});
 await assertSucceeds(setDoc(doc(a,name),fresh()));
 await assertSucceeds(getDoc(doc(a,name)));await assertSucceeds(getDoc(doc(b,name)));
 await assertSucceeds(getDocs(query(collection(a,'dashboardTelemetry'),where('day','>=','2026-11-01'),where('day','<=','2026-11-07'),orderBy('day','desc'))));
 for(const client of [guest,anon,password]){await assertFails(getDoc(doc(client,name)));await assertFails(getDocs(collection(client,'dashboardTelemetry')));await assertFails(setDoc(doc(client,name),fresh()));}
 await assertFails(setDoc(doc(b,name),fresh()));
 await assertFails(setDoc(doc(a,'dashboardTelemetry/'+run+'-dashB'),{...fresh(),ownerUid:'dashB'}));
 await assertSucceeds(updateDoc(doc(a,name),{revision:120,payload:'{"next":true}',updatedAt:serverTimestamp()}));
 for(const invalid of [{ownerUid:'dashB'},{runId:'b'.repeat(32)},{day:'2026-11-08'},{revision:1},{revision:1e15},{status:99},{payload:'a'.repeat(65537)},{payload:3},{ownerUid:'a'.repeat(1000)},{extra:'x'},{schemaVersion:deleteField()},{updatedAt:Timestamp.fromMillis(0)},{revision:'120'}])await assertFails(updateDoc(doc(a,name),{...invalid,updatedAt:Object.hasOwn(invalid,'updatedAt')?invalid.updatedAt:serverTimestamp()}));
 await assertSucceeds(updateDoc(doc(a,name),{status:1,updatedAt:serverTimestamp()}));
 await assertFails(updateDoc(doc(a,name),{status:0,revision:180,updatedAt:serverTimestamp()}));
 await assertFails(setDoc(doc(guest,'statisticsReaders/dashGuest'),{enabled:true}));
 await assertFails(getDoc(doc(b,'users/dashA')));
 await assertFails(deleteDoc(doc(b,name)));
 await env.withSecurityRulesDisabled(c=>updateDoc(doc(c.firestore(),'statisticsReaders/dashA'),{enabled:false}));
 await assertFails(getDoc(doc(a,name)));await assertFails(updateDoc(doc(a,name),{updatedAt:serverTimestamp()}));
 console.log('DASHBOARD_RULES_OK: private period queries; role, owner, schema, bounds, server time, monotonic revision, completion and revocation checked.');
 }finally{await env.cleanup();}
}
main().catch(e=>{console.error(e);process.exitCode=1;});
