// Firebase API keys identify the public project; access is enforced by Auth + Firestore rules.
export const firebaseConfig={apiKey:'AIzaSyAq5LTlzFLFswFdfPAlvPtkbkjqUj3yM4M',authDomain:'arcana-arena-f2997.firebaseapp.com',projectId:'arcana-arena-f2997'};
const CDN='https://www.gstatic.com/firebasejs/11.10.0/';
let sdk,auth,db,unsubscribers=[],generation=0;
export async function initialize(onUser){
 if(!sdk){const [app,a,f]=await Promise.all([import(CDN+'firebase-app.js'),import(CDN+'firebase-auth.js'),import(CDN+'firebase-firestore.js')]);sdk={...a,...f};const instance=app.initializeApp(firebaseConfig);auth=a.getAuth(instance);db=f.getFirestore(instance);await a.setPersistence(auth,a.browserSessionPersistence);}
 return sdk.onAuthStateChanged(auth,onUser);
}
export async function login(){const provider=new sdk.GoogleAuthProvider();provider.setCustomParameters({prompt:'select_account'});return sdk.signInWithPopup(auth,provider);}
export async function logout(){stop();await sdk.signOut(auth);}
export function stop(){generation++;for(const unsub of unsubscribers)unsub();unsubscribers=[];}
export async function subscribe(range,callbacks){
 stop();const current=generation;const user=auth.currentUser;if(!user)throw Error('Inicia sesión con Google.');
 const access=await sdk.getDoc(sdk.doc(db,'statisticsReaders',user.uid));if(current!==generation)return;
 if(!access.exists()||access.data().enabled!==true)throw Error('Esta cuenta no tiene habilitada la lectura de estadísticas.');
 function listen(collectionName,handler,isLive){const q=sdk.query(sdk.collection(db,collectionName),sdk.where('day','>=',range.start),sdk.where('day','<=',range.end),sdk.orderBy('day','desc'));unsubscribers.push(sdk.onSnapshot(q,{includeMetadataChanges:true},snap=>{if(current!==generation)return;handler(snap.docs.map(d=>({...d.data(),id:d.id})),snap.metadata.fromCache);},error=>{if(current===generation)callbacks.error(error,isLive);}));}
 listen('matchStatistics',callbacks.history,false);listen('dashboardTelemetry',callbacks.live,true);
}
export function friendlyError(error){const code=error?.code||'';if(code.includes('unauthorized-domain'))return 'Autoriza '+location.hostname+' en Firebase Authentication → Configuración → Dominios autorizados.';if(code.includes('popup-blocked'))return 'El navegador bloqueó la ventana de Google. Permite ventanas emergentes para esta página.';if(code.includes('permission-denied'))return 'Acceso denegado. Comprueba el permiso statisticsReaders y las reglas de dashboardTelemetry.';if(code.includes('network-request-failed')||code==='unavailable')return 'Sin conexión con Firebase. Se reintentará al recuperar la red.';if(code.includes('popup-closed-by-user'))return 'Se cerró el inicio de sesión.';return error?.message||'No se pudo conectar con Firebase.';}
