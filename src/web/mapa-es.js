// Majinheim: el mapa del mod WebMap en español. Se carga con site-config.js en todas sus páginas y cambia los textos
// en inglés de la página (y los que el mapa dibuja) por su traducción. No toca el código del mod: si el mod trae un
// texto nuevo, sale en inglés hasta añadirlo aquí. Los nombres del juego, como en Valheim en español.
(function () {
  'use strict';
  var T = {
    'Show or hide the sidebar': 'Mostrar u ocultar la barra lateral', 'Sidebar': 'Barra lateral', 'state-as-code': 'estado en código', 'live': 'en vivo',
    'Switch to Atlas (L)': 'Cambiar a Atlas (L)', 'Switch to Terrain (L)': 'Cambiar a Relieve (L)', 'night': 'noche', 'cooked': 'cocinado', 'boats': 'barcos', 'places': 'lugares',
    'continent': 'continente', 'island': 'isla', 'holm': 'islote', 'No walking tallied yet.': 'Todavía no se ha contado ningún paseo.', 'Hub': 'Nudo', 'linked': 'enlazado',
    'Elsewhere': 'En otra parte', 'Close up': 'De cerca', 'close up': 'de cerca', 'See this gate on the map': 'Ver este portal en el mapa', 'per death': 'por muerte',
    'Copy link puts the whole plan in the URL.': 'Copiar enlace mete todo el plano en la dirección.', 'Layers are live reference, not part of your plan.': 'Las capas son referencia en vivo, no parte de tu plano.',
    'thin': 'fino', 'thick': 'grueso',
    'Online:': 'Conectados:', 'nothing yet': 'nada todavía',
    'not shown on map — enable “visible to other players” on the in-game map (M)': 'no salen en el mapa: activa “visible para otros jugadores” en el mapa del juego (M)',
    'nobody has said anything yet': 'nadie ha dicho nada todavía',
    'The server is not answering right now. This page retries every minute.': 'El server no responde ahora mismo. Esta página lo vuelve a intentar cada minuto.',
    "— creatures that fell, trees felled and rock areas broken with you as the last to hit them, or, when your own game was running the fight, with you standing there; a creature that despawns is nobody's kill.":
      '— criaturas que cayeron, árboles talados y rocas rotas con el último golpe tuyo o, si tu juego llevaba la pelea, estando tú allí; una criatura que desaparece no cuenta para nadie.',
    ", each bar, coal, flour or thread a smelter, kiln, windmill, spinning wheel or refinery made, for whoever loaded it. The server sees the stations, never the bag: the cauldron and the preparation table are invisible, so of bread and pies only the oven's step shows. At a fermenter, a hive or a smelter the player standing within 8 m is the one credited, and a slot filled before the count began counts for nobody.":
      ', cada lingote, carbón, harina o hilo que hizo una fundición, horno de carbón, molino, rueca o refinería, para quien la cargó. El server ve las estaciones, nunca el inventario: el caldero y la mesa de preparación no se ven, así que del pan y las empanadas solo cuenta el paso por el horno. En un fermentador, una colmena o una fundición cuenta para el jugador que esté a menos de 8 m, y lo cargado antes de empezar la cuenta no cuenta para nadie.',
    "— a title in three parts. The first word is a compound: what you fight with gives the prefix (battle for a two-hander, blade, shield, bow, knife, spell) and what your body is gives the root (mage, knight, warrior, rogue, brute), read from what you wear and from the food behind your bars, so a two-hander in eitr-weave is a Battlemage and a shield in heavy armour a Paladin; a few weapons colour it (a crossbow an Arbalist, an atgeir a Vanguard, axe and shield a Huscarl). The second word is your trade, from what you have done: the roster's top builder is the Architect, a builder who also mines an Engineer, who also fells a Carpenter, who also farms a Homesteader; a Cook, Baker, Brewer or Smith from fifty things made; the Slayer; Builders, Miners, Lumberjacks, Farmers, Explorers, Seafarers, Anglers and Wayfarers. Time in hand is the evidence: the server sees hands and back slots, never the bag, and blows only on creatures another player's game runs; skills never leave your character file. Dashed until ten minutes have had something in hand.":
      '— un título en tres partes. La primera palabra es compuesta: sale de con qué luchas (arma a dos manos, espada, escudo, arco, cuchillo, magia) y de cómo vas (mago, caballero, guerrero, pícaro, bruto), según lo que llevas puesto y lo que comes; así, un arma a dos manos con ropa de eitr es un Mago de batalla y un escudo con armadura pesada, un Paladín. Algunas armas lo cambian (una ballesta, Ballestero; un atgeir, Vanguardia; hacha y escudo, Huscarle). La segunda es tu oficio, según lo que has hecho: el que más construye es el Arquitecto; un constructor que además mina, Ingeniero; que además tala, Carpintero; que además cultiva, Granjero; Cocinero, Panadero, Cervecero o Herrero tras hacer cincuenta cosas; el Cazador; y Constructores, Mineros, Leñadores, Agricultores, Exploradores, Navegantes, Pescadores y Caminantes. Se mira lo que llevas en la mano: el server ve las manos y la espalda, nunca el inventario, y las habilidades nunca salen de tu personaje. Sale en blanco hasta llevar diez minutos con algo en la mano.',
    "— five sides, each your share of the roster's best, square-rooted so one giant does not flatten everyone: Explorer is km, portal hops and km at sea taken together; Builder pieces standing; Survivor km per death; Slayer kills; Harvester trees, rocks, dishes cooked and things smelted together.":
      '— cinco lados, cada uno lo que llevas frente al mejor del grupo (suavizado para que uno muy fuerte no deje a todos en nada): Explorador son los km, saltos por portal y km por mar; Constructor, las piezas en pie; Superviviente, km por muerte; Cazador, criaturas abatidas; Recolector, árboles, rocas, platos cocinados y cosas fundidas.',
    'one retag away · drag to pan, scroll or pinch to zoom': 'a un cambio de etiqueta · arrastra para mover, rueda o pellizca para acercar',
    // ---- general y navegación
    'Valheim WebMap': 'Mapa de Majinheim', 'Valheim world map': 'Mapa del mundo de Valheim', 'Map': 'Mapa', 'Players': 'Jugadores',
    'Portals': 'Portales', 'World': 'Mundo', 'Plan': 'Plano', 'Settings': 'Ajustes', 'Close': 'Cerrar', 'Next': 'Siguiente',
    'Online': 'En línea', 'Offline': 'Apagado', 'Status unavailable': 'Estado no disponible', 'Server unreachable': 'Sin conexión con el server',
    'server unreachable': 'sin conexión con el server', 'nobody online': 'nadie conectado', 'Server': 'Server', 'checking…': 'comprobando…',
    'loading…': 'cargando…', 'counting…': 'contando…', 'reading the world…': 'leyendo el mundo…', 'Save': 'Guardar', 'Saved': 'Guardado',
    'Failed': 'Ha fallado', 'Copied': 'Copiado', 'Undo': 'Deshacer', 'Clear': 'Borrar', 'Share': 'Compartir', 'Default': 'Por defecto',
    'none yet': 'ninguno todavía', 'just now': 'ahora mismo', 'moments ago': 'hace un momento', 'could not save': 'no se pudo guardar',
    'Day': 'Día', 'Map mod': 'Mod del mapa',

    // ---- barra lateral del mapa
    'Show': 'Mostrar', 'Deaths': 'Muertes', 'Pins': 'Marcadores', 'portals': 'portales', 'explored': 'explorado', 'online': 'conectados',
    'Log': 'Registro', 'chat': 'chat', 'all': 'todo', 'Legend': 'Leyenda', 'click a row to hide or show it': 'pulsa una fila para ocultarla o mostrarla',
    'forest → cleared': 'bosque → talado', 'builds': 'construcciones', 'floor plan when close': 'planta al acercarse', 'boats & carts': 'barcos y carros',
    'graves': 'tumbas', 'traders': 'comerciantes', 'pins': 'marcadores', 'Layers': 'Capas', 'View': 'Vista', 'Ground': 'Suelo', 'Details': 'Detalles',
    'Names': 'Nombres', 'Torches & fires': 'Antorchas y fuegos', 'Trails': 'Rastros', 'Light': 'Luz', 'Server clock': 'Hora del server',
    'Morning': 'Mañana', 'Noon': 'Mediodía', 'Evening': 'Tarde', 'Night': 'Noche', 'Shadows': 'Sombras', 'Buildings': 'Construcciones',
    'Trees': 'Árboles', 'Bushes': 'Arbustos', 'Rocks': 'Rocas', 'Everything else': 'Todo lo demás', 'Travel': 'Viajes', 'Builds': 'Construcciones',
    'Wilds': 'Naturaleza', 'Bare': 'Limpio', 'Continents & islands': 'Continentes e islas', 'Holms': 'Islotes', 'Water': 'Agua', 'Lakes & bays': 'Lagos y bahías',
    'Rivers': 'Ríos', 'Height': 'Altura', 'Mountain ranges': 'Cordilleras', 'Biomes': 'Biomas', 'Terrain': 'Relieve', 'Atlas': 'Atlas',
    'not drawn on the atlas': 'no se dibuja en el atlas', 'Explored': 'Explorado', 'Standing': 'En pie', 'Felled': 'Talados', 'Afloat': 'A flote', 'Named': 'Con nombre',

    // ---- títulos de botones
    'Of the world, walked by someone': 'Del mundo, pisado por alguien', 'Players on right now': 'Jugadores conectados ahora', 'Details & legend': 'Detalles y leyenda',
    'Switch ground (L)': 'Cambiar el suelo (L)', 'The map, from above': 'El mapa, desde arriba',
    'Stand in the middle of the map in 3D; or drag the figure onto the map': 'Ponte en el centro del mapa en 3D, o arrastra la figura al mapa',
    'The names of the land. Signed in, tap one on the map to rename it': 'Los nombres de los lugares. Con sesión iniciada, toca uno en el mapa para cambiarlo',
    'Which names': 'Qué nombres', 'Torches, fire pits and hearths; a grey ring has burnt out': 'Antorchas, hogueras y chimeneas; un aro gris se ha apagado',
    'Where people died, fading over a month': 'Donde murió alguien; se va borrando en un mes', 'Where people have walked': 'Por donde ha pasado la gente',
    'Drag onto the map to stand there in 3D (or tap, then tap the map)': 'Arrástrala al mapa para ponerte allí en 3D (o tócala y luego toca el mapa)',
    "Cinematic: a tour of the world's places, in 3D where the ground is walked. Any touch ends it.": 'Cinemática: un recorrido por los lugares del mundo, en 3D donde se ha pisado. Cualquier toque la para.',
    'Cinematic': 'Cinemática', 'The world in 3D': 'El mundo en 3D', 'Zoom in': 'Acercar', 'Zoom out': 'Alejar', 'Fit': 'Ajustar', 'Commands & help': 'Comandos y ayuda',
    'Fullscreen': 'Pantalla completa', "The server's own time of day": 'La hora del día en el server', 'Shadows cost a phone most of its frame rate': 'Las sombras hacen ir lento el móvil',
    'Back to the map (Esc)': 'Volver al mapa (Esc)', 'Overview: circle the spot from above': 'Vista general: girar alrededor desde arriba', 'Light and what to show': 'Luz y qué mostrar',
    'See in 3D': 'Ver en 3D', 'Escape': 'Esc',

    // ---- ayuda de comandos
    'Map commands': 'Comandos del mapa', 'Type these in the in-game chat (press': 'Escríbelos en el chat del juego (pulsa', 'Enter': 'Intro', 'to open chat).': 'para abrir el chat).',
    'drop an unnamed dot where you stand': 'pone un punto sin nombre donde estás', 'drop a dot with a label': 'pone un punto con un texto',
    'a typed pin, e.g.': 'un marcador de un tipo, por ejemplo', 'remove your most recent pin': 'quita tu último marcador', 'remove the pin matching that text': 'quita el marcador con ese texto',
    'with no text, removes your most recent': 'sin texto, quita el último', 'unnamed': 'sin nombre', 'pin': 'marcador', 'Types': 'Tipos', 'dot': 'punto', 'fire': 'fuego',
    'mine': 'mina', 'house': 'casa', 'cave': 'cueva', 'Or on this map, signed in: tap walked ground and': 'O en este mapa, con sesión iniciada: toca suelo pisado y',
    'Pin here': 'Marcar aquí', ', or tap a pin to relabel, retype, move or delete it.': ', o toca un marcador para cambiarle el texto o el tipo, moverlo o borrarlo.',
    "Each row of the sidebar's": 'Cada fila de la', 'is a switch, remembered by this browser; the': 'de la barra lateral es un interruptor que este navegador recuerda; la tarjeta',
    'card on the map holds presets of them, details (torches and fires, deaths, trails), and swaps the ground between the terrain render and a flat atlas of biomes (':
      'del mapa guarda combinaciones, los detalles (antorchas y fuegos, muertes, rastros) y cambia el suelo entre el relieve y un atlas plano de biomas (',
    'cycles). Zoomed right in, builds turn from roofs into a floor plan; hide': 'los va cambiando). Muy de cerca, las construcciones pasan de tejados a planta; oculta',
    'to keep the roofs. Portal lines join linked pairs, and a grave is gear still lying there.': 'para ver los tejados. Las líneas de portales unen las parejas enlazadas, y una tumba es equipo que sigue allí tirado.',
    'Drag the little figure from the buttons onto walked ground (on a phone: tap it, then tap the map), or pick': 'Arrastra la figurita de los botones a suelo pisado (en el móvil: tócala y luego toca el mapa), o elige',
    'under View in the': 'en Vista de la tarjeta', 'card, or': ', o', 'on a place or a pin, and you stand there at eye height. Drag to look around, scroll or pinch to zoom the eye,':
      'en un lugar o un marcador, y te pones allí a la altura de los ojos. Arrastra para mirar, rueda o pellizca para acercar,',
    'or the arrows to walk (': 'o las flechas para andar (', 'Shift': 'Mayús', 'runs), click the ground to walk there; ⇅ circles the spot from above, where the same keys fly you over the land (':
      'corre), pulsa en el suelo para ir allí; ⇅ gira alrededor desde arriba, y con las mismas teclas vuelas sobre el terreno (',
    'or': 'o', 'Space': 'Espacio', 'rise,': 'sube,', 'sink).': 'baja).', 'Esc': 'Esc',
    "brings you back to the map where you stood. Buildings, trees and rocks are the game's own models, mined rocks as far as they have been mined.":
      'te devuelve al mapa donde estabas. Las construcciones, árboles y rocas son los modelos del propio juego; las rocas minadas, tal como han quedado.',
    'Show yourself on the map': 'Salir en el mapa', 'Press': 'Pulsa', 'in game and turn on': 'en el juego y activa', 'visible to other players': 'visible para otros jugadores',
    '. Without it you still show in the online list, but never with a location.': '. Sin eso sales en la lista de conectados, pero nunca en el mapa.',
    'Good to know': 'Conviene saber', 'Players and pins refresh every few seconds; builds and the explored mask every minute while someone has the map open.':
      'Los jugadores y los marcadores se actualizan cada pocos segundos; las construcciones y lo explorado, cada minuto mientras alguien tenga el mapa abierto.',

    // ---- marcadores
    'New pin': 'Marcador nuevo', 'Edit pin': 'Editar marcador', 'Moving pin': 'Moviendo marcador', 'Tap the map where it goes.': 'Toca el mapa donde va.',
    'the server takes pins from the site after its next restart': 'el server acepta marcadores desde la web tras su próximo reinicio',

    // ---- biomas, lugares y cosas del juego
    'Meadows': 'Prados', 'Black Forest': 'Selva Negra', 'Black forest': 'Selva Negra', 'Swamp': 'Pantano', 'Mountain': 'Montaña', 'Mountains': 'Montañas', 'Plains': 'Llanuras',
    'Mistlands': 'Tierras Brumosas', 'Ashlands': 'Tierras de Ceniza', 'Deep North': 'Norte Profundo', 'Deep north': 'Norte Profundo', 'Ocean': 'Océano',
    'Continent': 'Continente', 'Island': 'Isla', 'Holm': 'Islote', 'Mountain range': 'Cordillera', 'Lake': 'Lago', 'Bay': 'Bahía', 'River': 'Río',
    'Raft': 'Balsa', 'Karve': 'Karve', 'Longship': 'Barco largo', 'Cart': 'Carro', 'Boat': 'Barco',
    'Eikthyr': 'Eikthyr', 'The Elder': 'El Anciano', 'Bonemass': 'Masa Ósea', 'Moder': 'Moder', 'Yagluth': 'Yagluth', 'The Queen': 'La Reina', 'Fader': 'Fader',
    'Haldor': 'Haldor', 'Hildir': 'Hildir', 'Bog Witch': 'Bruja del Pantano', 'his wagon camp': 'su campamento con carro', 'her tent camp': 'su campamento con tienda',
    'her hut on stilts': 'su cabaña sobre pilotes', 'keeps shop': 'tiene tienda', 'a camp': 'un campamento',

    // ---- jugadores
    'Sort': 'Ordenar', 'last seen': 'visto por última vez', 'deaths': 'muertes', 'safest': 'más seguro', 'distance': 'distancia', 'best streak': 'mejor racha',
    'close calls': 'por los pelos', 'pieces': 'piezas', 'everyone': 'todos', 'online now': 'conectados ahora', 'seen this week': 'vistos esta semana',
    'gear still out': 'equipo aún tirado', 'How these are counted': 'Cómo se cuenta', 'not seen since the tally began': 'no se le ha visto desde que empezó la cuenta',
    'nothing to go on yet': 'aún no hay datos', 'as last seen': 'como se le vio por última vez', 'right hand': 'mano derecha', 'left hand': 'mano izquierda', 'on the back': 'a la espalda',
    'Survival': 'Supervivencia', 'Journeys': 'Viajes', 'km at sea': 'km por mar', 'Deeds': 'Hazañas', 'trees felled': 'árboles talados', 'rocks broken': 'rocas rotas',
    'Building': 'Construcción', 'Presence': 'Presencia', 'chat lines': 'líneas de chat', 'at sea': 'en el mar', 'most on the roster': 'el que más', 'fewest on the roster': 'el que menos',
    'farthest on the roster': 'el que más lejos', 'least on the roster': 'el que menos', 'safest on the roster': 'el más prudente', 'deadliest on the roster': 'el que más muere',
    'Closest call': 'El más apurado', 'and lived': 'y vivió', 'Best streak': 'Mejor racha', 'since the last': 'desde la última', 'Deadliest ground': 'Donde más muere',
    'of all deaths': 'de todas las muertes', 'Safest ground': 'Donde menos muere', 'of all km': 'de todos los km', 'of everything standing': 'de todo lo construido',
    'Gates': 'Portales', "of the world's gates": 'de los portales del mundo', 'Fleet': 'Flota', "the roster's biggest fleet": 'la flota más grande', 'of every hull': 'de todos los barcos',
    'Cooked': 'Cocinado', 'nothing burnt yet': 'nada quemado todavía', "of the roster's cooking": 'de todo lo cocinado', 'Top dish': 'Plato estrella', 'Brewed': 'Hidromiel',
    'Smelted': 'Fundido', "of the map's pins": 'de los marcadores del mapa', "the roster's loudest": 'el que más habla', "the roster's quietest": 'el más callado',
    'of everything said': 'de todo lo dicho', 'every grave reclaimed': 'todas las tumbas recuperadas',
    'stats unavailable — these tallies may be out of date': 'estadísticas no disponibles; estos datos pueden estar desfasados', 'stats unavailable': 'estadísticas no disponibles',
    'Survivor': 'Superviviente', 'Builder': 'Constructor', 'Slayer': 'Cazador', 'Harvester': 'Recolector', 'Explorer': 'Explorador',
    'Joins': 'Entradas', 'Chat': 'Chat', 'km': 'km', 'Hops': 'Saltos', 'Pieces': 'Piezas', 'and': 'y', 'ships': 'barcos', 'Graves': 'Tumbas', 'Deaths per km': 'Muertes por km',
    'Close calls': 'Por los pelos', 'Streak': 'Racha', 'Corpse runs': 'Recuperar el cuerpo', 'recovered': 'recuperado', 'failed': 'fallido', 'rescued': 'rescatado', 'out there': 'pendiente',
    'Bosses': 'Jefes', 'Kills': 'Muertes causadas', 'trees': 'árboles', 'rocks': 'rocas', 'Kitchen': 'Cocina', 'dishes cooked': 'platos cocinados', 'burnt': 'quemados',
    'meads brewed': 'hidromieles', 'honey taken': 'miel recogida', 'smelted': 'fundido', 'Class': 'Clase', 'The pentagon': 'El pentágono',
    '— every time you have died in this world.': '— cada vez que has muerto en este mundo.', '— sessions started, one per connection to the server.': '— partidas empezadas, una por cada conexión al server.',
    '— lines you have said in chat.': '— líneas que has dicho en el chat.', '— metres covered on foot and by boat, with teleports excluded.': '— metros recorridos a pie y en barco, sin contar los portales.',
    '— portal trips and respawns: jumps over 100 m between one-second samples.': '— viajes por portal y reapariciones: saltos de más de 100 m de un segundo a otro.',
    '— map pins you have placed.': '— marcadores que has puesto en el mapa.', '— what is standing in the world right now with your name on it.': '— lo que hay construido ahora mismo en el mundo a tu nombre.',
    '— your tombstones still out there holding gear.': '— tus tumbas que siguen ahí con equipo.',
    '— deaths divided by km walked, overall and by the biome you were in: the biome is read off the ground under you every second.': '— muertes divididas entre km recorridos, en total y por bioma (el bioma se mira cada segundo bajo tus pies).',
    '— health under a tenth and back over three tenths without dying; the lowest shows the closest anyone came.': '— bajar de un 10 % de vida y volver a más del 30 % sin morir; el mínimo es lo más cerca que ha estado alguien.',
    '— the longest walk between two deaths, and how far you have come since the last one.': '— el camino más largo entre dos muertes, y lo que llevas desde la última.',
    'means you got back to where you died;': 'es que volviste a donde moriste;', 'means you died again within 200 m of it first;': 'es que volviste a morir a menos de 200 m antes de llegar;',
    'means the grave was emptied by someone else before you got there;': 'es que otro vació la tumba antes de que llegaras;', 'is a death you have not returned to.': 'es una muerte a la que no has vuelto.',
    "— the world's own record of which have fallen, in the order they did.": '— los que han caído según el propio mundo, en el orden en que cayeron.',
    '— what came out of the stations:': '— lo que ha salido de las estaciones:', 'on a fire or in the oven, for whoever put them on (': 'en un fuego o en el horno, para quien los puso (',
    'instead if left there too long);': 'si se dejaron demasiado);', ', counted at the tap for whoever filled the fermenter;': ', contados al sacarlos, para quien llenó el fermentador;',
    'from a hive; and': 'de una colmena; y',
    'Anything built before the tally started counts for nobody, so a builder who has not logged in since shows zero.': 'Lo construido antes de empezar la cuenta no cuenta para nadie, así que quien no ha entrado desde entonces sale con cero.',
    'Time played is not tracked, and no figure here is a stand-in for it. Distances are the yardstick.': 'No se cuenta el tiempo jugado y ninguna cifra lo sustituye: la medida son las distancias.',

    // ---- clase (tarjeta de jugador)
    'a bow': 'un arco', 'a crossbow': 'una ballesta', 'a staff': 'un báculo', 'a shield': 'un escudo', 'a knife': 'un cuchillo', 'a sword': 'una espada', 'an axe': 'un hacha',
    'a mace': 'una maza', 'a spear': 'una lanza', 'an atgeir': 'un atgeir', 'the hammer': 'el martillo', 'the hoe': 'la azada', 'the cultivator': 'el cultivador', 'a pickaxe': 'un pico',
    'a torch': 'una antorcha', 'a fishing rod': 'una caña de pescar', 'a tool': 'una herramienta', 'heavy armour': 'armadura pesada', 'medium armour': 'armadura media',
    'light armour': 'armadura ligera', 'no armour': 'sin armadura', 'hearty food': 'comida contundente', 'light, quick food': 'comida ligera', 'eitr food': 'comida de eitr',
    'a balanced table': 'una dieta equilibrada', 'Mage': 'Mago', 'Battlemage': 'Mago de batalla', 'Spellsword': 'Espada arcana', 'Spellguard': 'Guardia arcano', 'Arcane Archer': 'Arquero arcano',
    'Nightblade': 'Hoja nocturna', 'Knight': 'Caballero', 'Templar': 'Templario', 'Champion': 'Campeón', 'Warrior': 'Guerrero', 'Paladin': 'Paladín', 'Marksman': 'Tirador',
    'Executioner': 'Verdugo', 'Soldier': 'Soldado', 'Warlock': 'Brujo', 'Reaver': 'Saqueador', 'Fighter': 'Luchador', 'Warden': 'Guardián', 'Ranger': 'Montaraz', 'Rogue': 'Pícaro',
    'Enchanter': 'Encantador', 'Berserker': 'Berserker', 'Duelist': 'Duelista', 'Skirmisher': 'Hostigador', 'Archer': 'Arquero', 'Assassin': 'Asesino', 'Brute': 'Bruto',
    'Shaman': 'Chamán', 'Bruiser': 'Matón', 'Guardian': 'Protector', 'Hunter': 'Cazador', 'Cutthroat': 'Degollador', 'Sorcerer': 'Hechicero', 'Brawler': 'Camorrista',
    'Sentinel': 'Centinela', 'Cutpurse': 'Ratero', 'Arbalist': 'Ballestero', 'Vanguard': 'Vanguardia', 'Huscarl': 'Huscarle', 'Hoplite': 'Hoplita', 'Raider': 'Asaltante',
    'Cook': 'Cocinero', 'Baker': 'Panadero', 'Brewer': 'Cervecero', 'Smith': 'Herrero', 'Architect': 'Arquitecto', 'Carpenter': 'Carpintero', 'Engineer': 'Ingeniero',
    'Homesteader': 'Granjero', 'Miner': 'Minero', 'Lumberjack': 'Leñador', 'Farmer': 'Agricultor', 'Seafarer': 'Navegante', 'Angler': 'Pescador', 'Wayfarer': 'Caminante',

    // ---- portales
    'Portals': 'Portales', 'a standing link ·': 'un enlace activo ·', 'one retag away · drag to pan, scroll or pinch to zoom': 'a un cambio de etiqueta · arrastra para mover, rueda o pellizca para acercar',
    '· a hub opens close up, a gate takes you through to where it leads; ⌖ opens the map there': '· un nudo se abre de cerca, un portal te lleva a donde conecta; ⌖ abre el mapa allí',
    'Fit every portal': 'Ver todos los portales', 'Open the map here': 'Abrir el mapa aquí', 'Stand here in 3D': 'Ponerse aquí en 3D', 'Back to the network (Esc)': 'Volver a la red (Esc)',
    'the world centre': 'el centro del mundo', 'unnamed site': 'lugar sin nombre', 'one retag away': 'a un cambio de etiqueta', 'dialled now': 'conectado ahora',
    'could not save the name': 'no se pudo guardar el nombre', 'server unreachable — this atlas may be out of date': 'sin conexión con el server; este atlas puede estar desfasado',

    // ---- mundo
    'Records': 'Récords', 'among the places anyone has seen': 'entre los lugares que alguien ha visto', 'Danger by biome': 'Peligro por bioma', 'everyone together': 'todos juntos',
    'Lands': 'Tierras', 'the continents and islands someone has set foot on, with what stands on them': 'los continentes e islas que alguien ha pisado, con lo que hay construido',
    'Land': 'Tierra', 'km²': 'km²', 'walked': 'pisado', 'gates': 'portales', 'peak': 'cumbre', 'Largest continent': 'Continente más grande', 'Largest island': 'Isla más grande',
    'Most explored land': 'Tierra más explorada', 'Least explored land': 'Tierra menos explorada', 'Tallest peak': 'Cumbre más alta', 'Longest river': 'Río más largo',
    'Biggest lake': 'Lago más grande', 'Widest bay': 'Bahía más ancha', 'Most built place': 'Lugar con más construcciones', 'Busiest kitchen': 'Cocina con más trabajo',
    'Deadliest place': 'Lugar más mortal', 'Most gated place': 'Lugar con más portales', 'Farthest build from the centre': 'Construcción más lejos del centro',
    'Northernmost walked ground': 'Lo más al norte pisado', 'Southernmost walked ground': 'Lo más al sur pisado', 'open sea': 'mar abierto', 'Oldest grave': 'Tumba más antigua',
    'Latest name given': 'Último nombre puesto', 'Meals': 'Comidas', 'server unreachable — this page may be out of date': 'sin conexión con el server; esta página puede estar desfasada',
    'the server is not answering right now': 'el server no responde ahora mismo',
    'walked by someone since the fog first lifted': 'pisado por alguien desde que se levantó la niebla', 'walked, the rest still under fog': 'pisado; el resto sigue bajo la niebla',
    'built by everyone': 'construido entre todos', 'stumps standing where trees were': 'tocones donde había árboles', 'linked to another': 'enlazado con otro',
    'every one still standing': 'todos siguen en pie', 'of the bosses': 'de los jefes', 'since the count began': 'desde que empezó la cuenta',
    'gear still lying where someone fell': 'equipo que sigue donde alguien cayó', 'of the places carry a given name': 'de los lugares tienen nombre',
    'Farthest': 'Más lejos', 'Most deaths': 'Más muertes', 'Loudest': 'Más hablador', 'Latest death': 'Última muerte',

    // ---- plano
    'Copy link': 'Copiar enlace', 'Download PNG': 'Descargar PNG', 'The whole plan travels inside the link — no account, no server.': 'Todo el plano va dentro del enlace: sin cuenta y sin server.',
    'Reference layers': 'Capas de referencia', 'New plan': 'Plano nuevo', 'live map': 'mapa en vivo', 'Shared plan': 'Plano compartido',
    'opened read-only, so your own plan is untouched.': 'abierto solo para ver, así que tu plano no se toca.', 'Make a copy': 'Hacer una copia', 'Keep mine': 'Quedarme con el mío',
    'Share link': 'Compartir enlace', 'Valheim world map plan': 'Plano del mundo de Valheim', 'label text…': 'texto…', 'Untitled plan': 'Plano sin título',
    'Notes — meet at the karve, bring a cart…': 'Notas: quedamos en el karve, traed un carro…', 'Undo (Ctrl+Z)': 'Deshacer (Ctrl+Z)', 'Remove every drawing': 'Borrar todos los dibujos',
    'Fit the explored world': 'Ver todo lo explorado', 'Distance': 'Distancia', 'before nightfall?': '¿antes de que anochezca?', 'Pan': 'Mover', 'Pen': 'Lápiz', 'Line': 'Línea',
    'Arrow': 'Flecha', 'Box': 'Caja', 'Circle': 'Círculo', 'Label': 'Texto', 'Stamp': 'Sello', 'Erase': 'Borrar', 'Ore': 'Mineral', 'Camp': 'Campamento', 'Base': 'Base',
    'Portal': 'Portal', 'Danger': 'Peligro', 'Cave': 'Cueva', 'Clear the title, notes and every drawing?': '¿Borrar el título, las notas y todos los dibujos?',
    'Pick a tool and draw on the map.': 'Elige una herramienta y dibuja en el mapa.', 'place names': 'nombres de lugares', 'players online': 'jugadores conectados',
    'not a plan': 'no es un plano', 'Select & copy': 'Seleccionar y copiar', 'Replace your own saved plan with this one?': '¿Cambiar tu plano guardado por este?',

    // ---- ajustes
    "The map mod's settings, kept by the server. Changes apply at once unless marked, and every change is posted to the Discord.": 'Los ajustes del mod del mapa, guardados en el server. Los cambios se aplican al momento salvo los marcados, y cada cambio se avisa en el Discord.',
    'A restart is due.': 'Hace falta reiniciar.', 'A setting marked': 'Un ajuste marcado con', 'restart': 'reiniciar', 'has changed; it takes effect when the server next restarts.': 'ha cambiado; se aplica cuando el server se reinicie.',
    'No sessions refused yet.': 'No se ha rechazado ninguna sesión.', 'Sign everyone out': 'Cerrar la sesión de todos', 'The map': 'El mapa', 'Chat & Discord': 'Chat y Discord',
    'The 3D view': 'La vista 3D', 'The mod': 'El mod', 'Sign in with Discord to see the settings.': 'Entra con Discord para ver los ajustes.',
    "The settings are for the server's admins.": 'Los ajustes son para los administradores del server.',
    'This server does not take settings from the site yet (it needs the mod build that does).': 'Este server todavía no acepta ajustes desde la web (hace falta una versión del mod que lo haga).',
    'Everyone is signed out, you too. Sign in again to come back here.': 'Se ha cerrado la sesión de todos, también la tuya. Entra otra vez para volver aquí.', 'could not reset': 'no se pudo reiniciar',
    "the config's own value": 'el valor del archivo de ajustes',

    // ---- vista 3D
    'Loading the 3D view…': 'Cargando la vista 3D…', 'The server’s map mod has no 3D view yet: it comes with the mod’s next restart.': 'El mod del mapa del server aún no tiene vista 3D: llega con su próximo reinicio.',
    'Nobody has walked here yet, so there is nothing to show.': 'Nadie ha pisado esto todavía, así que no hay nada que enseñar.', 'Nobody has walked here yet': 'Nadie ha pisado esto todavía',
    'Drag to pan · two fingers to turn and zoom': 'Arrastra para mover · dos dedos para girar y acercar',
    'Drag to pan, right-drag to turn · W A S D to fly over · E and Q up and down · Shift faster': 'Arrastra para mover, botón derecho para girar · W A S D para volar · E y Q arriba y abajo · Mayús más rápido',
    'Drag to look around · tap the ground to walk there': 'Arrastra para mirar · toca el suelo para ir allí',
    'Drag to look · W A S D to walk · click the ground to go there · Esc for the map': 'Arrastra para mirar · W A S D para andar · pulsa el suelo para ir allí · Esc para el mapa',
    'Street: stand on the spot': 'A pie de calle: ponerse en el sitio', 'walked chunks': 'zonas pisadas',

    // ---- recorrido (cinemática)
    'the wild': 'la naturaleza', "A boss's altar": 'Un altar de jefe', 'boss altars': 'altares de jefe', 'A Vegvisir': 'Un Vegvisir', 'Vegvisirs': 'Vegvisirs',
    'A burial chamber': 'Una cámara funeraria', 'burial chambers': 'cámaras funerarias', 'A sunken crypt': 'Una cripta hundida', 'sunken crypts': 'criptas hundidas',
    'A troll cave': 'Una cueva de trol', 'troll caves': 'cuevas de trol', 'A frost cave': 'Una cueva helada', 'frost caves': 'cuevas heladas', 'A fuling village': 'Una aldea de fulings',
    'fuling villages': 'aldeas de fulings', 'An infested mine': 'Una mina infestada', 'infested mines': 'minas infestadas', 'A dvergr outpost': 'Un puesto dvergr',
    'dvergr outposts': 'puestos dvergr', 'A dvergr dig': 'Una excavación dvergr', 'dvergr digs': 'excavaciones dvergr', 'An eitr refinery': 'Una refinería de eitr',
    'eitr refineries': 'refinerías de eitr', 'A tar pit': 'Un pozo de alquitrán', 'tar pits': 'pozos de alquitrán', 'An abandoned village': 'Una aldea abandonada',
    'abandoned villages': 'aldeas abandonadas', 'Ruins': 'Ruinas', 'A stone circle': 'Un círculo de piedras', 'stone circles': 'círculos de piedras',
    "Where Hildir's chest lies": 'Donde está el cofre de Hildir', "of Hildir's hideouts": 'de los escondites de Hildir', 'A wonder of the mist': 'Una maravilla de la niebla',
    'wonders of the mist': 'maravillas de la niebla', 'of its kind': 'de su tipo', 'Online now': 'Conectados ahora', 'Portal hub': 'Nudo de portales', 'The loneliest gate': 'El portal más solitario',
    'An unnamed gate': 'Un portal sin nombre', 'to the next gate': 'hasta el siguiente portal', 'waiting for a twin': 'esperando pareja', 'A great build': 'Una gran construcción',
    'The biggest build': 'La construcción más grande', 'of all standing': 'de todo lo construido', "A builder's own": 'Lo de un constructor', 'pieces here': 'piezas aquí',
    'standing in all': 'construidas en total', 'Where people die': 'Donde muere la gente', 'A bad spot': 'Un mal sitio', 'The latest death': 'La última muerte',
    'The oldest grave': 'La tumba más antigua', 'of theirs out there': 'suyas por ahí', 'graves in all': 'tumbas en total', 'boat afloat': 'barco a flote', 'boats afloat': 'barcos a flote',
    'left standing where the last load came off': 'parado donde se descargó por última vez', 'The busiest kitchen': 'La cocina con más trabajo', 'meals cooked': 'comidas cocinadas',
    'of every meal cooked': 'de todas las comidas', 'An island': 'Una isla', 'of it walked': 'pisado', 'The tallest peak climbed': 'La cumbre más alta escalada', 'above the sea': 'sobre el mar',
    'Along the ridge': 'Por la cresta', 'end to end': 'de punta a punta', 'at the top': 'en la cima', 'The biggest lake known': 'El lago más grande conocido', 'of its shores walked': 'de su orilla pisada',
    'The longest river known': 'El río más largo conocido', 'source to sea': 'del nacimiento al mar', 'pinned it': 'lo marcó', 'A named place': 'Un lugar con nombre', 'named it': 'le puso nombre',
    'Along the shore': 'Por la costa', 'its coast from the water': 'su costa desde el agua', 'km² of water': 'km² de agua', 'From the dock': 'Desde el muelle', 'From the shore': 'Desde la orilla',
    "The water's edge": 'La orilla', 'The quiet wild': 'La naturaleza tranquila', 'Nowhere in particular': 'Ningún sitio en concreto', 'walked once, and left alone': 'pisado una vez y abandonado',
    'and more to the nearest build': 'y más hasta la construcción más cercana', 'Where people go': 'Por donde va la gente', 'Every path walked': 'Todos los caminos recorridos',
    'the trails over the whole walked world': 'los rastros por todo el mundo pisado', 'walked, all told': 'recorridos en total', 'Where it all began': 'Donde empezó todo',
    'The sacrificial stones': 'Las piedras del sacrificio', 'the centre of the world': 'el centro del mundo', 'of the world walked since': 'del mundo pisado desde entonces', 'Stop the tour': 'Parar el recorrido'
  };
  // Textos que el mapa compone con números o nombres.
  var DIR = { 'north': 'norte', 'south': 'sur', 'east': 'este', 'west': 'oeste', 'north-east': 'noreste', 'north-west': 'noroeste', 'south-east': 'sureste', 'south-west': 'suroeste' };
  var R = [
    [/^updated (.+)$/, 'actualizado $1'], [/^read (\d.*)$/, 'leído $1'], [/^(\d[\d.,]*) pairs? linked$/, '$1 parejas enlazadas'], [/^(\d[\d.,]*) waiting for a twin$/, '$1 esperando pareja'], [/^build (.+)$/, 'versión $1'],
    [/^(\d[\d.,]*) players?$/, function (m, n) { return n + (n === '1' ? ' jugador' : ' jugadores'); }], [/^tallied since (.+)$/, 'contado desde $1'],
    [/^(\d[\d.,]*) pairs?$/, function (m, n) { return n + (n === '1' ? ' pareja' : ' parejas'); }], [/^(\d[\d.,]*) portals?$/, function (m, n) { return n + (n === '1' ? ' portal' : ' portales'); }],
    [/^(\d[\d.,]*) hubs?$/, function (m, n) { return n + (n === '1' ? ' nudo' : ' nudos'); }], [/^(\d[\d.,]*) loose$/, '$1 sueltos'], [/^(\d[\d.,]*) gates?$/, function (m, n) { return n + (n === '1' ? ' portal' : ' portales'); }],
    [/^(\d[\d.,]*) meals cooked, (\d[\d.,]*) burnt$/, '$1 comidas cocinadas, $2 quemadas'], [/^(\d[\d.,]*) meads brewed$/, '$1 hidromieles'], [/^(\d[\d.,]*) honey taken$/, '$1 de miel recogida'],
    [/^(\d[\d.,]*) bars smelted$/, '$1 lingotes fundidos'], [/^([\d.,]+)% walked$/, '$1 % pisado'], [/^([\d.,]+)% walked, and someone has set foot there$/, '$1 % pisado, y alguien ha estado allí'],
    [/^([\d.,]+) m above the sea$/, '$1 m sobre el mar'], [/^([\d.,]+) pieces standing$/, '$1 piezas en pie'], [/^([\d.,]+) km out$/, 'a $1 km'],
    [/^(north|south|east|west|north-east|north-west|south-east|south-west), (.+)$/, function (m, d, r) { return DIR[d] + ', ' + tr(r); }],
    [/^(north|south|east|west|north-east|north-west|south-east|south-west)$/, function (m, d) { return DIR[d]; }],
    [/^([\d.,]+) m (north|south|east|west) of the centre$/, function (m, n, d) { return n + ' m al ' + DIR[d] + ' del centro'; }],
    [/^(.+) ago and still holding gear$/, function (m, a) { return tr(a + ' ago') + ' y aún con equipo'; }],
    [/^(.+): fell (\d+)(st|nd|rd|th)$/, function (m, a, n) { return tr(a) + ': cayó en ' + n + 'º lugar'; }], [/^(.+): still standing$/, function (m, a) { return tr(a) + ': sigue en pie'; }],
    [/^Through to (.+)$/, 'Hacia $1'], [/^colour (\d+)$/, 'color $1'], [/^(\d[\d.,]*) portals, (\d[\d.,]*) pins\.$/, '$1 portales, $2 marcadores.'],
    [/^player _(.+)_ joined$/, '$1 ha entrado'], [/^player _(.+)_ left$/, '$1 ha salido'], [/^player _(.+)_ died$/, '$1 ha muerto'],
    [/^(\d[\d.,]*) online$/, '$1 conectados'],
    [/^last seen (.+)$/, function (m, a) { return 'visto ' + tr(a); }],
    [/^(\d+) min ago$/, 'hace $1 min'], [/^(\d+) ?h ago$/, 'hace $1 h'], [/^(\d+) ?d ago$/, 'hace $1 d'], [/^(\d+)m ago$/, 'hace $1 min'],
    [/^([\d.,]+) km long$/, '$1 km de largo'], [/^([\d.,]+) m long$/, '$1 m de largo'],
    [/^([\d.,]+) pieces? built$/, function (m, n) { return n + (n === '1' ? ' pieza construida' : ' piezas construidas'); }],
    [/^([\d.,]+) trees felled$/, '$1 árboles talados'], [/^([\d.,]+) rocks broken$/, '$1 rocas rotas'], [/^([\d.,]+) kills$/, '$1 muertes causadas'],
    [/^([\d.,]+) portal hops$/, '$1 saltos por portal'], [/^Pin · (.+)$/, function (m, a) { return 'Marcador · ' + tr(a); }],
    [/^fed for (.+)$/, function (m, a) { return 'comida: ' + a; }], [/^(.+) of the time in hand$/, '$1 del tiempo con algo en la mano'],
    [/^Sessions from before (.+) are refused\.$/, 'Se rechazan las sesiones anteriores a $1.'],
    [/^The 3D view did not load: (.*)$/, 'La vista 3D no ha cargado: $1']
  ];

  function tr(s) {
    var t = s.replace(/\s+/g, ' ').trim();
    if (t === '\\u2014') return lead(s) + '—' + trail(s);   // el mod escribe "\u2014" tal cual mientras no hay datos
    if (!t || !/[A-Za-z]/.test(t)) return s;
    if (T.hasOwnProperty(t)) return lead(s) + T[t] + trail(s);
    if (t.length > 2 && t.slice(-2) === ' ·') { var r1 = tr(t.slice(0, -2)); return r1 === t.slice(0, -2) ? s : lead(s) + r1 + ' ·' + trail(s); }
    if (t.indexOf('· ') === 0) { var r0 = tr(t.slice(2)); return r0 === t.slice(2) ? s : lead(s) + '· ' + r0 + trail(s); }
    var sep = t.lastIndexOf(' · ');   // títulos "Majinheim · Players"
    if (sep > 0 && !T.hasOwnProperty(t)) { var a = tr(t.slice(0, sep)), b = tr(t.slice(sep + 3)); if (a + ' · ' + b !== t) return lead(s) + a + ' · ' + b + trail(s); }
    var o = Object.prototype.hasOwnProperty.call(T, t) ? T[t] : null;
    if (o === null) for (var i = 0; i < R.length; i++) if (R[i][0].test(t)) { o = t.replace(R[i][0], R[i][1]); break; }
    return o === null || o === t ? s : lead(s) + o + trail(s);
  }
  function lead(s) { return /^\s/.test(s) ? ' ' : ''; }
  function trail(s) { return /\s$/.test(s) ? ' ' : ''; }

  var ATTRS = ['title', 'placeholder', 'aria-label', 'alt'];
  var SKIP = { SCRIPT: 1, STYLE: 1, TEXTAREA: 1, CODE: 1, KBD: 1, PRE: 1 };
  function el(e) {
    for (var i = 0; i < ATTRS.length; i++) {
      var v = e.getAttribute && e.getAttribute(ATTRS[i]);
      if (v) { var n = tr(v); if (n !== v) e.setAttribute(ATTRS[i], n); }
    }
  }
  function walk(root) {
    if (!root) return;
    if (root.nodeType === 3) { if (!(root.parentNode && SKIP[root.parentNode.nodeName])) { var n = tr(root.nodeValue); if (n !== root.nodeValue) root.nodeValue = n; } return; }
    if (root.nodeType !== 1 && root.nodeType !== 9 && root.nodeType !== 11) return;
    if (root.nodeType === 1) el(root);
    if (SKIP[root.nodeName] || root.isContentEditable) return;
    for (var c = root.firstChild; c; c = c.nextSibling) walk(c);
  }

  // Lo que el mapa dibuja en el lienzo (nombres de jefes, barcos, biomas...).
  ['fillText', 'strokeText'].forEach(function (f) {
    var p = window.CanvasRenderingContext2D && CanvasRenderingContext2D.prototype, orig = p && p[f];
    if (!orig) return;
    p[f] = function (text) { var a = Array.prototype.slice.call(arguments); if (typeof text === 'string') a[0] = tr(text); return orig.apply(this, a); };
  });

  var busy = false;
  var mo = new MutationObserver(function (list) {
    if (busy) return;
    busy = true;
    try {
      for (var i = 0; i < list.length; i++) {
        var m = list[i];
        if (m.type === 'characterData') walk(m.target);
        else if (m.type === 'attributes') el(m.target);
        else for (var j = 0; j < m.addedNodes.length; j++) walk(m.addedNodes[j]);
      }
    } finally { busy = false; }
  });
  mo.observe(document.documentElement, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ATTRS });
  document.documentElement.lang = 'es';
  function all() { walk(document.documentElement); if (document.title) document.title = tr(document.title); }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', all); else all();
  window.MajinheimES = { tr: tr };
})();
