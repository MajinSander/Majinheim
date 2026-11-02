// Majinheim Web: pide /api/estado cada 5 s y pinta la página. Todo con textContent (los nombres vienen del juego).
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var datos = null;

  function el(tag, cls, txt) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (txt != null) e.textContent = txt;
    return e;
  }

  function duracion(min) {
    if (min < 1) return 'recién llegado';
    var h = Math.floor(min / 60), m = min % 60;
    return h > 0 ? h + ' h ' + (m < 10 ? '0' : '') + m + ' min' : m + ' min';
  }

  function horas(min) {
    if (min < 60) return min + ' min';
    var h = min / 60;
    return (h < 10 ? h.toFixed(1).replace('.', ',') : Math.round(h)) + ' h';
  }

  function fecha(iso) {
    if (!iso) return '—';
    var d = new Date(iso), hoy = new Date();
    var hora = d.toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' });
    if (d.toDateString() === hoy.toDateString()) return 'hoy, ' + hora;
    var ayer = new Date(hoy); ayer.setDate(hoy.getDate() - 1);
    if (d.toDateString() === ayer.toDateString()) return 'ayer, ' + hora;
    return d.toLocaleDateString('es-ES', { day: 'numeric', month: 'short' }) + ', ' + hora;
  }

  function hace(iso) {
    if (!iso) return '—';
    var min = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
    if (min < 1) return 'hace un momento';
    if (min < 60) return 'hace ' + min + ' min';
    if (min < 1440) return 'hace ' + duracion(min);
    return fecha(iso);
  }

  function limpiar(n) { while (n.firstChild) n.removeChild(n.firstChild); }

  function pintar() {
    var d = datos;
    if (!d) return;
    var s = d.servidor || {};
    $('nombre').textContent = s.nombre || 'Majinheim';
    document.title = (s.nombre || 'Majinheim') + ' · ' + (s.estado || '');

    var est = $('estado');
    var clases = { 'en marcha': 'verde', 'arrancando': 'ambar', 'apagado': 'rojo' };
    var textos = { 'en marcha': 'En marcha', 'arrancando': 'Arrancando…', 'apagado': 'Apagado' };
    est.className = 'pastilla ' + (clases[s.estado] || 'gris');
    est.textContent = textos[s.estado] || 'Desconocido';

    $('desde').textContent = s.desde ? fecha(s.desde) + ' (' + duracion(Math.round((Date.now() - new Date(s.desde).getTime()) / 60000)) + ')' : 'apagado';
    $('dia').textContent = s.dia != null ? 'Día ' + s.dia : '—';
    $('version').textContent = s.version || '—';
    $('guardado').textContent = hace(s.ultimoGuardado);
    $('backup').textContent = d.ultimoBackup ? hace(d.ultimoBackup.fecha) : '—';
    $('cpu').textContent = s.estado === 'apagado' ? '—' : String(s.cpu).replace('.', ',') + ' %';
    $('ram').textContent = s.estado === 'apagado' ? '—' : (s.ramMb >= 1024 ? (s.ramMb / 1024).toFixed(1).replace('.', ',') + ' GB' : s.ramMb + ' MB');

    var js = d.jugadores || [];
    $('n-jugadores').textContent = js.length;
    $('nadie').hidden = js.length > 0;
    var ul = $('jugadores'); limpiar(ul);
    js.forEach(function (j) {
      var li = el('li');
      var q = el('span', 'quien'); q.appendChild(el('span', 'punto')); q.appendChild(document.createTextNode(j.nombre));
      li.appendChild(q);
      var min = Math.max(0, Math.round((Date.now() - new Date(j.desde).getTime()) / 60000));
      li.appendChild(el('span', 'cuanto', duracion(min) + (j.muertes ? ' · ' + j.muertes + (j.muertes === 1 ? ' muerte' : ' muertes') : '')));
      ul.appendChild(li);
    });

    var sem = d.semana || [];
    $('semana-vacia').hidden = sem.length > 0;
    var ub = $('semana'); limpiar(ub);
    var max = sem.reduce(function (a, x) { return Math.max(a, x.minutos); }, 1);
    sem.forEach(function (x) {
      var li = el('li');
      var f = el('div', 'fila'); f.appendChild(el('span', null, x.nombre)); f.appendChild(el('span', 'tenue', horas(x.minutos)));
      var p = el('div', 'pista'), r = el('div', 'relleno'); r.style.width = Math.max(2, 100 * x.minutos / max) + '%';
      p.appendChild(r); li.appendChild(f); li.appendChild(p); ub.appendChild(li);
    });

    var ses = d.ultimasSesiones || [];
    $('sesiones-vacia').hidden = ses.length > 0;
    var us = $('sesiones'); limpiar(us);
    ses.forEach(function (x) {
      var li = el('li');
      li.appendChild(el('span', 'quien', x.nombre));
      var min = Math.max(0, Math.round((new Date(x.fin) - new Date(x.inicio)) / 60000));
      li.appendChild(el('span', 'cuanto', fecha(x.inicio) + ' · ' + duracion(min)));
      us.appendChild(li);
    });

    var mods = d.mods && d.mods.lista ? d.mods.lista : [];
    $('n-mods').textContent = mods.length;
    $('rev').textContent = d.mods ? 'revisión ' + d.mods.revision : '';
    var um = $('mods'); limpiar(um);
    mods.forEach(function (m) {
      var li = el('li');
      if (m.web && /^https:\/\/thunderstore\.io\//.test(m.web)) {
        var a = el('a', null, m.nombre); a.href = m.web; a.target = '_blank'; a.rel = 'noopener noreferrer'; li.appendChild(a);
      } else li.appendChild(el('span', null, m.nombre));
      li.appendChild(el('span', 'v', m.version));
      um.appendChild(li);
    });

    $('btn-mapa').hidden = d.mapa !== '/mapa/';
    $('panel').textContent = d.panel || '';
  }

  function pedir() {
    fetch('/api/estado', { cache: 'no-store' })
      .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
      .then(function (j) { datos = j; $('aviso').hidden = true; pintar(); })
      .catch(function () { $('aviso').hidden = false; });
  }

  // Quién soy: invitado (botón Entrar), jugador o administrador (botón Panel de control).
  function sesion() {
    fetch('/api/sesion', { cache: 'no-store', credentials: 'same-origin' })
      .then(function (r) { return r.json(); })
      .then(function (s) {
        $('quien').hidden = !s.dentro;
        if (s.dentro) $('quien').textContent = s.usuario + (s.rol === 'admin' ? ' · admin' : '');
        $('btn-salir').hidden = !s.dentro;
        $('btn-panel').hidden = !(s.dentro && s.rol === 'admin');
        $('btn-entrar').hidden = s.dentro || !s.cuentas;
      })
      .catch(function () { });
  }

  $('btn-salir').addEventListener('click', function () {
    fetch('/api/salir', { method: 'POST', cache: 'no-store', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', 'X-Majinheim': '1' }, body: '{}' })
      .then(sesion, sesion);
  });

  sesion();
  pedir();
  setInterval(pedir, 5000);
  setInterval(pintar, 30000);   // los minutos siguen contando aunque no cambie nada
})();
