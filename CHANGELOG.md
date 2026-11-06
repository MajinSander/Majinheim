# Novedades

## 2.2.0
- **La IA conoce el mundo**: en Discord se le puede preguntar "@MajinBot ¿dónde está el hierro?", "¿cuánta madera tenemos?", "¿cómo se hace la espada de hierro?", "¿para qué sirve la piel de lobo?" o "¿dónde está mi tumba?", y contesta con los datos reales del mundo, no de memoria.
- **Dónde está cada cosa**: mira todos los cofres, cajones, barcos y carros, junta los que están en el mismo sitio y lo explica con el portal más cercano ("a 40 m al norte del portal «Casa»") y los carteles de al lado. Dice cuánto hay en total, si hay parecidos (chatarra de hierro...) y si hay algo en alguna tumba. **Los cofres personales no se miran.**
- **Recetas de verdad**: materiales, mesa y nivel, lo que piden las mejoras y con qué herramienta se construye cada cosa, sacado del propio server con todos sus mods y en español. Si preguntas por un material, dice para qué sirve.
- **Tumbas**: de quién, cuándo murió, dónde está y qué lleva.
- Lo hace un mod propio, **Majinheim Mundo**, que va **solo en el server** (los jugadores no instalan nada) y **solo lee**: no cambia nada del mundo. Cada 5 minutos apunta lo que hay, poco a poco para no frenar la partida. El launcher lo prepara solo al arrancar el server, para la versión de Valheim de ese PC, y lo vuelve a preparar si Valheim se actualiza. Si algo falla, el server arranca igual, sin él.
- **Buscar en el mundo** también en la web: al entrar con cualquier cuenta (también las de jugador) sale la tarjeta "Buscar en el mundo", con lo mismo que contesta la IA. Los invitados sin cuenta no la ven.
- Se puede apagar en la pestaña **Asistente IA** ("Que sepa qué hay en los cofres...").
- Salud: nueva línea "La IA conoce el mundo" (si el mod está funcionando y de cuándo son los datos).
- Consola: `MajinheimCli mundo` (prepara el mod y dice qué sabe), y `buscar <objeto>`, `receta <objeto>` y `tumbas [jugador]` para ver lo mismo que consulta la IA.

## 2.1.0
- **Mantenimiento diario sin sustos**: a la hora del reinicio diario, y solo si no hay nadie jugando, el launcher del server mira si hay versiones nuevas de los mods o de Valheim. Si las hay, hace backup del mundo, los ajustes y la lista de mods; apaga el server; deja que Steam actualice Valheim; instala los mods nuevos y **prueba que el server arranca y carga el mundo**. Si un mod nuevo da errores, lo deja en la versión de antes y se queda con los demás; si aun así no arranca, vuelve a dejarlo todo como estaba (también el mundo, si la prueba llegó a guardarlo). Al terminar, el server queda como estaba (encendido o apagado), tus amigos reciben los mods nuevos al abrir su launcher y en Discord sale qué se ha hecho.
- Para saber si un arranque ha ido bien se comparan sus errores con los de antes: solo cuentan los **nuevos** que vienen del cargador de mods o de un mod actualizado. Los de siempre (gráficos, vídeo...) no hacen saltar la alarma.
- Una versión de un mod que falla la prueba no se vuelve a intentar sola: espera a la siguiente versión o a que alguien la pida a mano ("Reintentar las que fallaron").
- **Actualizaciones de Valheim sin esperar**: si hay una esperando, los que ya tienen el juego actualizado no pueden entrar, así que se instala y se prueba en cuanto el server lleva un rato sin nadie, sin esperar al mantenimiento diario.
- Nueva pestaña **Salud** en el launcher del server: server, último backup, backups comprobados, copia fuera del PC, espacio en disco, mods, Valheim, HTTPS y DDNS, cortafuegos, horas activas de Windows Update, arranque con Windows, Discord, asistente IA y el último mantenimiento, cada uno en verde, amarillo o rojo con lo que hay que hacer. Se revisa sola cada 10 minutos y avisa en Discord si algo importante se estropea (disco casi lleno, backups, HTTPS, DDNS, IA) y cuando se arregla. Botones para revisar, actualizar ahora (con prueba) y comprobar los backups, e historial de los últimos mantenimientos.
- **Backups comprobados**: una vez por semana se abre el último backup del mundo y se lee entero (y el de la copia fuera del PC), para enterarse antes de necesitarlo si alguno está dañado.
- **Mods a vigilar**: aviso en Discord (una vez) de los mods que su autor ha marcado como obsoletos en Thunderstore o que llevan mucho sin versión nueva. En la pestaña Mods, los obsoletos salen en rojo.
- Discord: `/salud` (la revisión, solo la ve quien la pide) y `/actualizar` (el mantenimiento ahora, con prueba), los dos solo para administradores.
- Web de administración: tarjeta **Salud del server**, botón "Actualizar ahora (con prueba)" e historial de mantenimientos.
- Consola: `MajinheimCli salud`, `comprobar-backups`, `errores` (los errores del último arranque que no salían antes) y `actualizar [reintentar]` (con el server apagado y el launcher cerrado).
- Web: el cuadro de "Mensaje al chat del juego" ya no se sale de la tarjeta en pantallas estrechas.

