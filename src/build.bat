@echo off
rem Compila las dos ediciones y la consola (la edicion Server lleva dentro el panel web) con el compilador que ya trae Windows (.NET Framework 4.8)
rem Mono.Cecil (para reparar el mod de cajones) es el que trae BepInEx; en ejecucion se usa el del perfil.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set CECIL=lib\Mono.Cecil.dll
if not exist "%CECIL%" (echo Falta %CECIL%: copialo del BepInExPack (BepInExPack_Valheim/BepInEx/core) & exit /b 1)
set REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Web.Extensions.dll /r:System.Management.dll /r:System.Security.dll /r:"%CECIL%"
set WEBRES=/resource:web\index.html,index.html /resource:web\app.js,app.js /resource:web\estilo.css,estilo.css /resource:web\icono.svg,icono.svg /resource:web\admin.html,admin.html /resource:web\admin.js,admin.js /resource:web\estadisticas.html,estadisticas.html /resource:web\estadisticas.js,estadisticas.js /resource:web\mapa-es.js,mapa-es.js /resource:web\ayuda.html,ayuda.html /resource:web\ayuda.js,ayuda.js /resource:web\ayuda.json,ayuda.json /resource:web\jugador.html,jugador.html /resource:web\jugador.js,jugador.js /resource:web\vivo.html,vivo.html /resource:web\vivo.js,vivo.js /resource:web\historia.html,historia.html /resource:web\historia.js,historia.js /resource:web\mundo3d.html,mundo3d.html /resource:web\mundo3d.js,mundo3d.js /resource:web\mapa2d.js,mapa2d.js
set MODRES=/resource:mod\MajinheimMundo.cs,MajinheimMundo.cs
set OPTS=/nologo /optimize+ /platform:anycpu /codepage:65001 /win32icon:icon.ico

if not exist ..\distribucion mkdir ..\distribucion

"%CSC%" %OPTS% /target:winexe /define:SERVER /out:..\MajinheimServer.exe %REFS% %WEBRES% %MODRES% Core.cs Patch.cs Web.cs WebAdmin.cs Config.cs UI.cs ConfigPage.cs WebPage.cs AutoPage.cs AiPage.cs Extras.cs DiscordBot.cs AiChat.cs Rcon.cs Salud.cs SaludPage.cs Mundo.cs Vivo.cs Grupo.cs GrupoAuto.cs Soporte.cs Seguridad.cs Program.cs || goto :err
"%CSC%" %OPTS% /target:winexe /out:..\distribucion\Majinheim.exe %REFS% Core.cs Patch.cs Config.cs UI.cs ConfigPage.cs Soporte.cs Program.cs || goto :err
"%CSC%" %OPTS% /target:exe /define:SERVER /out:..\MajinheimCli.exe %REFS% %MODRES% /resource:web\ayuda.json,ayuda.json Core.cs Patch.cs Web.cs WebAdmin.cs AiChat.cs Extras.cs Salud.cs Mundo.cs Vivo.cs Grupo.cs Soporte.cs Config.cs Seguridad.cs Cli.cs || goto :err
echo Compilado OK
exit /b 0
:err
echo ERROR DE COMPILACION
exit /b 1
