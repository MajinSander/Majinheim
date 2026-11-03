# Majinheim

Launcher propio para el server de Valheim **Majinheim** (Valheim 1.0).
Sustituye a Thunderstore/r2modman: un solo programa que instala los mods, los mantiene al día y entra al server.

## Para jugar

1. Descarga **Majinheim.exe** de [Releases](https://github.com/MajinSander/Majinheim/releases/latest).
2. Guárdalo en una carpeta propia y corta (por ejemplo `C:\Majinheim`) y ábrelo.
3. Pulsa **JUGAR**. Se baja los mods solo, se actualiza solo y entra al server.

Si Windows avisa (SmartScreen): *Más información → Ejecutar de todas formas*.
Abrir Valheim normal desde Steam = sin mods. Para jugar con mods, siempre desde Majinheim.exe.
En el juego, **F1** abre el menú de ajustes de los mods.

## Web del server

En el launcher, el botón **Web del server** abre una página con el estado del server: si está encendido,
quién está jugando y desde cuándo, el día en el mundo, las horas de la semana, los mods, las estadísticas de cada vikingo
y el mapa del mundo (lo explorado, que se guarda también en los backups).
Se puede entrar como invitado (solo mirar) o con una cuenta; solo las cuentas de administrador ven los controles del server.
La opción **Recordarme en este dispositivo** evita tener que entrar cada vez.

## Discord y tareas automáticas
En el launcher del server, pestaña **Automático**: pega el token del bot y el ID del canal y pulsa "Guardar y probar". Ahí también se eligen los backups automáticos y el reinicio diario.

- El bot avisa cuando el server se enciende, se apaga o se cae, quién entra y sale, y si hay mods con versión nueva.
- Comandos: `/estado`, `/jugadores`, `/estadisticas` y `/web` para todos; `/encender`, `/apagar`, `/reiniciar`, `/backup` y `/avisar` solo para los administradores del Discord.

## Avisos en el juego
Antes de apagar o reiniciar el server con gente dentro, sale un aviso en el chat del juego 1 minuto y 10 segundos antes.
Usa el mod [ValheimRcon](https://thunderstore.io/c/valheim/p/Tristan/ValheimRcon/), que va **solo en el server** (los jugadores no instalan nada).
El launcher le pone una contraseña larga al azar y hace que solo acepte órdenes desde el propio PC del server: pestaña **Automático** → "Preparar".

## Cómo funciona

- `modpack.json` (en este repositorio) es la lista de mods y versiones. La publica el launcher del server.
- Los mods se descargan de [Thunderstore](https://thunderstore.io/c/valheim/). **Este repositorio no contiene ningún mod.**
- El mod de cajones (kg ItemDrawers, versión QuickFix de Firehunter1980) no funciona tal cual en Valheim 1.0:
  el launcher descarga el original y le aplica en tu PC un pequeño arreglo (`src/Patch.cs`) sin cambiar
  nombres ni datos, así los cajones ya construidos se conservan.
- Las actualizaciones del launcher salen de [Releases](https://github.com/MajinSander/Majinheim/releases).

## Código

C# (.NET Framework 4.8, sin dependencias externas). `src/build.bat` compila las dos ediciones:
`MajinheimServer.exe` (administra el server y lleva dentro la web, `src/Web.cs` + `src/web/`) y `Majinheim.exe` (jugadores).
La web solo escucha en el propio PC; desde internet se llega por HTTPS a través de [Caddy](https://caddyserver.com),
que el launcher descarga de sus Releases oficiales comprobando la huella SHA-512.
`src/lib/Mono.Cecil.dll` no se incluye: cópialo de `BepInExPack_Valheim/BepInEx/core`.

Créditos de los mods a sus autores en Thunderstore. Mod de cajones: KGvalheim y Firehunter1980.

— Majin