## 2.0.0
- **Asistente con IA**: en Discord se le habla mencionando al bot con @, como a uno más del grupo ("@MajinBot ¿quién jugó ayer?", "¿cuántas horas lleva Benja este mes?", "¿qué jefe nos falta?") y responde con los datos reales del server: quién juega, horas y partidas de cada vikingo, días y horas en que se juega, muertes, jefes, mods, último backup y día del mundo. También resuelve dudas generales de Valheim y recuerda lo último hablado en el canal para seguir la conversación. El bot solo ve el texto de los mensajes que lo mencionan: no lee el resto de conversaciones.
- La IA funciona **en el propio PC del server** con [Ollama](https://ollama.com): ni las preguntas ni los datos salen a internet, y no cuesta nada.
- **Solo lee**: no puede encender, apagar ni cambiar nada, y nunca ve IDs de Steam ni IPs. A los administradores del Discord les puede mirar también el registro del server para buscar errores.
- Nueva pestaña **Asistente IA** en el launcher del server: instalar Ollama, descargar el modelo con su progreso, elegir modelo (Qwen3 14B por defecto; Qwen3 8B para gráficas de 8 GB), activar las respuestas en Discord y probar preguntas sin salir del launcher.
- Antes de responder revisa que los números salgan de los datos reales; si no, rehace la respuesta o dice que no lo sabe.
- Una pregunta cada 10 segundos por persona y una a la vez en la gráfica, para no cargar el PC mientras se juega.

## 1.8.3
- **El mapa de la web, en español**: menús, botones, ayudas, estadísticas de jugadores, portales, mundo, plano y la visita guiada. Los nombres del juego van como en Valheim en español (Prados, Selva Negra, El Anciano, Masa Ósea…), y los comandos del chat (`!pin`…) no cambian. Se traduce en la propia web, sin tocar el mod, así que sigue en español aunque el mod se actualice.
- Jefes derrotados: Eikthyr sale en los Prados y Bonemass como Masa Ósea, como en el juego.

## 1.8.2
- "Comprobar desde internet" (pestaña **Web**) ya no dice que falla cuando el router simplemente no deja abrir la dirección pública desde dentro de casa: comprueba el HTTPS del propio PC, que la DDNS apunte a casa y las visitas que han llegado de internet.
- `/estado` en Discord dice también si hay una actualización de Valheim esperando.
- Si el server se cae 3 veces seguidas nada más arrancar (por ejemplo, un mod que no funciona tras una actualización de Valheim), el launcher deja de reintentarlo y avisa en Discord, en vez de reintentar sin fin (cada arranque hace un backup y acabaría borrando los buenos más antiguos).
- Los registros del launcher y del HTTPS ya no crecen sin fin: al pasar de 5 MB se guarda el anterior y se empieza otro.

## 1.8.1
- **Actualizaciones de Valheim**: Steam actualiza el server solo, cuando está apagado. El launcher avisa en Discord, en la pestaña **Automático** y en la web cuando hay una actualización esperando y cuando ya se ha instalado (con una versión nueva del juego, algún mod puede necesitar actualizarse).
- La web pide a los buscadores (Google y demás) que no la guarden ni la enseñen: salen nombres de jugadores.

## 1.8.0
- **Consola del server** en el panel de administración de la web: órdenes como `players`, `banlist`, `unban`, `save` o `serverStats`, con botones rápidos. Solo se permiten órdenes seguras (nada que borre cosas del mundo) y todo queda apuntado en el registro.
- **Expulsar o banear** a un jugador conectado desde el panel, con confirmación.
- **Jefes derrotados**: la web enseña qué jefes ha vencido el grupo y cuándo, `/estado` en Discord lo resume, y al caer uno nuevo sale un aviso en Discord y en el chat del juego.
- **Copia fuera del PC**: en la pestaña Automático se elige una carpeta en otro disco, un USB u OneDrive, y cada backup del mundo se copia también allí (se guardan los 10 últimos).

## 1.7.2
- Las copias del mundo guardan también lo explorado del mapa de la web.
- En la web, "Último backup" enseña la última copia del mundo (no las de ajustes que se hacen al instalar mods).
- Corregido: una copia de ajustes reciente podía retrasar la siguiente copia programada del mundo.

## 1.7.1
- Corregido: en la web, "Último backup" podía enseñar uno antiguo en vez del más reciente.

## 1.7.0
- **Avisos en el juego**: si hay alguien jugando, antes de apagar o reiniciar desde la web, desde Discord o con el reinicio diario, sale un aviso en el chat del juego 1 minuto antes y otro 10 segundos antes.
- El aviso de Discord de mods con versión nueva queda solo con la lista de mods y su cambio de versión.
- **Mensajes al chat del juego** desde el panel de administración de la web, con `/avisar` en Discord (solo administradores) y desde el launcher.
- Usa el mod ValheimRcon (solo en el server). El launcher le pone una contraseña larga al azar y hace que solo acepte órdenes desde el propio PC. Se prepara en la pestaña **Automático**.
- Corregido: al instalar o actualizar mods se borraba el mapa de la web (lo explorado y el dibujo del mundo). Ahora se conserva.

## 1.6.0
- **Comandos del bot de Discord**: `/estado`, `/jugadores`, `/estadisticas` y `/web` para todos; `/encender`, `/apagar`, `/reiniciar` y `/backup` solo para los administradores del Discord. El bot no lee mensajes ni ve a nadie: solo recibe los comandos. Se activa o desactiva en la pestaña **Automático**.
- Lo que se hace desde Discord queda apuntado en el mismo registro que lo que se hace desde la web.

## 1.5.0
- **Estadísticas** en la web (botón "Estadísticas"): horas jugadas por cada vikingo, partidas, la más larga, muertes, horas de los últimos 30 días y a qué hora se juega. Las ve cualquiera, sin datos de red.
- **Avisos en Discord** con el bot del grupo: server encendido, apagado o caído; quién entra y sale y cuánto ha jugado; backups automáticos que fallen; mods con versión nueva. Nunca menciona a nadie. El token se guarda cifrado y solo sirve en el PC del server.
- **Tareas automáticas** (nueva pestaña **Automático** del launcher del server):
  - Backup del mundo cada pocas horas con el server encendido, justo después de que Valheim guarde, y solo si alguien ha jugado.
  - Reinicio diario a la hora que elijas, solo si no hay nadie jugando.
  - Búsqueda de versiones nuevas de mods cada 12 horas.
- Corregido: al activar "Acceso desde internet" salía el error "No se puede convertir un objeto de tipo System.Object[]..." y no se preparaba el HTTPS.
- Si el acceso desde internet está activado y falta el programa del HTTPS (por ejemplo, porque falló la descarga), se prepara solo al abrir el launcher.
- **En casa**: muchos routers no dejan abrir la dirección pública desde dentro de la propia casa. Con el acceso desde internet activado, en la red de casa el estado, las estadísticas y el mapa también se abren en la dirección local del server (la pestaña **Web** la muestra), sin abrir nada más en el router. Para entrar con una cuenta hace falta la dirección segura: la pestaña **Web** da la línea para el archivo hosts de un PC de casa.
- Panel de administración: últimos backups, mods con versión nueva y el estado de las tareas automáticas y de Discord.

## 1.4.2
- Corregido: el mapa del mundo de la web salía en negro (no cargaba el dibujo del mundo).

## 1.4.1
- **Cuentas en la web**: se entra con usuario y contraseña. Los administradores ven los controles del server; las cuentas de jugador y los invitados ven el estado, las estadísticas y el mapa, sin datos de red.
- Botón **Entrar como invitado** para quien no tiene cuenta.
- **Recordarme en este dispositivo**: no hace falta volver a entrar durante 30 días desde la última visita. El administrador ve las sesiones abiertas y puede cerrarlas todas de una vez.
- Nuevo dato en la web: **día en el mundo**.
- Las cuentas se crean y se cambian en la pestaña **Web** del launcher del server.

## 1.4.0
- **Administrar desde la web** (móvil o PC): encender, apagar, reiniciar y hacer backups del server, y ver su registro. Solo con la contraseña de administrador.
- La contraseña se pone en la pestaña **Web** del launcher del server. Sin contraseña, la administración web está desactivada.
- Seguridad: la contraseña no se guarda en claro, tras varios intentos fallidos se bloquea un rato y queda apuntado todo lo que se hace desde la web.

## 1.3.0
- **Mapa del mundo** en la web del server: lo que habéis explorado entre todos, quién está jugando y dónde (si se deja ver en el mapa), portales y construcciones. Lo pone el mod WebMap, que va solo en el server: no hay que instalar nada.
- El mapa se ve dentro de la misma web, con su candado HTTPS, y es solo para mirar.

## 1.2.0
- Nueva **web del server**: si está encendido, quién está jugando y desde cuándo, horas de la semana, últimas partidas y lista de mods. Se ve bien en el móvil y se actualiza sola.
- Launcher de jugador: botón **Web del server** para abrirla directamente.
- Edición Server: nueva pestaña **Web** para encenderla y abrirla desde internet con HTTPS (el certificado se saca y se renueva solo).

## 1.1.3
- La consola del juego (tecla **F5**) viene activada al jugar desde el launcher.

## 1.1.2
- Privacidad: la dirección de la red local del server ya no se publica. Quien juegue en la misma casa que el server puede indicarla en **Ajustes**.
- El aviso de actualización ahora muestra las novedades de la versión nueva.

## 1.1.1
- Si juegas en la misma casa que el server, el launcher entra por la red local automáticamente.
- Corregido: en algunos equipos el launcher no podía conectar con GitHub.

## 1.1.0
- La lista de mods y las actualizaciones del launcher llegan desde GitHub: el launcher se actualiza solo.
- Mod de cajones (ItemDrawers) compatible con Valheim 1.0, conservando los cajones ya construidos.
- Nuevo menú de ajustes de los mods dentro del juego (tecla **F1**).
- El launcher del server permite cambiar los ajustes de los mods.

## 1.0.0
- Primera versión: instala y actualiza los mods sin Thunderstore ni r2modman, y entra al server con un botón.
