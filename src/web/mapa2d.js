// Majinheim Web: el mapa en 2D que comparten el mapa en vivo y la máquina del tiempo (2.7).
// Un lienzo que se arrastra y se amplía (ratón, rueda, dedos). El fondo es /api/mapa.png (solo lo explorado) y encima se
// pintan portales, rutas y vikingos. Norte arriba. Todo el texto se dibuja en el lienzo: nada de HTML con nombres del juego.
(function () {
  'use strict';
  var COLORES = ['#e0a54a', '#6fbf73', '#6fb3e0', '#d9614c', '#b48ad8', '#e8d36b', '#5fd0c0', '#e58fb5', '#a6c95a', '#f08a4b'];

  function color(nombre) {
    var h = 0, s = String(nombre || '').toLowerCase();
    for (var i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) >>> 0;
    return COLORES[h % COLORES.length];
  }

  function Mapa2D(lienzo) {
    var m = this;
    m.lienzo = lienzo; m.ctx = lienzo.getContext('2d');
    m.cx = 0; m.cz = 0; m.escala = 0.05;           // píxeles por metro
    m.fondo = null; m.limites = null;
    m.portales = []; m.rutas = {}; m.vikingos = []; m.tumbas = [];
    m.hasta = Infinity;                            // las rutas se pintan hasta esta hora (para repetir la noche)
    m.cola = 900;                                  // segundos de ruta que se ven bien marcados; lo anterior, más tenue
    m.alMover = null;                              // aviso de que el usuario ha arrastrado (para dejar de seguir a alguien)
    m._pendiente = false; m._dedos = {}; m._ajustado = false;
    new ResizeObserver(function () { m.medir(); }).observe(lienzo);
    m.medir(); m._eventos();
  }

  Mapa2D.color = color;

  Mapa2D.prototype.medir = function () {
    var r = this.lienzo.getBoundingClientRect(), dpr = window.devicePixelRatio || 1;
    this.w = Math.max(50, r.width); this.h = Math.max(50, r.height);
    this.lienzo.width = Math.round(this.w * dpr); this.lienzo.height = Math.round(this.h * dpr);
    this.dpr = dpr;
    if (!this._ajustado && this.limites) this.ajustar();
    this.pedir();
  };

  Mapa2D.prototype.ponerFondo = function (url, lim) {
    var m = this, cambia = !m.limites || m.limites.x0 !== lim.x0 || m.limites.x1 !== lim.x1 || m.limites.z0 !== lim.z0 || m.limites.z1 !== lim.z1;
    m.limites = lim;
    if (cambia && !m._tocado) m.ajustar();
    var img = new Image();
    img.onload = function () { m.fondo = img; m.pedir(); };
    img.src = url;
  };

  // Que se vea todo lo explorado.
  Mapa2D.prototype.ajustar = function () {
    var l = this.limites; if (!l) return;
    this.cx = (l.x0 + l.x1) / 2; this.cz = (l.z0 + l.z1) / 2;
    this.escala = Math.min(this.w / (l.x1 - l.x0), this.h / (l.z1 - l.z0)) * 0.96;
    this._ajustado = true; this._tocado = false;
    this.pedir();
  };

  Mapa2D.prototype.centrar = function (x, z, escalaMin) {
    this.cx = x; this.cz = z;
    if (escalaMin && this.escala < escalaMin) this.escala = escalaMin;
    this.pedir();
  };

  Mapa2D.prototype.px = function (x, z) { return [this.w / 2 + (x - this.cx) * this.escala, this.h / 2 - (z - this.cz) * this.escala]; };
  Mapa2D.prototype.pedir = function () {
    var m = this;
    if (m._pendiente) return;
    m._pendiente = true;
    requestAnimationFrame(function () { m._pendiente = false; m.pintar(); });
  };

  function etiqueta(c, txt, x, y, col, negrita) {
    c.font = (negrita ? '600 ' : '') + '12px system-ui, sans-serif';
    c.lineWidth = 3; c.strokeStyle = 'rgba(20,17,15,.85)'; c.lineJoin = 'round';
    c.strokeText(txt, x, y); c.fillStyle = col; c.fillText(txt, x, y);
  }

  Mapa2D.prototype.pintar = function () {
    var m = this, c = m.ctx, i;
    c.setTransform(m.dpr, 0, 0, m.dpr, 0, 0);
    c.fillStyle = '#14110f'; c.fillRect(0, 0, m.w, m.h);
    if (m.fondo && m.limites) {
      var a = m.px(m.limites.x0, m.limites.z1), b = m.px(m.limites.x1, m.limites.z0);
      c.imageSmoothingEnabled = m.escala < 0.2;   // de cerca, los cuadros del terreno limpios
      c.drawImage(m.fondo, a[0], a[1], b[0] - a[0], b[1] - a[1]);
    }
    // Portales: solo con nombre cuando hay sitio.
    c.textAlign = 'left'; c.textBaseline = 'middle';
    for (i = 0; i < m.portales.length; i++) {
      var p = m.portales[i], q = m.px(p.x, p.z);
      if (q[0] < -20 || q[1] < -20 || q[0] > m.w + 20 || q[1] > m.h + 20) continue;
      c.fillStyle = '#b48ad8'; c.strokeStyle = '#14110f'; c.lineWidth = 1.5;
      c.beginPath(); c.rect(q[0] - 3.5, q[1] - 3.5, 7, 7); c.fill(); c.stroke();
      if (m.escala > 0.12) etiqueta(c, p.nombre, q[0] + 8, q[1], '#d9c7ee', false);
    }
    // Rutas.
    var nombres = Object.keys(m.rutas);
    for (i = 0; i < nombres.length; i++) {
      var pts = m.rutas[nombres[i]], col = color(nombres[i]);
      if (!pts || !pts.length) continue;
      var ref = Math.min(m.hasta, m._ahora || Infinity);
      if (!isFinite(ref)) ref = pts[pts.length - 1][0];
      for (var paso = 0; paso < 2; paso++) {       // primero todo tenue, luego lo reciente marcado
        c.beginPath(); c.lineJoin = 'round'; c.lineCap = 'round';
        var abierto = false;
        for (var k = 0; k < pts.length; k++) {
          var pt = pts[k];
          if (pt[0] > m.hasta) break;
          if (paso === 1 && pt[0] < ref - m.cola) { abierto = false; continue; }
          var s = m.px(pt[1], pt[2]);
          if (!abierto || pt[3]) { c.moveTo(s[0], s[1]); abierto = true; } else c.lineTo(s[0], s[1]);
        }
        c.strokeStyle = col; c.globalAlpha = paso === 0 ? 0.35 : 0.95; c.lineWidth = paso === 0 ? 1.5 : 2.5;
        c.stroke();
      }
      c.globalAlpha = 1;
    }
    // Tumbas del día.
    for (i = 0; i < m.tumbas.length; i++) {
      var t = m.tumbas[i], tq = m.px(t.x, t.z);
      c.font = '15px system-ui, sans-serif'; c.textAlign = 'center';
      c.fillText('💀', tq[0], tq[1]);
      c.textAlign = 'left';
      if (m.escala > 0.08) etiqueta(c, t.nombre, tq[0] + 11, tq[1], '#efe6da', false);
    }
    // Vikingos.
    for (i = 0; i < m.vikingos.length; i++) {
      var v = m.vikingos[i], vq = m.px(v.x, v.z), vc = color(v.n);
      c.beginPath(); c.arc(vq[0], vq[1], 7, 0, 6.2832);
      c.fillStyle = vc; c.globalAlpha = v.oculto ? 0.55 : 1; c.fill();
      c.lineWidth = 2; c.strokeStyle = '#14110f'; c.stroke();
      c.beginPath(); c.arc(vq[0], vq[1], 10.5, 0, 6.2832); c.lineWidth = 1.5; c.strokeStyle = vc; c.stroke();
      c.globalAlpha = 1;
      etiqueta(c, v.n + (v.oculto ? ' (oculto)' : ''), vq[0] + 15, vq[1], '#efe6da', true);
    }
    // Escala y norte.
    var metros = [50, 100, 250, 500, 1000, 2500, 5000], largo = metros[0];
    for (i = 0; i < metros.length; i++) if (metros[i] * m.escala <= 140) largo = metros[i];
    var lx = 14, ly = m.h - 16, lw = largo * m.escala;
    c.strokeStyle = '#efe6da'; c.lineWidth = 2; c.beginPath(); c.moveTo(lx, ly - 5); c.lineTo(lx, ly); c.lineTo(lx + lw, ly); c.lineTo(lx + lw, ly - 5); c.stroke();
    etiqueta(c, largo >= 1000 ? (largo / 1000).toString().replace('.', ',') + ' km' : largo + ' m', lx + lw + 8, ly - 3, '#efe6da', false);
    c.textAlign = 'center'; etiqueta(c, 'N', m.w - 20, 28, '#efe6da', true);
    c.beginPath(); c.moveTo(m.w - 20, 8); c.lineTo(m.w - 24, 17); c.lineTo(m.w - 16, 17); c.closePath(); c.fillStyle = '#e0a54a'; c.fill();
    c.textAlign = 'left';
  };

  Mapa2D.prototype._zoom = function (factor, sx, sy) {
    var m = this, antes = m.escala;
    m.escala = Math.max(0.004, Math.min(4, m.escala * factor));
    var f = m.escala / antes;
    // El punto bajo el cursor se queda donde estaba.
    m.cx += (sx - m.w / 2) / antes * (1 - 1 / f); m.cz -= (sy - m.h / 2) / antes * (1 - 1 / f);
    m._tocado = true; m.pedir();
  };

  Mapa2D.prototype._eventos = function () {
    var m = this, l = m.lienzo;
    l.style.touchAction = 'none';
    function pos(e) { var r = l.getBoundingClientRect(); return [e.clientX - r.left, e.clientY - r.top]; }
    l.addEventListener('wheel', function (e) { e.preventDefault(); var p = pos(e); m._zoom(e.deltaY < 0 ? 1.2 : 1 / 1.2, p[0], p[1]); if (m.alMover) m.alMover(true); }, { passive: false });
    l.addEventListener('pointerdown', function (e) { l.setPointerCapture(e.pointerId); m._dedos[e.pointerId] = pos(e); });
    l.addEventListener('pointermove', function (e) {
      var antes = m._dedos[e.pointerId]; if (!antes) return;
      var ahora = pos(e), ids = Object.keys(m._dedos);
      if (ids.length === 1) {
        m.cx -= (ahora[0] - antes[0]) / m.escala; m.cz += (ahora[1] - antes[1]) / m.escala;
        m._tocado = true; if (m.alMover) m.alMover(false);
      } else if (ids.length === 2) {
        var otro = m._dedos[ids[0] === String(e.pointerId) ? ids[1] : ids[0]];
        var d0 = Math.hypot(antes[0] - otro[0], antes[1] - otro[1]), d1 = Math.hypot(ahora[0] - otro[0], ahora[1] - otro[1]);
        if (d0 > 5) m._zoom(d1 / d0, (ahora[0] + otro[0]) / 2, (ahora[1] + otro[1]) / 2);
        if (m.alMover) m.alMover(true);
      }
      m._dedos[e.pointerId] = ahora; m.pedir();
    });
    function fuera(e) { delete m._dedos[e.pointerId]; }
    l.addEventListener('pointerup', fuera); l.addEventListener('pointercancel', fuera);
    l.addEventListener('dblclick', function (e) { var p = pos(e); m._zoom(1.8, p[0], p[1]); });
  };

  window.Mapa2D = Mapa2D;
})();
