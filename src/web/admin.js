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
  function pintarExtras(d) {
    pintarSalud(d);
    pintarMantenimiento(d);
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

    var ub = $('backups'); limpiar(ub);
    (d.backups || []).slice(0, 8).forEach(function (b) {
      var li = document.createElement('li');
      var q = document.createElement('span'); q.className = 'quien';
      q.textContent = new Date(b.fecha).toLocaleString('es-ES', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
      var c = document.createElement('span'); c.className = 'cuanto';
      c.textContent = (tipos[b.nombre] || b.nombre.replace(/[-_]/g, ' ')) + ' · ' + String(b.mb).replace('.', ',') + ' MB';
      li.appendChild(q); li.appendChild(c); ub.appendChild(li);
    });
    if (!ub.firstChild) { var li1 = document.createElement('li'); li1.className = 'tenue'; li1.textContent = 'Todavía no hay backups.'; ub.appendChild(li1); }
  }

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
    api('/api/entrar', { usuario: $('usuario').value.trim(), clave: $('clave').value, recordar: $('recordar').checked }).then(function (r) {
      $('clave').value = '';
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
