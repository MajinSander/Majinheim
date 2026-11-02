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
    (d.acciones || []).forEach(function (a) {
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

    var pre = $('registro');
    var abajo = pre.scrollTop + pre.clientHeight >= pre.scrollHeight - 20;
    pre.textContent = (d.registro || []).join('\n');
    if (abajo) pre.scrollTop = pre.scrollHeight;
    if (!timer) timer = setInterval(refrescar, 4000);
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
