// Majinheim Web: la máquina del tiempo (2.7). /api/historia da los días en que pasó algo; /api/historia/dia, un día al
// detalle con sus rutas; /api/mapa.png?dia= pinta el mapa con lo que estaba explorado ese día. Nombres siempre con textContent.
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var mapa = new Mapa2D($('mapa'));
  var dias = [], elegido = -1, hoy = '', primeraFoto = null, repitiendo = null, pedido = 0;
  var REPETIR_MS = 60000;
  var ICONOS = { jefe: '👑', muerte: '💀', mapa: '🧭', obra: '🔨', portal: '🌀', dia: '🌅' };

  function el(tag, cls, txt) { var e = document.createElement(tag); if (cls) e.className = cls; if (txt != null) e.textContent = txt; return e; }
  function limpiar(n) { while (n.firstChild) n.removeChild(n.firstChild); }
  function fecha(dia) { return new Date(dia + 'T12:00:00'); }
  function fechaLarga(dia) {
    if (dia === hoy) return 'Hoy, ' + fecha(dia).toLocaleDateString('es-ES', { day: 'numeric', month: 'long' });
    return fecha(dia).toLocaleDateString('es-ES', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
  }
  function fechaCorta(dia) { return fecha(dia).toLocaleDateString('es-ES', { day: 'numeric', month: 'short', year: '2-digit' }); }
  function dur(min) { return min < 60 ? min + ' min' : Math.floor(min / 60) + ' h' + (min % 60 ? ' ' + (min % 60) + ' min' : ''); }
  function hora(t) { return new Date(t * 1000).toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' }); }
  function resumen(d) {
    var n = d.jugadores.length, partes = [];
    if (n) partes.push((n === 1 ? d.jugadores[0].nombre : n + ' vikingos') + ' · ' + dur(d.minutos));
    if (d.muertes) partes.push(d.muertes === 1 ? '1 muerte' : d.muertes + ' muertes');
    d.hechos.forEach(function (h) { if (h.tipo !== 'muerte') partes.push(h.texto); });
    return partes.join(' · ') || 'Sin partidas';
  }

  function listaDias() {
    var ul = $('dias'); limpiar(ul);
    $('n-dias').textContent = dias.length;
    dias.forEach(function (d, i) {
      var li = el('li'), b = el('button', i === elegido ? 'elegido' : '');
      b.type = 'button';
      b.appendChild(el('span', 'fecha', fechaLarga(d.dia)));
      b.appendChild(el('span', 'resumen', resumen(d)));
      b.onclick = function () { elegir(i); window.scrollTo({ top: 0, behavior: 'smooth' }); };
      li.appendChild(b); ul.appendChild(li);
    });
  }

  // dias va del más nuevo al más viejo; el deslizador, de izquierda (viejo) a derecha (nuevo).
  function elegir(i) {
    if (i < 0 || i >= dias.length) return;
    parar();
    elegido = i;
    var d = dias[i];
    $('deslizador').value = dias.length - 1 - i;
    $('titulo').textContent = fechaLarga(d.dia);
    $('b-antes').disabled = i >= dias.length - 1; $('b-despues').disabled = i <= 0;
    history.replaceState(null, '', '?dia=' + d.dia);
    $('enlace-3d').href = d.dia === hoy ? '/3d' : '/3d?dia=' + d.dia;
    // Lo que ya se sabe del día.
    var ul = $('hechos'); limpiar(ul);
    d.hechos.forEach(function (h) { var li = el('li'); li.appendChild(el('span', 'icono', ICONOS[h.tipo] || '•')); li.appendChild(el('span', null, h.texto)); ul.appendChild(li); });
    $('sin-hechos').hidden = d.hechos.length > 0;
    Array.prototype.forEach.call(document.querySelectorAll('#dias button'), function (b, k) { b.className = k === i ? 'elegido' : ''; });
    var mio = ++pedido;
    fetch('/api/historia/dia?dia=' + d.dia, { cache: 'no-store' }).then(function (r) { return r.json(); }).then(function (det) { if (mio === pedido) detalle(d, det); }).catch(function () { });
  }

  function detalle(d, det) {
    var ul = $('jugaron'); limpiar(ul);
    var porNombre = {};
    (det.partidas || []).forEach(function (p) { (porNombre[p.nombre] || (porNombre[p.nombre] = [])).push(p); });
    var nombres = Object.keys(porNombre);
    $('n-jug').textContent = nombres.length; $('nadie').hidden = nombres.length > 0;
    nombres.forEach(function (n) {
      var li = el('li'), q = el('a', 'quien');
      q.href = '/jugador?n=' + encodeURIComponent(n);
      var punto = el('span', 'punto'); punto.style.background = Mapa2D.color(n);
      q.appendChild(punto); q.appendChild(document.createTextNode(n));
      var ratos = porNombre[n].map(function (p) { return hora(p.inicio) + ' a ' + (p.jugando ? 'ahora' : hora(p.fin)); }).join(', ');
      var muertes = porNombre[n].reduce(function (a, p) { return a + p.muertes; }, 0);
      li.appendChild(q); li.appendChild(el('span', 'cuanto', 'De ' + ratos + (muertes ? ' · ' + (muertes === 1 ? '1 muerte' : muertes + ' muertes') : '')));
      ul.appendChild(li);
    });
    var nota = $('nota-mapa'), textos = [];
    if (det.diaMundo) textos.push('Día ' + det.diaMundo + ' en el mundo' + (det.piezas != null ? ' · ' + det.piezas.toLocaleString('es-ES') + ' piezas construidas' : ''));
    if (det.mapaDeHoy && d.dia !== hoy) textos.push('De este día no hay foto del mapa (se guardan desde ' + (primeraFoto ? 'el ' + fechaCorta(primeraFoto) : 'que se instaló esta página') + '): se enseña como está ahora.');
    if (!Object.keys(det.rutas || {}).length && nombres.length) textos.push('No hay rutas de este día: se guardan desde que existe el mapa en vivo, y solo de quien se marca como visible en el juego.');
    nota.hidden = !textos.length; nota.textContent = textos.join(' ');
    // El mapa.
    if (det.mapa) {
      $('sin-mapa').hidden = true;
      mapa.ponerFondo('/api/mapa.png?v=' + det.mapa.v + (d.dia !== hoy && !det.mapaDeHoy ? '&dia=' + d.dia : ''), det.mapa);
    } else $('sin-mapa').hidden = false;
    mapa.portales = det.portales || []; mapa.tumbas = det.tumbas || [];
    mapa.rutas = det.rutas || {}; mapa.vikingos = []; mapa.hasta = Infinity; mapa.cola = 1e9;
    $('b-repetir').disabled = !Object.keys(mapa.rutas).some(function (n) { return mapa.rutas[n].length > 1; });
    mapa.pedir();
  }

  function enRuta(pts, t) {
    if (!pts.length || t < pts[0][0]) return null;
    for (var i = pts.length - 1; i >= 0; i--) {
      if (pts[i][0] > t) continue;
      var a = pts[i], b = pts[i + 1];
      if (!b || b[3] || b[0] - a[0] > 120) return t - a[0] > 120 ? null : [a[1], a[2]];
      var f = (t - a[0]) / (b[0] - a[0]);
      return [a[1] + (b[1] - a[1]) * f, a[2] + (b[2] - a[2]) * f];
    }
    return null;
  }

  // Los ratos sin nadie se saltan: solo se repite el tiempo en que alguien se movía.
  function tramos() {
    var ts = [];
    Object.keys(mapa.rutas).forEach(function (n) { mapa.rutas[n].forEach(function (p) { ts.push(p[0]); }); });
    ts.sort(function (a, b) { return a - b; });
    var res = [], ini = ts[0], ant = ts[0];
    for (var i = 1; i < ts.length; i++) { if (ts[i] - ant > 300) { res.push([ini, ant]); ini = ts[i]; } ant = ts[i]; }
    if (ts.length) res.push([ini, ant]);
    return res;
  }

  $('b-repetir').onclick = function () {
    if (repitiendo) { parar(); return; }
    var tr = tramos(), total = tr.reduce(function (a, t) { return a + (t[1] - t[0]) + 1; }, 0);
    if (!total) return;
    repitiendo = { tramos: tr, total: total, t0: performance.now() };
    mapa.cola = 600;
    $('b-repetir').textContent = '■ Parar'; $('reloj').hidden = false;
    requestAnimationFrame(cuadro);
  };
  function parar() {
    repitiendo = null; mapa.hasta = Infinity; mapa._ahora = null; mapa.cola = 1e9; mapa.vikingos = [];
    $('b-repetir').textContent = '▶ Ver el día en un minuto'; $('reloj').hidden = true;
    mapa.pedir();
  }
  function cuadro() {
    if (!repitiendo) return;
    var f = (performance.now() - repitiendo.t0) / REPETIR_MS;
    if (f >= 1) { parar(); return; }
    var resto = f * repitiendo.total, t = 0;
    for (var i = 0; i < repitiendo.tramos.length; i++) {
      var tr = repitiendo.tramos[i], largo = tr[1] - tr[0] + 1;
      if (resto <= largo) { t = tr[0] + resto; break; }
      resto -= largo; t = tr[1];
    }
    mapa.hasta = t; mapa._ahora = t;
    mapa.vikingos = [];
    Object.keys(mapa.rutas).forEach(function (n) { var p = enRuta(mapa.rutas[n], t); if (p) mapa.vikingos.push({ n: n, x: p[0], z: p[1] }); });
    $('reloj').textContent = hora(t);
    mapa.pintar();
    requestAnimationFrame(cuadro);
  }

  $('b-todo').onclick = function () { mapa.ajustar(); };
  $('b-antes').onclick = function () { elegir(elegido + 1); };
  $('b-despues').onclick = function () { elegir(elegido - 1); };
  $('deslizador').oninput = function () { elegir(dias.length - 1 - Number(this.value)); };

  fetch('/api/historia', { cache: 'no-store' })
    .then(function (r) { return r.json().then(function (d) { if (!r.ok) throw new Error(d.error || 'error'); return d; }); })
    .then(function (d) {
      dias = d.dias || []; hoy = d.hoy; primeraFoto = d.primeraFoto;
      if (!dias.length) { $('vacio').hidden = false; $('sub').textContent = 'Aún no hay días que enseñar.'; return; }
      $('contenido').hidden = false;
      $('sub').textContent = dias.length === 1 ? 'Un día de historia.' : dias.length + ' días de historia. Mueve la barra para viajar.';
      $('deslizador').max = dias.length - 1;
      $('eje-ini').textContent = fechaCorta(dias[dias.length - 1].dia); $('eje-fin').textContent = fechaCorta(dias[0].dia);
      listaDias();
      var pedidoDia = new URLSearchParams(location.search).get('dia'), i = 0;
      dias.forEach(function (x, k) { if (x.dia === pedidoDia) i = k; });
      mapa.medir();
      elegir(i);
    })
    .catch(function (e) {
      $('vacio').hidden = false;
      $('vacio-texto').textContent = e && e.message && e.message !== 'error' && e.message !== 'Failed to fetch' ? e.message : 'No he podido cargar la historia. Prueba a recargar la página.';
      $('sub').textContent = '';
    });
})();
