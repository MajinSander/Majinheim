// Majinheim Web: estadísticas. Pide /api/estadisticas cada minuto. Todo con textContent (los nombres vienen del juego).
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };

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
  function fechaCorta(iso) {
    var d = new Date(iso);
    return d.toLocaleDateString('es-ES', { day: 'numeric', month: 'short' });
  }
  function hace(iso) {
    if (!iso) return 'jugando ahora';
    var min = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
    if (min < 60) return 'hace ' + Math.max(1, min) + ' min';
    if (min < 1440) return 'hace ' + Math.round(min / 60) + ' h';
    var dias = Math.round(min / 1440);
    return dias === 1 ? 'ayer' : 'hace ' + dias + ' días';
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

  function pintar(d) {
    var t = d.total || {};
    $('t-horas').textContent = Math.round((t.minutos || 0) / 60).toLocaleString('es-ES');
    $('t-sesiones').textContent = (t.sesiones || 0).toLocaleString('es-ES');
    $('t-jugadores').textContent = t.jugadores || 0;
    $('t-dia').textContent = t.dia != null ? t.dia : '—';
    $('t-desde').textContent = t.desde ? 'Desde el ' + new Date(t.desde).toLocaleDateString('es-ES', { day: 'numeric', month: 'long', year: 'numeric' }) + ', cuando el launcher empezó a apuntar las partidas.' : '';

    var dias = d.dias || [];
    barras($('dias'), dias.map(function (x) { return x.minutos; }), function (i, v) {
      var x = dias[i];
      return fechaCorta(x.fecha + 'T12:00:00') + ': ' + (v ? horas(v) + ', ' + x.jugadores + (x.jugadores === 1 ? ' vikingo' : ' vikingos') : 'nadie jugó');
    });
    if (dias.length) $('dia-ini').textContent = fechaCorta(dias[0].fecha + 'T12:00:00');
    var jugados = dias.filter(function (x) { return x.minutos > 0; }).length;
    var tot = dias.reduce(function (a, x) { return a + x.minutos; }, 0);
    $('dias-resumen').textContent = jugados ? 'Se ha jugado ' + jugados + (jugados === 1 ? ' día' : ' días') + ' de 30, ' + horas(tot) + ' en total.' : 'Nadie ha jugado en los últimos 30 días.';

    var hs = d.horas || [];
    barras($('horas'), hs, function (i, v) { return (i < 10 ? '0' : '') + i + ':00 · ' + horas(v); });

    var tb = $('jugadores'); limpiar(tb);
    var js = d.jugadores || [];
    $('vacio').hidden = js.length > 0;
    js.forEach(function (j) {
      var tr = el('tr');
      var n = el('td', 'nombre');
      if (j.jugando) n.appendChild(el('span', 'punto'));
      var a = el('a', null, j.nombre);   // la página de cada vikingo (2.6)
      a.href = '/jugador?n=' + encodeURIComponent(j.nombre);
      n.appendChild(a);
      tr.appendChild(n);
      tr.appendChild(el('td', 'num', horas(j.minutos)));
      tr.appendChild(el('td', 'num', j.semana ? horas(j.semana) : '—'));
      tr.appendChild(el('td', 'num', j.sesiones));
      tr.appendChild(el('td', 'num', horas(j.masLarga)));
      tr.appendChild(el('td', 'num', j.muertes));
      tr.appendChild(el('td', 'num', hace(j.ultima)));
      tb.appendChild(tr);
    });
  }

  function pedir() {
    fetch('/api/estadisticas', { cache: 'no-store' })
      .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
      .then(function (d) { $('aviso').hidden = true; pintar(d); })
      .catch(function () { $('aviso').hidden = false; });
  }

  // El botón del mapa solo si el mapa está encendido (lo dice /api/estado).
  fetch('/api/estado', { cache: 'no-store' }).then(function (r) { return r.json(); })
    .then(function (s) { $('btn-mapa').hidden = !s.mapa; }).catch(function () { $('btn-mapa').hidden = true; });

  // Eje de horas alineado con las barras: una casilla por hora, con texto cada 6 horas.
  (function () {
    var eje = $('horas-eje');
    for (var h = 0; h < 24; h++) eje.appendChild(el('span', null, h % 6 === 0 ? (h < 10 ? '0' : '') + h + ':00' : ''));
  })();

  pedir();
  setInterval(pedir, 60000);
})();
