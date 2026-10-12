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
        <button id="login">Entrar con Google</button><p id="status">Después del acceso, vuelve al juego.</p></main>
        <script type="module">
        import {initializeApp} from 'https://www.gstatic.com/firebasejs/12.4.0/firebase-app.js';
        import {getAuth,GoogleAuthProvider,signInWithPopup,setPersistence,inMemoryPersistence} from 'https://www.gstatic.com/firebasejs/12.4.0/firebase-auth.js';
        const auth=getAuth(initializeApp({{{config}}})),button=document.querySelector('#login'),status=document.querySelector('#status');
        await setPersistence(auth,inMemoryPersistence);
        button.onclick=async()=>{button.disabled=true;status.textContent='Abriendo Google…';try{
          const result=await signInWithPopup(auth,new GoogleAuthProvider());
          const response=await fetch({{{endpoint}}},{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({idToken:await result.user.getIdToken(),browserSecret:{{{secret}}}})});
          if(!response.ok)throw new Error('El servidor no pudo completar el acceso. Inícialo de nuevo desde el juego.');
          status.textContent='Acceso completado. Puedes volver a Arcana Arena.';button.textContent='Sesión iniciada';
        }catch(error){status.textContent=error.message||'No se pudo iniciar sesión.';button.disabled=false;}};
        </script></html>
        """;
    }
}
