// Majinheim Web: la página de cada vikingo (2.6). Pide /api/jugador?nombre=... cada minuto. Todo con textContent (los nombres vienen del juego).
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var nombre = new URLSearchParams(location.search).get('n') || '';

  function el(tag, cls, txt) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (txt != null) e.textContent = txt;
    return e;
  }
  function limpiar(n) { while (n.firstChild) n.removeChild(n.firstChild); }
  function horas(min) {
    if (min < 60) return min + ' min';
    var h = min / 60;
    return (h < 10 ? h.toFixed(1).replace('.', ',') : Math.round(h).toLocaleString('es-ES')) + ' h';
  }
  function fechaCorta(iso) { return new Date(iso).toLocaleDateString('es-ES', { day: 'numeric', month: 'short' }); }
  function fechaLarga(iso) { return new Date(iso).toLocaleDateString('es-ES', { day: 'numeric', month: 'long', year: 'numeric' }); }
  function cuando(iso) {
    var d = new Date(iso), hoy = new Date(), ayer = new Date(Date.now() - 86400000);
    var h = d.toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' });
    if (d.toDateString() === hoy.toDateString()) return 'Hoy, ' + h;
    if (d.toDateString() === ayer.toDateString()) return 'Ayer, ' + h;
    return d.toLocaleDateString('es-ES', { weekday: 'short', day: 'numeric', month: 'short' }) + ', ' + h;
  }
  function hace(iso) {
    if (!iso) return 'jugando ahora';
    var min = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
    if (min < 60) return 'hace ' + Math.max(1, min) + ' min';
    if (min < 1440) return 'hace ' + Math.round(min / 60) + ' h';
    var dias = Math.round(min / 1440);
    return dias === 1 ? 'ayer' : 'hace ' + dias + ' días';
  }
  function franja(h) {
    if (h >= 6 && h < 14) return 'por la mañana';
    if (h >= 14 && h < 21) return 'por la tarde';
    if (h >= 21 || h < 2) return 'por la noche';
    return 'de madrugada';
  }
  function barras(cont, valores, titulo) {
    limpiar(cont);
    var max = valores.reduce(function (a, v) { return Math.max(a, v); }, 0) || 1;
    valores.forEach(function (v, i) {
      var b = el('div', 'barra' + (v === 0 ? ' cero' : ''));
      b.style.height = Math.max(v ? 4 : 2, Math.round(100 * v / max)) + '%';
      b.title = titulo(i, v);
      cont.appendChild(b);
    });
  }
  function item(ul, a, b, enlace) {
    var li = el('li'), q = el(enlace ? 'a' : 'span', 'quien', a);
    if (enlace) q.href = enlace;
    li.appendChild(q);
    if (b) li.appendChild(el('span', 'cuanto', b));
    ul.appendChild(li);
  }

  function pintar(d) {
    $('contenido').hidden = false; $('noexiste').hidden = true;
    document.title = 'Majinheim · ' + d.nombre;
    limpiar($('nombre'));
    if (d.jugando) $('nombre').appendChild(el('span', 'punto'));
    $('nombre').appendChild(document.createTextNode(d.nombre));
    $('sub').textContent = d.jugando ? 'Jugando ahora' : 'Última vez ' + hace(d.ultima);

    $('c-horas').textContent = d.minutos < 60 ? horas(d.minutos) : Math.round(d.minutos / 60).toLocaleString('es-ES');
    $('c-semana').textContent = d.semana ? horas(d.semana) : '—';
    $('c-sesiones').textContent = d.sesiones.toLocaleString('es-ES');
    $('c-muertes').textContent = d.muertes.toLocaleString('es-ES');
    var r = ['Juega desde el ' + fechaLarga(d.primera) + '.'];
    if (d.de > 1) r.push(d.puesto === 1 ? 'Es quien más ha jugado de los ' + d.de + '.' : 'Va ' + d.puesto + 'º de ' + d.de + ' en horas jugadas.');
    r.push('Su partida más larga: ' + horas(d.masLarga) + ', el ' + fechaCorta(d.masLargaFecha) + '.');
    if (d.horaFavorita != null) r.push('Suele jugar ' + franja(d.horaFavorita) + '.');
    if (d.tumbas) r.push(d.tumbas === 1 ? 'Tiene una tumba sin recoger.' : 'Tiene ' + d.tumbas + ' tumbas sin recoger.');
    $('c-resumen').textContent = r.join(' ');

    var dias = d.dias || [];
    barras($('dias'), dias.map(function (x) { return x.minutos; }), function (i, v) {
      return fechaCorta(dias[i].fecha + 'T12:00:00') + ': ' + (v ? horas(v) : 'no jugó');
    });
    if (dias.length) $('dia-ini').textContent = fechaCorta(dias[0].fecha + 'T12:00:00');
    var jugados = dias.filter(function (x) { return x.minutos > 0; }).length;
    var tot = dias.reduce(function (a, x) { return a + x.minutos; }, 0);
    $('dias-resumen').textContent = jugados ? 'Ha jugado ' + jugados + (jugados === 1 ? ' día' : ' días') + ' de 30, ' + horas(tot) + ' en total.' : 'No ha jugado en los últimos 30 días.';

    barras($('horas'), d.horas || [], function (i, v) { return (i < 10 ? '0' : '') + i + ':00 · ' + horas(v); });

    var lg = $('logros'); limpiar(lg);
    (d.logros || []).slice().reverse().forEach(function (t) { item(lg, '🏅 ' + t); });
    if (!(d.logros || []).length) lg.appendChild(el('li', 'vacio', 'Todavía ninguno. El primero llega a las 10 horas.'));
    $('siguiente').textContent = d.siguiente ? 'Le faltan ' + horas(d.siguiente.faltan) + ' para las ' + d.siguiente.horas + ' horas.' : '';

    var cp = $('companeros'); limpiar(cp);
    (d.companeros || []).forEach(function (c) { item(cp, c.nombre, horas(c.minutos) + ' juntos', '/jugador?n=' + encodeURIComponent(c.nombre)); });
    $('solo').hidden = (d.companeros || []).length > 0;

    var tb = $('recientes'); limpiar(tb);
    (d.recientes || []).forEach(function (p) {
      var tr = el('tr');
      var c = el('td', 'nombre');
      if (p.jugando) c.appendChild(el('span', 'punto'));
      c.appendChild(document.createTextNode(cuando(p.inicio)));
      tr.appendChild(c);
      tr.appendChild(el('td', 'num', p.jugando ? horas(p.minutos) + ' (sigue)' : horas(p.minutos)));
      tr.appendChild(el('td', 'num', p.muertes));
      tb.appendChild(tr);
    });
  }

  function pedir() {
    if (!nombre) { $('noexiste').hidden = false; $('sub').textContent = ''; return; }
    fetch('/api/jugador?nombre=' + encodeURIComponent(nombre), { cache: 'no-store' })
      .then(function (r) {
        if (r.status === 404) { $('contenido').hidden = true; $('noexiste').hidden = false; $('sub').textContent = ''; $('nombre').textContent = nombre; return null; }
        if (!r.ok) throw new Error(r.status);
        return r.json();
      })
      .then(function (d) { $('aviso').hidden = true; if (d) pintar(d); })
      .catch(function () { $('aviso').hidden = false; });
  }

  (function () {
    var eje = $('horas-eje');
    for (var h = 0; h < 24; h++) eje.appendChild(el('span', null, h % 6 === 0 ? (h < 10 ? '0' : '') + h + ':00' : ''));
  })();

  pedir();
  setInterval(pedir, 60000);
})();
