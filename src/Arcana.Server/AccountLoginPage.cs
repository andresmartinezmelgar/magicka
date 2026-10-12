using System.Text.Json;

namespace Arcana.Server;

internal static class AccountLoginPage
{
    public static string Render(AccountOptions options, string id, string key)
    {
        string config = JsonSerializer.Serialize(new { apiKey = options.ApiKey, authDomain = options.AuthDomain, projectId = options.ProjectId });
        string endpoint = JsonSerializer.Serialize(options.PublicUrl + "/account/login/" + id + "/complete");
        string secret = JsonSerializer.Serialize(key);
        return $$$"""
        <!doctype html><html lang="es"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Acceso a Arcana Arena</title>
        <style>body{margin:0;min-height:100vh;display:grid;place-items:center;background:#081322;color:#eef3fa;font:18px system-ui}main{max-width:440px;padding:32px}h1{color:#e8c574}button{padding:16px 24px;font:inherit;background:#e8c574;border:0;color:#081322;cursor:pointer}p{line-height:1.5}#status{min-height:70px}</style>
        <main><h1>ARCANA ARENA</h1><p>Inicia sesión para recuperar tu mago, equipamiento y oro.</p>
        <button id="login" disabled>Preparando Google…</button><p id="status" role="status">Cargando el acceso seguro…</p><noscript>Activa JavaScript para iniciar sesión.</noscript></main>
        <script type="module">
        const button=document.querySelector('#login'),status=document.querySelector('#status');
        const errors={
          'auth/popup-blocked':'Permite abrir ventanas emergentes en este navegador y vuelve a pulsar Entrar con Google.',
          'auth/popup-closed-by-user':'Has cerrado Google. Puedes volver a intentarlo.',
          'auth/unauthorized-domain':'El acceso todavía no está disponible desde este dominio. Contacta con el administrador del juego.',
          'auth/operation-not-allowed':'El servidor todavía no ha activado Google Login. Contacta con el administrador del juego.',
          'auth/network-request-failed':'No se pudo conectar con Google. Comprueba la conexión y vuelve a intentarlo.'
        };
        try{
          const [{initializeApp},{getAuth,GoogleAuthProvider,signInWithPopup,setPersistence,inMemoryPersistence,signOut}]=await Promise.all([
            import('https://www.gstatic.com/firebasejs/12.4.0/firebase-app.js'),import('https://www.gstatic.com/firebasejs/12.4.0/firebase-auth.js')]);
          const auth=getAuth(initializeApp({{{config}}})),provider=new GoogleAuthProvider();
          provider.setCustomParameters({prompt:'select_account'});
          await setPersistence(auth,inMemoryPersistence);
          button.disabled=false;button.textContent='Entrar con Google';status.textContent='Elige tu cuenta Google y después vuelve al juego.';
          button.onclick=async()=>{button.disabled=true;status.textContent='Abriendo Google…';try{
          const result=await signInWithPopup(auth,provider);
          const response=await fetch({{{endpoint}}},{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({idToken:await result.user.getIdToken(),browserSecret:{{{secret}}}})});
          if(!response.ok){const error=await response.json().catch(()=>null);throw new Error(error?.error||'El servidor no pudo completar el acceso. Inícialo de nuevo desde el juego.');}
          await signOut(auth);
          status.textContent='Acceso completado. Puedes volver a Arcana Arena.';button.textContent='Sesión iniciada';
          history.replaceState(null,'',location.pathname);
          }catch(error){status.textContent=errors[error.code]||error.message||'No se pudo iniciar sesión.';button.disabled=false;}};
        }catch(error){status.textContent='No se pudo cargar el acceso Google. Comprueba la conexión y recarga esta página.';button.textContent='Google no disponible';}
        </script></html>
        """;
    }
}
