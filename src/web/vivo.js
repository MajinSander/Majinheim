// Majinheim Web: mapa en vivo (2.7). Pide /api/vivo cada 2 segundos: dónde está cada vikingo y los puntos nuevos de su ruta.
// Entre una lectura y la siguiente los iconos se deslizan, para que no vayan a saltos. Los nombres van siempre con textContent.
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var mapa = new Mapa2D($('mapa'));
  var ultimo = 0, version = null, seguido = null, fallos = 0, vueltas = 0;
  var vistos = {};          // nombre -> { desde:[x,z], hasta:[x,z], t0 }
  var repitiendo = null;    // { ini, fin, t0 } mientras se repite la noche
  var CADA = 2000, REPETIR_MS = 60000;

  function el(tag, cls, txt) { var e = document.createElement(tag); if (cls) e.className = cls; if (txt != null) e.textContent = txt; return e; }
  function limpiar(n) { while (n.firstChild) n.removeChild(n.firstChild); }
  function hora(t) { return new Date(t * 1000).toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' }); }

  mapa.alMover = function (soloZoom) { if (!soloZoom && seguido) { seguido = null; pintarLista(actuales); } };
  $('b-todo').onclick = function () { seguido = null; mapa.ajustar(); pintarLista(actuales); };

  var actuales = [];
  function pintarLista(js) {
    var ul = $('lista'); limpiar(ul);
    $('n').textContent = js.length; $('nadie').hidden = js.length > 0;
    js.forEach(function (j) {
      var li = el('li');
      var q = el('span', 'quien');
      var punto = el('span', 'punto'); punto.style.background = Mapa2D.color(j.n);
      q.appendChild(punto); q.appendChild(document.createTextNode(j.n + (j.oculto ? ' (oculto)' : '')));
      var b = el('button', 'boton chico' + (seguido === j.n ? '' : ' secundario'), seguido === j.n ? 'Siguiendo' : 'Seguir');
      b.type = 'button';
      b.onclick = function () { seguido = seguido === j.n ? null : j.n; pintarLista(actuales); if (seguido) mapa.centrar(j.x, j.z, 0.35); };
      li.appendChild(q); li.appendChild(b); ul.appendChild(li);
    });
  }

  function recibir(d) {
    fallos = 0;
    if (d.mapa) {
      $('sin-mapa').hidden = true;
      if (d.mapa.v !== version) { version = d.mapa.v; mapa.ponerFondo('/api/mapa.png?v=' + d.mapa.v, d.mapa); }
      mapa.portales = d.mapa.portales || [];
    } else if (!ultimo && !version) $('sin-mapa').hidden = false;
    // Puntos nuevos de las rutas.
    Object.keys(d.rutas || {}).forEach(function (n) {
      var lista = mapa.rutas[n] || (mapa.rutas[n] = []);
      d.rutas[n].forEach(function (p) { if (!lista.length || p[0] > lista[lista.length - 1][0]) lista.push(p); });
    });
    ultimo = d.t;
    // Hacia dónde se desliza cada icono.
    var ahora = performance.now(), nuevos = {};
    (d.jugadores || []).forEach(function (j) {
      var v = vistos[j.n], desde = v ? posicion(v, ahora) : [j.x, j.z];
      if (v && Math.abs(j.x - desde[0]) + Math.abs(j.z - desde[1]) > 300) desde = [j.x, j.z];   // un portal: sin deslizar
      nuevos[j.n] = { desde: desde, hasta: [j.x, j.z], t0: ahora, oculto: j.oculto };
    });
    vistos = nuevos;
    actuales = d.jugadores || [];
    pintarLista(actuales);
    var n = actuales.length;
    $('estado').className = 'pastilla ' + (d.encendido ? 'verde' : 'gris');
    $('estado').textContent = d.encendido ? 'Server encendido' : 'Server apagado';
    $('sub').textContent = !d.encendido ? 'El server está apagado. Se ven las rutas de las últimas horas.' : n === 0 ? 'No hay nadie jugando ahora.' : n === 1 ? 'Un vikingo en el mapa.' : n + ' vikingos en el mapa.';
    var oc = $('ocultos');
    oc.hidden = !d.ocultos;
    if (d.ocultos) oc.textContent = d.ocultos === 1 ? 'Hay un vikingo más jugando que no se ha marcado como visible.' : 'Hay ' + d.ocultos + ' vikingos más jugando que no se han marcado como visibles.';
    $('b-repetir').disabled = !hayRutas();
  }

  function hayRutas() { return Object.keys(mapa.rutas).some(function (n) { return mapa.rutas[n].length > 1; }); }
  function posicion(v, ahora) {
    var f = Math.min(1, (ahora - v.t0) / CADA);
    return [v.desde[0] + (v.hasta[0] - v.desde[0]) * f, v.desde[1] + (v.hasta[1] - v.desde[1]) * f];
  }

  // Dónde estaba cada uno a una hora, sacado de su ruta (para repetir la noche).
  function enRuta(pts, t) {
    if (!pts.length || t < pts[0][0]) return null;
    for (var i = pts.length - 1; i >= 0; i--) {
      if (pts[i][0] > t) continue;
      var a = pts[i], b = pts[i + 1];
      if (!b) return t - a[0] > 120 ? null : [a[1], a[2]];
      if (b[3] || b[0] - a[0] > 120) return t - a[0] > 120 ? null : [a[1], a[2]];
      var f = (t - a[0]) / (b[0] - a[0]);
      return [a[1] + (b[1] - a[1]) * f, a[2] + (b[2] - a[2]) * f];
    }
    return null;
  }

  $('b-repetir').onclick = function () {
    if (repitiendo) { parar(); return; }
    var ini = Infinity, fin = 0;
    Object.keys(mapa.rutas).forEach(function (n) { var p = mapa.rutas[n]; if (p.length) { ini = Math.min(ini, p[0][0]); fin = Math.max(fin, p[p.length - 1][0]); } });
    if (!(fin > ini)) return;
    repitiendo = { ini: ini, fin: fin, t0: performance.now() };
    seguido = null; mapa.cola = 600;
    $('b-repetir').textContent = '■ Volver al directo'; $('reloj').hidden = false;
  };
  function parar() {
    repitiendo = null; mapa.hasta = Infinity; mapa._ahora = null; mapa.cola = 900;
    $('b-repetir').textContent = '▶ Repetir la noche'; $('reloj').hidden = true;
  }

  function cuadro() {
    var ahora = performance.now();
    if (repitiendo) {
      var f = (ahora - repitiendo.t0) / REPETIR_MS;
      if (f >= 1) parar();
      else {
        var t = repitiendo.ini + (repitiendo.fin - repitiendo.ini) * f;
        mapa.hasta = t; mapa._ahora = t;
        mapa.vikingos = [];
        Object.keys(mapa.rutas).forEach(function (n) { var p = enRuta(mapa.rutas[n], t); if (p) mapa.vikingos.push({ n: n, x: p[0], z: p[1] }); });
        $('reloj').textContent = hora(t);
      }
    }
    if (!repitiendo) {
      mapa._ahora = ultimo || null;
      mapa.vikingos = Object.keys(vistos).map(function (n) { var p = posicion(vistos[n], ahora); return { n: n, x: p[0], z: p[1], oculto: vistos[n].oculto }; });
      if (seguido) { var s = mapa.vikingos.filter(function (v) { return v.n === seguido; })[0]; if (s) { mapa.cx = s.x; mapa.cz = s.z; } }
    }
    mapa.pintar();
    requestAnimationFrame(cuadro);
  }

  function pedir() {
    var conMapa = ultimo && ++vueltas % 150 === 0 ? '&mapa=1' : '';   // cada 5 minutos, por si se ha explorado más
    fetch('/api/vivo?desde=' + ultimo + conMapa, { cache: 'no-store' })
      .then(function (r) { return r.json().then(function (d) { if (!r.ok) throw new Error(d.error || 'error'); return d; }); })
      .then(recibir)
      .catch(function (e) {
        if (++fallos < 3) return;
        $('estado').className = 'pastilla rojo'; $('estado').textContent = 'Sin conexión';
        $('sub').textContent = e && e.message && e.message !== 'Failed to fetch' ? e.message : 'Sin conexión con el server. Reintentando…';
      })
      .then(function () { setTimeout(pedir, document.hidden ? 10000 : CADA); });
  }

  pedir();
  requestAnimationFrame(cuadro);
})();
