// Majinheim Web: administración. Todo con textContent; cada POST lleva la cabecera X-Majinheim.
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var timer = null;

  function api(path, body) {
    var o = { method: body ? 'POST' : 'GET', cache: 'no-store', credentials: 'same-origin', headers: {} };
    if (body) { o.headers['Content-Type'] = 'application/json'; o.headers['X-Majinheim'] = '1'; o.body = JSON.stringify(body); }
    return fetch(path, o).then(function (r) {
      return r.json().catch(function () { return {}; }).then(function (j) { j._code = r.status; return j; });
    });
  }

  function limpiar(n) { while (n.firstChild) n.removeChild(n.firstChild); }

  function verLogin(info) {
    $('panel').hidden = true; $('login').hidden = false; $('salir').hidden = true;
    $('estado').className = 'pastilla gris'; $('estado').textContent = 'Sin entrar';
    if (info) $('login-info').textContent = info;
    if (timer) { clearInterval(timer); timer = null; }
    $('clave').focus();
  }

  function verPanel(d) {
    $('login').hidden = true; $('panel').hidden = false; $('salir').hidden = false;
    var e = d.estado || {}, s = e.servidor || {};
    var textos = { 'en marcha': 'En marcha', 'arrancando': 'Arrancando…', 'apagado': 'Apagado' };
    var clases = { 'en marcha': 'verde', 'arrancando': 'ambar', 'apagado': 'rojo' };
    $('estado').className = 'pastilla ' + (clases[s.estado] || 'gris');
    $('estado').textContent = textos[s.estado] || '—';
    $('a-estado').textContent = textos[s.estado] || '—';
    var js = e.jugadores || [];
    $('a-jugadores').textContent = js.length ? js.map(function (j) { return j.nombre; }).join(', ') : 'nadie';
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

    var pre = $('registro');
    var abajo = pre.scrollTop + pre.clientHeight >= pre.scrollHeight - 20;
    pre.textContent = (d.registro || []).join('\n');
    if (abajo) pre.scrollTop = pre.scrollHeight;
  }

  function refrescar() {
    api('/api/admin/estado').then(function (d) {
      if (d.dentro) verPanel(d);
      else verLogin(d.configurado === false ? 'La administración web está desactivada. Se activa poniendo una contraseña en el launcher del server (pestaña Web).' : null);
    }).catch(function () { $('estado').textContent = 'Sin conexión'; });
  }

  $('form-login').addEventListener('submit', function (ev) {
    ev.preventDefault();
    $('login-error').textContent = '';
    api('/api/entrar', { clave: $('clave').value }).then(function (r) {
      $('clave').value = '';
      if (r.ok) { refrescar(); if (!timer) timer = setInterval(refrescar, 4000); }
      else $('login-error').textContent = r.mensaje || 'No se pudo entrar.';
    });
  });

  $('salir').addEventListener('click', function () { api('/api/salir', {}).then(function () { verLogin('Has salido.'); }); });

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
  timer = setInterval(refrescar, 4000);
})();
