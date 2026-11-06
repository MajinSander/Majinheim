// Majinheim Web: entrar y administrar. Todo con textContent; cada POST lleva la cabecera X-Majinheim.
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var timer = null;
  var textos = { 'en marcha': 'En marcha', 'arrancando': 'Arrancando…', 'apagado': 'Apagado' };
  var clases = { 'en marcha': 'verde', 'arrancando': 'ambar', 'apagado': 'rojo' };

  function api(path, body) {
    var o = { method: body ? 'POST' : 'GET', cache: 'no-store', credentials: 'same-origin', headers: {} };
    if (body) { o.headers['Content-Type'] = 'application/json'; o.headers['X-Majinheim'] = '1'; o.body = JSON.stringify(body); }
    return fetch(path, o).then(function (r) {
      return r.json().catch(function () { return {}; }).then(function (j) { j._code = r.status; return j; });
    });
  }

  function limpiar(n) { while (n.firstChild) n.removeChild(n.firstChild); }
  function parar() { if (timer) { clearInterval(timer); timer = null; } }
  function vista(id) { ['login', 'jugador', 'panel'].forEach(function (v) { $(v).hidden = v !== id; }); }

  function cabecera(d) {
    var dentro = d && d.dentro;
    $('salir').hidden = !dentro;
    $('quien').hidden = !dentro;
    if (dentro) $('quien').textContent = d.usuario + (d.rol === 'admin' ? ' · admin' : '');
    $('pie').textContent = !dentro ? 'Majinheim Web' : d.recordado
      ? 'Majinheim Web · sesión recordada en este dispositivo (30 días desde la última visita)'
      : 'Majinheim Web · la sesión se cierra al cerrar el navegador';
  }

  function verLogin(info) {
    parar(); vista('login'); cabecera(null);
    $('titulo').textContent = 'Majinheim'; $('sub').textContent = 'Entrar';
    document.title = 'Majinheim · Entrar';
    $('estado').className = 'pastilla gris'; $('estado').textContent = 'Sin entrar';
    if (info) $('login-info').textContent = info;
    if (!$('usuario').value) $('usuario').focus(); else $('clave').focus();
  }

  function verJugador(d) {
    parar(); vista('jugador'); cabecera(d);
    $('titulo').textContent = 'Majinheim'; $('sub').textContent = 'Tu cuenta';
    $('j-nombre').textContent = d.usuario;
    $('estado').className = 'pastilla gris'; $('estado').textContent = 'Jugador';
  }

  function verPanel(d) {
    vista('panel'); cabecera(d);
    $('titulo').textContent = 'Panel de control'; $('sub').textContent = 'Server de Valheim';
    document.title = 'Majinheim · Panel de control';
    var e = d.estado || {}, s = e.servidor || {};
    $('estado').className = 'pastilla ' + (clases[s.estado] || 'gris');
    $('estado').textContent = textos[s.estado] || '—';
    $('a-estado').textContent = textos[s.estado] || '—';
    var js = e.jugadores || [];
    $('a-jugadores').textContent = js.length ? js.map(function (j) { return j.nombre; }).join(', ') : 'nadie';
    $('a-dia').textContent = s.dia != null ? 'Día ' + s.dia : '—';
    $('a-backup').textContent = e.ultimoBackup ? new Date(e.ultimoBackup.fecha).toLocaleString('es-ES') : '—';
    document.querySelector('[data-accion="iniciar"]').disabled = s.estado !== 'apagado';
    document.querySelector('[data-accion="parar"]').disabled = s.estado === 'apagado';
    document.querySelector('[data-accion="reiniciar"]').disabled = s.estado === 'apagado';

    var ul = $('acciones'); limpiar(ul);
    (d.acciones || []).slice(0, 12).forEach(function (a) {
      var li = document.createElement('li');
      var q = document.createElement('span'); q.className = 'quien'; q.textContent = a.que;
      var c = document.createElement('span'); c.className = 'cuanto'; c.textContent = a.fecha.slice(5, 16) + ' · ' + a.ip;
      li.appendChild(q); li.appendChild(c); ul.appendChild(li);
    });
    if (!ul.firstChild) { var li = document.createElement('li'); li.className = 'tenue'; li.textContent = 'Nada todavía.'; ul.appendChild(li); }

    var ud = $('dispositivos'); limpiar(ud);
    (d.dispositivos || []).forEach(function (x) {
      var li = document.createElement('li');
      var q = document.createElement('span'); q.className = 'quien';
      q.textContent = x.usuario + ' · ' + x.dispositivo + (x.este ? ' (este)' : '');
      var c = document.createElement('span'); c.className = 'cuanto';
      c.textContent = x.desde.slice(5) + ' · ' + x.ip + (x.recordado ? ' · recordado' : '');
      li.appendChild(q); li.appendChild(c); ud.appendChild(li);
    });

    pintarConectados(js, s.estado);
    pintarExtras(d);

    var pre = $('registro');
    var abajo = pre.scrollTop + pre.clientHeight >= pre.scrollHeight - 20;
    pre.textContent = (d.registro || []).join('\n');
    if (abajo) pre.scrollTop = pre.scrollHeight;
    if (!timer) timer = setInterval(refrescar, 4000);
  }

  function pintarConectados(js, estado) {
    $('n-con').textContent = js.length;
    var ul = $('conectados');
    var clave = js.map(function (j) { return j.nombre; }).join('|') + '#' + estado;
    if (ul.getAttribute('data-clave') === clave) return;   // no repintar (y no perder el foco) si nada ha cambiado
    ul.setAttribute('data-clave', clave);
    limpiar(ul);
    js.forEach(function (j) {
      var li = document.createElement('li');
      var q = document.createElement('span'); q.className = 'quien'; q.textContent = j.nombre;
      var c = document.createElement('span'); c.className = 'botones-fila';
      [['expulsar', 'Expulsar', 'secundario'], ['banear', 'Banear', 'peligro']].forEach(function (a) {
        var b = document.createElement('button');
        b.className = 'boton chico ' + a[2]; b.textContent = a[1]; b.type = 'button';
        b.addEventListener('click', function () { accionJugador(j.nombre, a[0]); });
        c.appendChild(b);
      });
      li.appendChild(q); li.appendChild(c); ul.appendChild(li);
    });
    if (!ul.firstChild) { var li = document.createElement('li'); li.className = 'tenue'; li.textContent = estado === 'apagado' ? 'El server está apagado.' : 'No hay nadie jugando.'; ul.appendChild(li); }
  }

  function accionJugador(nombre, accion) {
    var msg = accion === 'banear'
      ? '¿Banear a ' + nombre + '? Lo saca del server y no podrá volver a entrar hasta que lo quites.'
      : '¿Expulsar a ' + nombre + ' del server? Podrá volver a entrar.';
    if (!window.confirm(msg)) return;
    $('jug-resultado').textContent = 'Un momento…';
    api('/api/admin/jugador', { nombre: nombre, accion: accion }).then(function (r) {
      if (r._code === 401) { verLogin('Tu sesión ha caducado. Entra otra vez.'); return; }
      $('jug-resultado').textContent = r.mensaje || '';
      refrescar();
    });
  }

  function consola(orden) {
    orden = (orden || '').trim();
    if (!orden) return;
    var pre = $('consola-salida');
    pre.textContent = '> ' + orden + '\n…';
    api('/api/admin/consola', { orden: orden }).then(function (r) {
      if (r._code === 401) { verLogin('Tu sesión ha caducado. Entra otra vez.'); return; }
      pre.textContent = '> ' + orden + '\n' + (r.ok ? (r.salida || '(sin respuesta)') : (r.mensaje || 'No se pudo.'));
      pre.className = 'registro consola' + (r.ok ? '' : ' fallo');
    }).catch(function () { pre.textContent = '> ' + orden + '\nSin conexión con el server.'; });
  }

  var tipos = { 'mundo-al-arrancar': 'al arrancar', 'programado': 'automático', 'web': 'desde la web', 'antes-de-restaurar': 'antes de restaurar', 'antes-de-actualizar': 'antes de actualizar' };
  var puntos = { ok: 'verde', aviso: 'ambar', mal: 'rojo', info: 'gris' };
  var orden = { mal: 0, aviso: 1, ok: 2, info: 3 };
  var niveles = { ok: 'Bien', aviso: 'Con avisos', mal: 'Falló' };
  function fechaCorta(iso) { return new Date(iso).toLocaleString('es-ES', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }); }

  // Salud del server (2.1): lo que revisa el launcher cada 10 minutos, con lo que falla arriba.
  function pintarSalud(d) {
    var sa = d.salud, ul = $('salud');
    $('t-salud').hidden = !sa && !d.tareas;   // launcher antiguo: sin tarjeta
    limpiar(ul);
    if (!sa) {
      var li0 = document.createElement('li'); li0.className = 'tenue'; li0.textContent = 'Todavía estoy haciendo la primera revisión.'; ul.appendChild(li0);
      $('salud-resumen').textContent = '';
      $('salud-resumen').className = 'resumen-salud';
      return;
    }
    var malo = (sa.items || []).some(function (i) { return i.estado === 'mal'; }), avisos = (sa.items || []).some(function (i) { return i.estado === 'aviso'; });
    $('salud-resumen').textContent = sa.resumen;
    $('salud-resumen').className = 'resumen-salud ' + (malo ? 'rojo' : avisos ? 'ambar' : 'verde');
    (sa.items || []).slice().sort(function (a, b) { return orden[a.estado] - orden[b.estado]; }).forEach(function (i) {
      var li = document.createElement('li');
      var q = document.createElement('span'); q.className = 'quien';
      var p = document.createElement('span'); p.className = 'punto ' + (puntos[i.estado] || 'gris');
      q.appendChild(p); q.appendChild(document.createTextNode(i.nombre));
      var c = document.createElement('span'); c.className = 'cuanto'; c.textContent = i.detalle;
      li.appendChild(q); li.appendChild(c); ul.appendChild(li);
    });
    $('salud-fecha').textContent = 'Revisado ' + fechaCorta(sa.fecha) + '. Se revisa sola cada 10 minutos; si algo importante se estropea, avisa en Discord.';
  }

  // Buscar en el mundo (2.2): lo mismo que contesta la IA a "¿dónde está el hierro?", para cualquier cuenta.
  function el(tag, cls, text) { var n = document.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; }
  // Mapa de la búsqueda (2.6): lo explorado, con un número en cada sitio de la lista y los portales con nombre para orientarse.
  var SVGNS = 'http://www.w3.org/2000/svg', COLORES = ['#e0a54a', '#6fbf73', '#6fa8dc'];
  function svg(tag, attrs, text) {
    var n = document.createElementNS(SVGNS, tag);
    Object.keys(attrs).forEach(function (k) { n.setAttribute(k, attrs[k]); });
    if (text != null) n.textContent = text;
    return n;
  }
  function pintarMapa(box, m, puntos) {
    var w = m.x1 - m.x0, h = m.z1 - m.z0, u = Math.max(w, h) / 100;
    var fig = el('figure', 'mapa-busqueda');
    var s = svg('svg', { viewBox: '0 0 ' + w + ' ' + h, role: 'img', 'aria-label': 'Mapa de lo explorado con los sitios encontrados' });
    s.appendChild(svg('image', { href: '/api/admin/mapa.png?v=' + m.v, x: 0, y: 0, width: w, height: h, preserveAspectRatio: 'none' }));
    function xy(x, z) { return [x - m.x0, m.z1 - z]; }
    (m.portales || []).forEach(function (p) {
      var c = xy(p.x, p.z), g = svg('g', { 'class': 'portal' });
      g.appendChild(svg('title', {}, 'Portal «' + p.nombre + '»'));
      g.appendChild(svg('rect', { x: c[0] - u * 0.7, y: c[1] - u * 0.7, width: u * 1.4, height: u * 1.4, transform: 'rotate(45 ' + c[0] + ' ' + c[1] + ')' }));
      g.appendChild(svg('text', { x: c[0] + u * 1.4, y: c[1] + u * 0.6, 'font-size': u * 2.8 }, p.nombre));
      s.appendChild(g);
    });
    puntos.forEach(function (p) {
      var c = xy(p.x, p.z), g = svg('g', { 'class': 'sitio' });
      g.appendChild(svg('title', {}, p.titulo));
      g.appendChild(svg('circle', { cx: c[0], cy: c[1], r: u * 2.6, fill: p.color }));
      g.appendChild(svg('text', { x: c[0], y: c[1] + u * 1.0, 'font-size': u * 2.8, 'text-anchor': 'middle' }, String(p.n)));
      s.appendChild(g);
    });
    fig.appendChild(s);
    fig.appendChild(el('figcaption', 'tenue', 'Solo sale lo que alguien ha explorado. Norte arriba. Los rombos morados son portales con nombre.'));
    box.appendChild(fig);
  }

  function pintarBusqueda(box, r) {
    limpiar(box);
    if (r.error) { box.appendChild(el('p', 'vacio', r.error)); return; }
    var puntos = [], num = 0;
    (r.encontrado || []).forEach(function (o, oi) {
      box.appendChild(el('h3', 'subtitulo', o.objeto + ': ' + (o.total || 0).toLocaleString('es-ES')));
      if (!o.total) { box.appendChild(el('p', 'vacio', o.nota || 'No hay en ningún cofre ni cajón.')); return; }
      if (o.reparto) box.appendChild(el('p', 'tenue', o.reparto));
      var ul = el('ul', 'lista apilada');
      (o.sitios || []).forEach(function (s) {
        var li = document.createElement('li'), quien = el('span', 'quien'), xz = /x (-?\d+), z (-?\d+)/.exec(s.coordenadas || '');
        if (xz && r.mapa) {
          num++;
          var color = COLORES[oi % COLORES.length], marca = el('span', 'marca', String(num));
          marca.style.background = color;
          quien.appendChild(marca);
          puntos.push({ n: num, x: +xz[1], z: +xz[2], color: color, titulo: num + '. ' + o.objeto + ': ' + s.cantidad.toLocaleString('es-ES') + ', ' + s.donde });
        }
        quien.appendChild(document.createTextNode(s.donde.charAt(0).toUpperCase() + s.donde.slice(1)));
        li.appendChild(quien);
        li.appendChild(el('span', 'cuanto', [s.cantidad.toLocaleString('es-ES') + ' en ' + s.en, s.carteles && s.carteles.length ? 'cartel: «' + s.carteles.join('», «') + '»' : null, s.coordenadas].filter(Boolean).join(' · ')));
        ul.appendChild(li);
      });
      box.appendChild(ul);
      if (o.otrosSitios) box.appendChild(el('p', 'tenue', 'Y en ' + o.otrosSitios + (o.otrosSitios === 1 ? ' sitio más.' : ' sitios más.')));
    });
    if (r.enTumbas) box.appendChild(el('p', 'tenue', 'En tumbas: ' + r.enTumbas.join(', ') + '.'));
    if (r.parecidos) box.appendChild(el('p', 'tenue', 'Parecidos: ' + r.parecidos.join(', ') + '.'));
    if (r.datos) box.appendChild(el('p', 'tenue', 'Son ' + r.datos + '.'));
    if (r.mapa && puntos.length) pintarMapa(box, r.mapa, puntos);
  }
  Array.prototype.forEach.call(document.querySelectorAll('[data-buscador]'), function (sec) {
    var form = sec.querySelector('form'), input = sec.querySelector('input'), box = sec.querySelector('.busqueda');
    form.addEventListener('submit', function (ev) {
      ev.preventDefault();
      var q = input.value.trim();
      if (!q) return;
      limpiar(box); box.appendChild(el('p', 'tenue', 'Buscando...'));
      api('/api/admin/buscar', { objeto: q }).then(function (r) {
        if (r._code === 401) { verLogin('Tienes que entrar otra vez.'); return; }
        pintarBusqueda(box, r._code === 200 ? r : { error: r.mensaje || r.error || 'No se ha podido buscar.' });
      }).catch(function () { pintarBusqueda(box, { error: 'Sin conexión con el server. Prueba otra vez.' }); });
    });
  });

  function pintarMantenimiento(d) {
    var m = d.mantenimiento, ul = $('mantenimientos'), b = document.querySelector('[data-accion="actualizar"]');
    b.hidden = !m;
    if (m) {
      b.disabled = !!m.enCurso;
      b.textContent = m.enCurso ? 'Mantenimiento en marcha…' : 'Actualizar ahora (con prueba)';
      $('act-nota').textContent = m.auto
        ? 'Se prueban y se instalan solos en el mantenimiento diario de las ' + m.hora + ', si no hay nadie jugando: backup antes y vuelta atrás si algo falla.'
        : 'El mantenimiento diario está desactivado (launcher del server, pestaña Salud): se actualiza a mano.';
    }
    limpiar(ul);
    ((m && m.historial) || []).forEach(function (h) {
      var li = document.createElement('li');
      var q = document.createElement('span'); q.className = 'quien';
      var p = document.createElement('span'); p.className = 'punto ' + (puntos[h.nivel] || 'gris');
      q.appendChild(p); q.appendChild(document.createTextNode(fechaCorta(h.fecha) + ' · ' + (niveles[h.nivel] || h.nivel) + (h.quien ? ' · ' + h.quien : '')));
      var c = document.createElement('span'); c.className = 'cuanto'; c.textContent = h.resumen;
      li.appendChild(q); li.appendChild(c); ul.appendChild(li);
    });
    if (!ul.firstChild) { var li1 = document.createElement('li'); li1.className = 'tenue'; li1.textContent = 'Todavía no se ha hecho ninguno.'; ul.appendChild(li1); }
  }
  // Problemas de los amigos (2.4): lo que llega del botón «Tengo un problema» del launcher de jugador.
  var problemaAbierto = null;
  function pintarProblemas(d) {
    var ps = d.problemas, ul = $('problemas');
    $('t-problemas').hidden = !ps;   // launcher antiguo: sin tarjeta
    if (!ps) return;
    $('n-problemas').textContent = ps.length;
    var clave = ps.map(function (p) { return p.id + (p.ia ? '+' : ''); }).join('|');
    if (ul.getAttribute('data-clave') === clave) return;
    ul.setAttribute('data-clave', clave);
    limpiar(ul);
    ps.forEach(function (p) {
      var li = document.createElement('li');
      var arriba = el('span', 'quien');
      arriba.appendChild(document.createTextNode((p.nombre || 'Sin nombre') + ' · ' + p.fecha.slice(8, 10) + '/' + p.fecha.slice(5, 7) + ' ' + p.fecha.slice(11, 16) + ' '));
      var b = el('button', 'boton chico secundario', 'Ver'); b.type = 'button';
      b.addEventListener('click', function () { verProblema(p.id); });
      arriba.appendChild(b);
      li.appendChild(arriba);
      li.appendChild(el('span', 'cuanto', '«' + p.texto + '»' + (p.pista ? ' · ' + p.pista : '')));
      ul.appendChild(li);
    });
    if (!ul.firstChild) ul.appendChild(el('li', 'tenue', 'Ninguno. Cuando un amigo pulse «Tengo un problema» en su launcher, sale aquí y te avisa en Discord.'));
  }
  function verProblema(id) {
    var box = $('problema');
    problemaAbierto = id;
    box.hidden = false; limpiar(box); box.appendChild(el('p', 'tenue', 'Cargando…'));
    api('/api/admin/problema?id=' + encodeURIComponent(id)).then(function (r) {
      if (problemaAbierto !== id) return;
      if (r._code === 401) { verLogin('Tu sesión ha caducado. Entra otra vez.'); return; }
      limpiar(box);
      if (!r.ok) { box.appendChild(el('p', 'vacio', r.mensaje || 'No se ha podido abrir.')); return; }
      box.appendChild(el('h3', 'subtitulo', (r.nombre || 'Sin nombre') + ' · ' + r.fecha));
      box.appendChild(el('p', 'cita', r.texto));
      var dl = el('dl', 'datos');
      [['Su PC', r.equipo], ['Valheim', r.juego], ['Mods', r.mods]].forEach(function (x) {
        var div = document.createElement('div'); div.appendChild(el('dt', null, x[0])); div.appendChild(el('dd', null, x[1])); dl.appendChild(div);
      });
      box.appendChild(dl);
      box.appendChild(el('h3', 'subtitulo', 'Lo que se vio'));
      var ul = el('ul', 'lista apilada');
      (r.pistas || []).forEach(function (p) { ul.appendChild(el('li', null, p)); });
      box.appendChild(ul);
      if (r.ia) { box.appendChild(el('h3', 'subtitulo', 'Lo que le dijo la IA')); box.appendChild(el('p', 'cita', r.ia)); }
      [['mods', 'Registro de los mods (BepInEx)'], ['juego', 'Registro de Valheim (Player.log)'], ['launcher', 'Registro del launcher']].forEach(function (x) {
        var det = el('details', 'registro-plegable'), sum = el('summary', null, x[1]);
        det.appendChild(sum); det.appendChild(el('pre', 'registro', (r.registros && r.registros[x[0]]) || '(vacío)'));
        box.appendChild(det);
      });
      var bot = el('div', 'botonera');
      var cerrar = el('button', 'boton secundario', 'Cerrar'); cerrar.type = 'button';
      cerrar.addEventListener('click', function () { problemaAbierto = null; box.hidden = true; limpiar(box); });
      var borrar = el('button', 'boton peligro', 'Borrar'); borrar.type = 'button';
      borrar.addEventListener('click', function () {
        if (!window.confirm('¿Borrar este problema? No se puede recuperar.')) return;
        api('/api/admin/problema-borrar', { id: id }).then(function (x) {
          if (x._code === 401) { verLogin('Tu sesión ha caducado. Entra otra vez.'); return; }
          problemaAbierto = null; box.hidden = true; limpiar(box);
          refrescar();
        });
      });
      bot.appendChild(cerrar); bot.appendChild(borrar);
      box.appendChild(bot);
    }).catch(function () { limpiar(box); box.appendChild(el('p', 'vacio', 'Sin conexión con el server.')); });
  }

  function pintarExtras(d) {
    pintarSalud(d);
    pintarSeguridad(d);
    if (!ajustesCargados) cargarAjustes();
    pintarMantenimiento(d);
    pintarProblemas(d);
    var t = d.tareas || {};
    $('a-tarea-backup').textContent = t.backupHoras ? 'cada ' + t.backupHoras + ' h' + (t.ultimoBackupAuto ? ' · último ' + new Date(t.ultimoBackupAuto).toLocaleString('es-ES', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }) : '') : (d.tareas ? 'solo al arrancar' : '—');
    $('a-tarea-reinicio').textContent = t.reinicioDiario ? 'a las ' + t.reinicioDiario + ' si no hay nadie' : (d.tareas ? 'desactivado' : '—');
    $('a-tarea-discord').textContent = t.discord === 'ok' ? 'funcionando' : t.discord === 'error' ? 'el último aviso falló' : (d.tareas ? 'sin configurar' : '—');
    $('a-tarea-discord').className = t.discord === 'error' ? 'aviso' : '';
    $('a-tarea-externa').textContent = t.copiaExterna === 'ok' ? (t.copiaExternaUltima ? 'última ' + new Date(t.copiaExternaUltima).toLocaleString('es-ES', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }) : 'activada') : t.copiaExterna === 'error' ? 'la última copia falló' : (d.tareas ? 'desactivada' : '—');
    $('a-tarea-externa').className = t.copiaExterna === 'error' ? 'aviso' : '';

    var act = d.actualizaciones || [];
    $('n-act').textContent = act.length;
    var ua = $('actualizaciones'); limpiar(ua);
    act.forEach(function (u) {
      var li = document.createElement('li');
      var q = document.createElement('span'); q.className = 'quien'; q.textContent = u.nombre;
      var c = document.createElement('span'); c.className = 'cuanto'; c.textContent = u.actual + ' → ' + u.nueva;
      li.appendChild(q); li.appendChild(c); ua.appendChild(li);
    });
    if (!ua.firstChild) { var li0 = document.createElement('li'); li0.className = 'tenue'; li0.textContent = 'Todo al día.'; ua.appendChild(li0); }

    pintarBackups(d);
  }

  // Backups (2.5): los que llevan el mundo se pueden restaurar desde aquí.
  var ultimo = null;
  function pintarBackups(d) {
    ultimo = d;
    var ub = $('backups');
    var bks = (d.backups || []).slice(0, 8);
    var clave = bks.map(function (b) { return (b.archivo || b.nombre) + (b.mundo ? '+' : ''); }).join('|');
    if (ub.getAttribute('data-clave') === clave) return;
    ub.setAttribute('data-clave', clave);
    limpiar(ub);
    bks.forEach(function (b) {
      var li = document.createElement('li');
      var izq = el('span', 'columna');
      izq.appendChild(el('span', 'quien', fechaCorta(b.fecha)));
      izq.appendChild(el('span', 'cuanto', (tipos[b.nombre] || b.nombre.replace(/[-_]/g, ' ')) + ' · ' + String(b.mb).replace('.', ',') + ' MB'));
      li.appendChild(izq);
      if (b.mundo && b.archivo) {
        var bt = el('button', 'boton secundario chico', 'Restaurar…');
        bt.type = 'button';
        bt.addEventListener('click', function () { abrirRestaurar(b); });
        li.appendChild(bt);
      }
      ub.appendChild(li);
    });
    if (!ub.firstChild) { var li1 = document.createElement('li'); li1.className = 'tenue'; li1.textContent = 'Todavía no hay backups.'; ub.appendChild(li1); }
  }

  function abrirRestaurar(b) {
    var box = $('restaurar'); limpiar(box); box.hidden = false;
    var s = ((ultimo && ultimo.estado) || {}).servidor || {};
    var encendido = s.estado && s.estado !== 'apagado';
    box.appendChild(el('h3', 'subtitulo', 'Volver el mundo al backup del ' + fechaCorta(b.fecha)));
    var ul = el('ul', 'pasos');
    ['El mundo y los personajes vuelven a como estaban en ese momento: lo construido y conseguido después se pierde.',
     'Antes se guarda una copia de cómo está ahora («antes de restaurar»), por si te arrepientes.',
     encendido ? 'El server se apaga guardando el mundo y vuelve a arrancar solo.' : 'El server está apagado y se queda apagado.',
     'Los ajustes, las cuentas y la lista de baneados no se tocan.',
     'No se hace si hay alguien jugando.'].forEach(function (t) { ul.appendChild(el('li', null, t)); });
    box.appendChild(ul);
    if (ultimo && ultimo.jugando) box.appendChild(el('p', 'aviso', 'Ahora hay gente jugando: espera a que no haya nadie.'));
    var f = el('div', 'formulario');
    var lab = el('label', null, 'Para confirmar, escribe RESTAURAR');
    lab.htmlFor = 'restaurar-confirmar';
    var inp = el('input'); inp.id = 'restaurar-confirmar'; inp.type = 'text'; inp.autocomplete = 'off'; inp.spellcheck = false; inp.maxLength = 20;
    var fila = el('div', 'botonera');
    var si = el('button', 'boton peligro', 'Restaurar ahora'); si.type = 'button'; si.disabled = true;
    var no = el('button', 'boton secundario', 'Cancelar'); no.type = 'button';
    var res = el('p', 'tenue'); res.setAttribute('role', 'status');
    inp.addEventListener('input', function () { si.disabled = inp.value.trim().toUpperCase() !== 'RESTAURAR'; });
    no.addEventListener('click', function () { box.hidden = true; limpiar(box); });
    si.addEventListener('click', function () {
      si.disabled = true; inp.disabled = true;
      res.textContent = 'Un momento…';
      api('/api/admin/restaurar', { archivo: b.archivo, confirmar: inp.value.trim() }).then(function (r) {
        if (r._code === 401) { verLogin('Tu sesión ha caducado. Entra otra vez.'); return; }
        res.textContent = r.mensaje || '';
        res.className = r.ok ? 'tenue' : 'aviso';
        if (r.ok) { no.textContent = 'Cerrar'; } else { inp.disabled = false; si.disabled = false; }
      }).catch(function () { res.textContent = 'Sin conexión con el server.'; inp.disabled = false; si.disabled = false; });
    });
    f.appendChild(lab); f.appendChild(inp);
    fila.appendChild(si); fila.appendChild(no);
    box.appendChild(f); box.appendChild(fila); box.appendChild(res);
    inp.focus();
    box.scrollIntoView({ block: 'nearest' });
  }

  // Seguridad de la cuenta (2.5): verificación en dos pasos con una app de códigos.
  var segFlujo = false;
  function pintarSeguridad(d) {
    if (segFlujo) return;
    var on = !!d.dosPasos;
    $('seg-estado').className = 'resumen-salud ' + (on ? 'verde' : 'ambar');
    $('seg-estado').textContent = on ? 'Verificación en dos pasos: activada' : 'Verificación en dos pasos: desactivada';
    $('seg-texto').textContent = on
      ? 'Para entrar como administrador hace falta la contraseña y el código de 6 cifras de la app del móvil.'
      : 'Con ella, para entrar como administrador hace falta además un código de 6 cifras de una app del móvil (Google Authenticator, Microsoft Authenticator…). Aunque alguien sepa tu contraseña, no podrá entrar.';
    $('seg-boton').textContent = on ? 'Quitar' : 'Activar';
    $('seg-boton').className = on ? 'boton secundario' : 'boton';
    $('seg-boton').setAttribute('data-on', on ? '1' : '0');
  }

  function cerrarSeguridad() { segFlujo = false; $('seg-flujo').hidden = true; limpiar($('seg-flujo')); $('seg-botonera').hidden = false; refrescar(); }

  function campoCodigo(id, texto) {
    var f = el('div', 'formulario');
    var lab = el('label', null, texto); lab.htmlFor = id;
    var inp = el('input'); inp.id = id; inp.type = 'text'; inp.inputMode = 'numeric'; inp.autocomplete = 'one-time-code'; inp.maxLength = 7; inp.placeholder = '123 456'; inp.spellcheck = false;
    f.appendChild(lab); f.appendChild(inp);
    return { caja: f, input: inp };
  }

  $('seg-boton').addEventListener('click', function () {
    var box = $('seg-flujo'); limpiar(box);
    var on = $('seg-boton').getAttribute('data-on') === '1';
    var res = el('p', 'tenue'); res.setAttribute('role', 'status');
    var fila = el('div', 'botonera');
    var no = el('button', 'boton secundario', 'Cancelar'); no.type = 'button';
    no.addEventListener('click', cerrarSeguridad);
    segFlujo = true; $('seg-botonera').hidden = true; box.hidden = false;
    if (on) {
      var c = campoCodigo('seg-codigo-quitar', 'Para quitarla, escribe el código que sale ahora en la app');
      var q = el('button', 'boton peligro', 'Quitar'); q.type = 'button';
      var mandar = function () {
        q.disabled = true; res.textContent = 'Un momento…';
        api('/api/admin/2fa-quitar', { codigo: c.input.value }).then(function (r) {
          if (r._code === 401) { segFlujo = false; verLogin('Tu sesión ha caducado. Entra otra vez.'); return; }
          if (r.ok) { cerrarSeguridad(); $('seg-texto').textContent = r.mensaje; return; }
          res.textContent = r.mensaje || 'No se pudo.'; res.className = 'aviso'; q.disabled = false; c.input.select();
        }).catch(function () { res.textContent = 'Sin conexión con el server.'; q.disabled = false; });
      };
      q.addEventListener('click', mandar);
      c.input.addEventListener('keydown', function (ev) { if (ev.key === 'Enter') mandar(); });
      box.appendChild(c.caja); fila.appendChild(q); fila.appendChild(no); box.appendChild(fila); box.appendChild(res);
      c.input.focus();
      return;
    }
    box.appendChild(el('p', 'vacio', 'Preparando…'));
    api('/api/admin/2fa-preparar', {}).then(function (r) {
      limpiar(box);
      if (!r.ok) { box.appendChild(el('p', 'aviso', r.mensaje || 'No se pudo.')); fila.appendChild(no); box.appendChild(fila); return; }
      var pasos = el('ol', 'pasos');
      var p1 = el('li', null, 'Abre en el móvil una app de códigos (Google Authenticator, Microsoft Authenticator o la que uses), pulsa añadir y escanea este código:');
      var img = el('img', 'qr'); img.src = r.qr; img.alt = 'Código QR para la app de verificación'; img.width = 200; img.height = 200;
      p1.appendChild(img);
      p1.appendChild(el('span', 'tenue', '¿No puedes escanearlo? Elige «introducir clave» y escribe: '));
      var kc = el('span', 'clave-totp');
      r.clave.split(' ').forEach(function (g, i) { if (i) kc.appendChild(document.createTextNode(' ')); kc.appendChild(el('span', 'grupo', g)); });
      p1.appendChild(kc);
      pasos.appendChild(p1);
      pasos.appendChild(el('li', null, 'Escribe aquí el código de 6 cifras que te sale en la app.'));
      box.appendChild(pasos);
      var c = campoCodigo('seg-codigo', 'Código de la app');
      var si = el('button', 'boton', 'Activar'); si.type = 'button';
      var mandar = function () {
        si.disabled = true; res.textContent = 'Comprobando…'; res.className = 'tenue';
        api('/api/admin/2fa-activar', { codigo: c.input.value }).then(function (x) {
          if (x._code === 401) { segFlujo = false; verLogin('Tu sesión ha caducado. Entra otra vez.'); return; }
          if (x.ok) { cerrarSeguridad(); $('seg-texto').textContent = x.mensaje; return; }
          res.textContent = x.mensaje || 'No se pudo.'; res.className = 'aviso'; si.disabled = false; c.input.select();
        }).catch(function () { res.textContent = 'Sin conexión con el server.'; si.disabled = false; });
      };
      si.addEventListener('click', mandar);
      c.input.addEventListener('keydown', function (ev) { if (ev.key === 'Enter') mandar(); });
      box.appendChild(c.caja); fila.appendChild(si); fila.appendChild(no); box.appendChild(fila); box.appendChild(res);
      c.input.focus({ preventScroll: true });
    }).catch(function () { limpiar(box); box.appendChild(el('p', 'aviso', 'Sin conexión con el server.')); fila.appendChild(no); box.appendChild(fila); });
  });

  // Ajustes de los mods (2.5): se cargan al abrir el panel y al elegir otro mod; no se repintan solos para no perder lo escrito.
  var cambios = {}, ajustesCargados = false, buscarTimer = null;
  function claveAjuste(a) { return a.archivo + '|' + a.seccion + '|' + a.clave; }
  function nCambios() { return Object.keys(cambios).length; }
  function botonesAjustes() {
    var n = nCambios();
    $('ajustes-ver').disabled = n === 0;
    $('ajustes-ver').textContent = n ? 'Revisar cambios (' + n + ')' : 'Revisar cambios';
    $('ajustes-deshacer').disabled = n === 0;
  }

  function cargarAjustes() {
    ajustesCargados = true;
    var q = $('ajustes-buscar').value.trim(), f = $('ajustes-mod').value;
    var url = '/api/admin/ajustes' + (q ? '?buscar=' + encodeURIComponent(q) : f ? '?archivo=' + encodeURIComponent(f) : '');
    $('ajustes-titulo').textContent = 'Cargando…';
    api(url).then(function (r) {
      if (r._code === 401) { verLogin('Tu sesión ha caducado. Entra otra vez.'); return; }
      if (r.mods) {
        var sel = $('ajustes-mod'), actual = sel.value;
        while (sel.options.length > 1) sel.remove(1);
        r.mods.forEach(function (m) {
          var o = el('option', null, m.nombre + ' (' + m.ajustes + ')'); o.value = m.archivo; sel.appendChild(o);
        });
        sel.value = actual;
      }
      pintarAjustes(r);
    }).catch(function () { $('ajustes-titulo').textContent = 'Sin conexión con el server.'; });
  }

  function pintarAjustes(r) {
    var ul = $('ajustes'); limpiar(ul);
    if (!r.ok) { $('ajustes-titulo').textContent = r.mensaje || 'No se pudo.'; return; }
    var lista = r.ajustes || [];
    var unMod = !!$('ajustes-mod').value && !$('ajustes-buscar').value.trim();   // un solo mod: no repetir su nombre en cada ajuste
    $('ajustes-titulo').textContent = r.titulo + (lista.length ? '' : ': no hay ninguno.');
    lista.forEach(function (a) {
      var k = claveAjuste(a), pend = cambios[k];
      var li = el('li', pend ? 'cambiado' : null);
      var cab = el('div', 'cabeza');
      cab.appendChild(el('span', 'quien', a.clave));
      cab.appendChild(el('span', 'cuanto', (unMod ? '' : a.mod + (a.seccion ? ' · ' : '')) + (a.seccion || '')));
      li.appendChild(cab);
      var info = [];
      if (a.descripcion) info.push(/[.!?:)]$/.test(a.descripcion) ? a.descripcion : a.descripcion + '.');
      if (a.rango) info.push('Rango: ' + a.rango + '.');
      if (a.varios && a.opciones && a.opciones.length) info.push('Puedes poner varios separados por comas: ' + a.opciones.join(', ') + '.');
      if (a.defecto != null) info.push('Por defecto: ' + a.defecto + '.');
      if (info.length) li.appendChild(el('span', 'cuanto', info.join(' ')));
      var fila = el('div', 'en-linea');
      var campo;
      var opciones = a.tipo === 'Boolean' ? ['true', 'false'] : (!a.varios && a.opciones && a.opciones.length ? a.opciones : null);
      if (opciones) {
        campo = el('select', 'campo');
        if (opciones.indexOf(a.valor) < 0) opciones = [a.valor].concat(opciones);
        opciones.forEach(function (v) {
          var o = el('option', null, a.tipo === 'Boolean' ? (v === 'true' ? 'Sí (true)' : v === 'false' ? 'No (false)' : v) : v);
          o.value = v; campo.appendChild(o);
        });
      } else {
        campo = el('input', 'campo'); campo.type = 'text'; campo.maxLength = 500; campo.spellcheck = false; campo.autocomplete = 'off';
        if (/^(Int|Single|Double|Decimal|Byte)/.test(a.tipo || '')) campo.inputMode = 'decimal';
      }
      campo.setAttribute('aria-label', a.clave);
      campo.value = pend ? pend.valor : a.valor;
      var antes = el('span', 'antes', pend ? 'antes: ' + a.valor : '');
      var cambiar = function () {
        var v = campo.value.trim();
        if (v === a.valor) delete cambios[k];
        else cambios[k] = { archivo: a.archivo, seccion: a.seccion, clave: a.clave, valor: v, antes: a.valor, mod: a.mod };
        li.className = cambios[k] ? 'cambiado' : '';
        antes.textContent = cambios[k] ? 'antes: ' + a.valor : '';
        botonesAjustes();
      };
      campo.addEventListener('change', cambiar);
      campo.addEventListener('input', cambiar);
      fila.appendChild(campo); fila.appendChild(antes);
      li.appendChild(fila);
      ul.appendChild(li);
    });
    botonesAjustes();
  }

  $('ajustes-mod').addEventListener('change', function () { $('ajustes-buscar').value = ''; cargarAjustes(); });
  $('ajustes-buscar').addEventListener('input', function () {
    if (buscarTimer) clearTimeout(buscarTimer);
    buscarTimer = setTimeout(cargarAjustes, 350);
  });
  $('ajustes-deshacer').addEventListener('click', function () { cambios = {}; $('ajustes-revisar').hidden = true; cargarAjustes(); });
  $('ajustes-ver').addEventListener('click', function () {
    var box = $('ajustes-revisar'); limpiar(box); box.hidden = false;
    var lista = Object.keys(cambios).map(function (k) { return cambios[k]; });
    box.appendChild(el('h3', 'subtitulo', lista.length === 1 ? '¿Guardar este cambio?' : '¿Guardar estos ' + lista.length + ' cambios?'));
    var ul = el('ul', 'cambios');
    lista.forEach(function (c) { ul.appendChild(el('li', null, c.mod + ' › ' + c.clave + ': ' + c.antes + ' → ' + c.valor)); });
    box.appendChild(ul);
    box.appendChild(el('p', 'tenue', 'Antes de guardar se hace una copia de los archivos que cambian.'));
    var fila = el('div', 'botonera');
    var si = el('button', 'boton', 'Sí, guardar'); si.type = 'button';
    var no = el('button', 'boton secundario', 'Volver'); no.type = 'button';
    var res = el('p', 'tenue'); res.setAttribute('role', 'status');
    no.addEventListener('click', function () { box.hidden = true; limpiar(box); $('ajustes-botonera').hidden = false; });
    si.addEventListener('click', function () {
      si.disabled = true; res.textContent = 'Guardando…';
      api('/api/admin/ajustes-guardar', { cambios: lista.map(function (c) { return { archivo: c.archivo, seccion: c.seccion, clave: c.clave, valor: c.valor }; }) }).then(function (r) {
        if (r._code === 401) { verLogin('Tu sesión ha caducado. Entra otra vez.'); return; }
        if (r.ok) {
          cambios = {}; box.hidden = true; limpiar(box); $('ajustes-botonera').hidden = false;
          $('ajustes-resultado').textContent = r.mensaje; $('ajustes-resultado').className = 'tenue';
          cargarAjustes();
          return;
        }
        res.textContent = r.mensaje || 'No se pudo.'; res.className = 'aviso'; si.disabled = false;
      }).catch(function () { res.textContent = 'Sin conexión con el server.'; si.disabled = false; });
    });
    fila.appendChild(si); fila.appendChild(no);
    box.appendChild(fila); box.appendChild(res);
    $('ajustes-botonera').hidden = true;
    $('ajustes-resultado').textContent = '';
    box.scrollIntoView({ block: 'nearest' });
  });

  function refrescar() {
    api('/api/admin/estado').then(function (d) {
      if (!d.dentro) verLogin(d.configurado === false ? 'Los controles del server están desactivados (no hay administrador). Puedes entrar como invitado.' : null);
      else if (d.rol !== 'admin') verJugador(d);
      else verPanel(d);
    }).catch(function () { $('estado').className = 'pastilla gris'; $('estado').textContent = 'Sin conexión'; });
  }

  $('form-login').addEventListener('submit', function (ev) {
    ev.preventDefault();
    $('login-error').textContent = '';
    var datos = { usuario: $('usuario').value.trim(), clave: $('clave').value, recordar: $('recordar').checked };
    var conCodigo = !$('fila-codigo').hidden;
    if (conCodigo) datos.codigo = $('codigo').value.trim();
    api('/api/entrar', datos).then(function (r) {
      if (r.codigo) {
        // Contraseña buena; falta (o está mal) el código de la app de verificación.
        $('fila-codigo').hidden = false;
        if (conCodigo && datos.codigo) $('login-error').textContent = r.mensaje || 'Código incorrecto.';
        else $('login-info').textContent = r.mensaje || 'Escribe el código de tu app de verificación.';
        $('codigo').value = ''; $('codigo').focus();
        return;
      }
      $('clave').value = ''; $('codigo').value = ''; $('fila-codigo').hidden = true;
      if (!r.ok) { $('login-error').textContent = r.mensaje || 'No se pudo entrar.'; $('clave').focus(); return; }
      if (r.rol === 'admin') refrescar();
      else location.href = '/';
    }).catch(function () { $('login-error').textContent = 'Sin conexión con el server. Prueba otra vez.'; });
  });

  $('salir').addEventListener('click', function () {
    api('/api/salir', {}).then(function () { verLogin('Has salido.'); });
  });

  $('salir-todos').addEventListener('click', function () {
    if (!window.confirm('¿Cerrar la sesión en todos los dispositivos, también en este? Todos tendrán que volver a entrar.')) return;
    api('/api/admin/salir-todos', {}).then(function (r) { verLogin(r.mensaje || 'Sesiones cerradas.'); });
  });

  $('form-aviso').addEventListener('submit', function (ev) {
    ev.preventDefault();
    var t = $('aviso-texto').value.trim();
    if (!t) return;
    $('aviso-resultado').textContent = 'Enviando…';
    api('/api/admin/aviso', { mensaje: t }).then(function (r) {
      if (r._code === 401) { verLogin('Tu sesión ha caducado. Entra otra vez.'); return; }
      $('aviso-resultado').textContent = r.mensaje || '';
      if (r.ok) $('aviso-texto').value = '';
    });
  });

  $('form-consola').addEventListener('submit', function (ev) {
    ev.preventDefault();
    consola($('consola-orden').value);
  });
  Array.prototype.forEach.call(document.querySelectorAll('[data-orden]'), function (b) {
    b.addEventListener('click', function () { consola(b.getAttribute('data-orden')); });
  });

  Array.prototype.forEach.call(document.querySelectorAll('[data-accion]'), function (b) {
    b.addEventListener('click', function () {
      var msg = b.getAttribute('data-confirmar');
      if (msg && !window.confirm(msg)) return;
      $('resultado').textContent = 'Un momento…';
      api('/api/admin/accion', { accion: b.getAttribute('data-accion') }).then(function (r) {
        if (r._code === 401) { verLogin('Tu sesión ha caducado. Entra otra vez.'); return; }
        $('resultado').textContent = r.mensaje || '';
        refrescar();
      });
    });
  });

  refrescar();
})();
