# Novedades

## 1.7.0
- **Avisos en el juego**: si hay alguien jugando, antes de apagar o reiniciar desde la web, desde Discord o con el reinicio diario, sale un aviso en el chat del juego 1 minuto antes y otro 10 segundos antes.
- El aviso de Discord de mods con versión nueva queda solo con la lista de mods y su cambio de versión.
- **Mensajes al chat del juego** desde el panel de administración de la web, con `/avisar` en Discord (solo administradores) y desde el launcher.
- Usa el mod ValheimRcon (solo en el server). El launcher le pone una contraseña larga al azar y hace que solo acepte órdenes desde el propio PC. Se prepara en la pestaña **Automático**.

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
