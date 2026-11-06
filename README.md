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
- **Vida en el Discord**: `/quedada viernes 22:00` para quedar (el bot apunta quién va, avisa 15 minutos antes y enciende el server), resumen de la semana los domingos, hitos (100 horas, récords…) y, si quieres, server bajo demanda (cualquiera lo enciende con `/encender` y se apaga solo vacío).
- Comandos: `/estado`, `/jugadores`, `/estadisticas`, `/web`, `/quedada` y `/quedadas` para todos; `/encender`, `/apagar`, `/reiniciar`, `/backup`, `/avisar`, `/salud` y `/actualizar` solo para los administradores del Discord.

## Salud y mantenimiento sin sustos
En el launcher del server, pestaña **Salud**: una revisión de todo lo que hace falta para que el server funcione (backups, copia fuera del PC, disco, mods, Valheim, HTTPS, DDNS, cortafuegos, Windows Update, Discord, IA...), en verde, amarillo o rojo y con lo que hay que hacer. Se repite sola cada 10 minutos y avisa en Discord si algo importante se estropea.

- **Mantenimiento diario**: a la hora del reinicio diario y si no hay nadie jugando, se actualizan los mods y Valheim con backup antes, **prueba de arranque** y vuelta atrás si algo falla. Si un mod nuevo da problemas, se queda en su versión de antes y no se reintenta solo.
- Cada semana se comprueba que el último backup del mundo se abre entero.
- Discord: `/salud` y `/actualizar` (solo administradores). En la web de administración, la tarjeta **Salud del server**.

## Asistente con IA
En Discord se le habla mencionando al bot con @, como a uno más del grupo: "@MajinBot ¿quién jugó ayer?", "¿cuántas horas lleva Benja este mes?", "¿qué jefe nos falta?". Recuerda lo último hablado en el canal, así que se le puede seguir preguntando ("¿y la semana pasada?").
La IA funciona en el propio PC del server con [Ollama](https://ollama.com) (nada sale a internet), solo lee datos y nunca ve IDs de Steam ni IPs.
Se prepara en la pestaña **Asistente IA** del launcher del server: instalar Ollama, descargar el modelo y activarla. Discord solo le pasa al bot el texto de los mensajes que lo mencionan: no lee el resto de conversaciones.
También conoce el mundo: "¿dónde está el hierro?", "¿cómo se hace la espada de hierro?", "¿dónde está mi tumba?". Lo saca un mod propio, **Majinheim Mundo** (`src/mod/`), que va solo en el server, solo lee (no cambia nada) y apunta cada 5 minutos qué hay en cofres, cajones y tumbas, y las recetas del server con sus mods, en español. Los cofres personales no se miran. El launcher del server lo compila solo en su PC para la versión de Valheim instalada.
Va mejor con una tarjeta gráfica de 12 GB (modelo Qwen3 14B, responde en 1-2 segundos); con 8 GB se puede usar el Qwen3 8B, más rápido pero menos fino. Parada no gasta: el modelo se descarga solo a los 15 minutos sin preguntas.

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
