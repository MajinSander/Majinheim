// Majinheim Web: el mundo explorado en 3D (2.7). Sin librerías: WebGL a mano.
// /api/terreno.bin da una rejilla con la altura y el bioma de lo explorado; aquí se levanta la malla, se colorea por bioma
// y se deja girar, acercar y sobrevolar. Encima van los nombres de los portales y, en directo, los vikingos (/api/vivo).
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var lienzo = $('mundo'), rotulos = $('rotulos');
  var dia = new URLSearchParams(location.search).get('dia');
  if (dia && !/^\d{4}-\d\d-\d\d$/.test(dia)) dia = null;
  var ALTO = 2, AGUA = 30;      // el relieve se exagera un poco para que se vea desde lejos
  // 1 Pradera, 2 Pantano, 3 Montaña, 4 Selva Negra, 5 Llanuras, 6 Tierras de ceniza, 7 Norte profundo, 9 Océano, 10 Tierras de niebla
  var BIOMA = [0x6d7a5a, 0x7ea04c, 0x5e5636, 0xd6dbe2, 0x2f4d2c, 0xc8b25a, 0x7c3b2c, 0xe7eef6, 0x2d5f86, 0x7ea04c, 0x6c6a86];

  function aviso(t) { var a = $('aviso'); a.textContent = t; a.hidden = false; $('sub').textContent = ''; }

  var gl = lienzo.getContext('webgl2', { antialias: true });
  if (!gl) { aviso('Este navegador no puede dibujar en 3D. Prueba con otro, o mira el mapa en vivo.'); return; }

  // ------------------------------------------------------------ cuentas con matrices (columnas, como en WebGL)
  function perspectiva(fov, asp, n, f) { var t = 1 / Math.tan(fov / 2); return [t / asp, 0, 0, 0, 0, t, 0, 0, 0, 0, (f + n) / (n - f), -1, 0, 0, 2 * f * n / (n - f), 0]; }
  function mirar(e, c) {
    var zx = e[0] - c[0], zy = e[1] - c[1], zz = e[2] - c[2], l = Math.hypot(zx, zy, zz); zx /= l; zy /= l; zz /= l;
    var xx = zz, xy = 0, xz = -zx; l = Math.hypot(xx, xz) || 1; xx /= l; xz /= l;   // arriba = (0,1,0)
    var yx = zy * xz - zz * xy, yy = zz * xx - zx * xz, yz = zx * xy - zy * xx;
    return [xx, yx, zx, 0, xy, yy, zy, 0, xz, yz, zz, 0, -(xx * e[0] + xy * e[1] + xz * e[2]), -(yx * e[0] + yy * e[1] + yz * e[2]), -(zx * e[0] + zy * e[1] + zz * e[2]), 1];
  }
  function por(a, b) {
    var o = new Array(16);
    for (var c = 0; c < 4; c++) for (var r = 0; r < 4; r++) o[c * 4 + r] = a[r] * b[c * 4] + a[4 + r] * b[c * 4 + 1] + a[8 + r] * b[c * 4 + 2] + a[12 + r] * b[c * 4 + 3];
    return o;
  }

  // ------------------------------------------------------------ programa
  function sombreador(tipo, txt) { var s = gl.createShader(tipo); gl.shaderSource(s, txt); gl.compileShader(s); if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s)); return s; }
  var prog = gl.createProgram();
  gl.attachShader(prog, sombreador(gl.VERTEX_SHADER,
    '#version 300 es\nin vec3 pos; in vec3 nor; in vec3 col; uniform mat4 mvp; uniform vec3 ojo; out vec3 vCol; out float vNiebla; out float vAgua;\n' +
    'void main(){ gl_Position = mvp * vec4(pos,1.0); float luz = 0.45 + 0.75 * max(dot(normalize(nor), normalize(vec3(-0.5,0.8,0.35))), 0.0);\n' +
    ' vAgua = nor.y > 1.5 ? 1.0 : 0.0; vCol = col * (vAgua > 0.5 ? 1.0 : luz); vNiebla = distance(pos, ojo); }'));
  gl.attachShader(prog, sombreador(gl.FRAGMENT_SHADER,
    '#version 300 es\nprecision mediump float; in vec3 vCol; in float vNiebla; in float vAgua; uniform float lejos; out vec4 o;\n' +
    'void main(){ float f = clamp((vNiebla - lejos * 0.55) / (lejos * 0.6), 0.0, 0.85); o = vec4(mix(vCol, vec3(0.078,0.067,0.059), f), 1.0); }'));
  gl.linkProgram(prog);
  if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) { aviso('No he podido preparar el 3D en este navegador.'); return; }
  gl.useProgram(prog);
  var uMvp = gl.getUniformLocation(prog, 'mvp'), uOjo = gl.getUniformLocation(prog, 'ojo'), uLejos = gl.getUniformLocation(prog, 'lejos');

  // ------------------------------------------------------------ terreno
  var T = null;            // { x0, z0, paso, w, h, alt, bio }
  var nIndices = 0;
  var cam = { tx: 0, tz: 0, giro: 0.6, inclinacion: 0.75, dist: 2000 };
  var limite = { x0: 0, x1: 1, z0: 0, z1: 1 };

  function altura(x, z) {
    if (!T) return AGUA;
    var i = Math.round((x - T.x0) / T.paso), j = Math.round((z - T.z0) / T.paso);
    if (i < 0 || j < 0 || i >= T.w || j >= T.h || T.bio[j * T.w + i] === 255) return AGUA;
    return Math.max(AGUA, T.alt[j * T.w + i]);
  }

  function levantar(buf) {
    var dv = new DataView(buf);
    if (buf.byteLength < 24 || String.fromCharCode(dv.getUint8(0), dv.getUint8(1), dv.getUint8(2), dv.getUint8(3)) !== 'MHT1') throw new Error('formato');
    var x0 = dv.getInt32(4, true), z0 = dv.getInt32(8, true), paso = dv.getInt32(12, true), w = dv.getInt32(16, true), h = dv.getInt32(20, true);
    var alt = new Uint8Array(buf, 24, w * h), bio = new Uint8Array(buf, 24 + w * h, w * h);
    T = { x0: x0, z0: z0, paso: paso, w: w, h: h, alt: alt, bio: bio };
    var idx = new Int32Array(w * h).fill(-1), n = 0, i, j, o;
    for (o = 0; o < w * h; o++) if (bio[o] !== 255) idx[o] = n++;
    var pos = new Float32Array(n * 3), nor = new Float32Array(n * 3), col = new Uint8Array(n * 3);
    var minI = w, maxI = 0, minJ = h, maxJ = 0;
    function hAt(i2, j2, def) { if (i2 < 0 || j2 < 0 || i2 >= w || j2 >= h) return def; var q = j2 * w + i2; return bio[q] === 255 ? def : Math.max(AGUA, alt[q]); }
    for (j = 0; j < h; j++) for (i = 0; i < w; i++) {
      o = j * w + i; var k = idx[o]; if (k < 0) continue;
      if (i < minI) minI = i; if (i > maxI) maxI = i; if (j < minJ) minJ = j; if (j > maxJ) maxJ = j;
      var a = alt[o], agua = a < AGUA, y = agua ? AGUA : a;
      pos[k * 3] = x0 + i * paso; pos[k * 3 + 1] = y * ALTO; pos[k * 3 + 2] = -(z0 + j * paso);
      var c;
      if (agua) {
        var hondo = Math.min(1, (AGUA - a) / 30);
        c = [0x4f + (0x1c - 0x4f) * hondo, 0x86 + (0x3d - 0x86) * hondo, 0xad + (0x5a - 0xad) * hondo];
        nor[k * 3] = 0; nor[k * 3 + 1] = 2; nor[k * 3 + 2] = 0;    // 2 = "esto es agua": sin sombras
      } else {
        var b = BIOMA[bio[o] < BIOMA.length ? bio[o] : 0];
        c = [(b >> 16) & 255, (b >> 8) & 255, b & 255];
        if (a < AGUA + 2) c = [(c[0] + 0xc9) / 2, (c[1] + 0xb9) / 2, (c[2] + 0x8a) / 2];   // playa
        else if (bio[o] === 3 && a < 95) c = [0x8a, 0x8c, 0x8f];                         // la falda de la montaña, roca sin nieve
        var hx = hAt(i - 1, j, y) - hAt(i + 1, j, y), hz = hAt(i, j - 1, y) - hAt(i, j + 1, y);
        var nx = hx * ALTO, ny = 2 * paso, nz = -hz * ALTO, l = Math.hypot(nx, ny, nz);
        nor[k * 3] = nx / l; nor[k * 3 + 1] = ny / l; nor[k * 3 + 2] = nz / l;
      }
      col[k * 3] = c[0]; col[k * 3 + 1] = c[1]; col[k * 3 + 2] = c[2];
    }
    var ind = new Uint32Array((w - 1) * (h - 1) * 6), m = 0;
    for (j = 0; j < h - 1; j++) for (i = 0; i < w - 1; i++) {
      var p = idx[j * w + i], q = idx[j * w + i + 1], r = idx[(j + 1) * w + i], s = idx[(j + 1) * w + i + 1];
      if (p < 0 || q < 0 || r < 0 || s < 0) continue;
      ind[m++] = p; ind[m++] = q; ind[m++] = r; ind[m++] = q; ind[m++] = s; ind[m++] = r;
    }
    nIndices = m;
    function atributo(nombre, datos, tipo, normalizado) {
      var b = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, b); gl.bufferData(gl.ARRAY_BUFFER, datos, gl.STATIC_DRAW);
      var loc = gl.getAttribLocation(prog, nombre); gl.enableVertexAttribArray(loc); gl.vertexAttribPointer(loc, 3, tipo, normalizado, 0, 0);
    }
    atributo('pos', pos, gl.FLOAT, false); atributo('nor', nor, gl.FLOAT, false); atributo('col', col, gl.UNSIGNED_BYTE, true);
    var e = gl.createBuffer(); gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, e); gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, ind.subarray(0, m), gl.STATIC_DRAW);
    limite = { x0: x0 + minI * paso, x1: x0 + maxI * paso, z0: z0 + minJ * paso, z1: z0 + maxJ * paso };
    verTodo();
    return n;
  }

  function verTodo() {
    cam.tx = (limite.x0 + limite.x1) / 2; cam.tz = (limite.z0 + limite.z1) / 2;
    var r = lienzo.getBoundingClientRect(), prop = Math.min(1, (r.width || 1) / (r.height || 1));
    cam.dist = Math.max(600, Math.max((limite.x1 - limite.x0) / prop, limite.z1 - limite.z0) * 1.15);
    cam.giro = 0.6; cam.inclinacion = 0.75;
  }

  // ------------------------------------------------------------ rótulos (portales y vikingos)
  var portales = [], vikingos = [], verNombres = true, etiquetas = [];
  function rotular(mvp, w, h) {
    var todo = [];
    vikingos.forEach(function (v) { todo.push({ x: v.x, z: v.z, t: '● ' + v.n, cls: 'vikingo', color: Mapa2D.color(v.n), fijo: true }); });
    if (verNombres) portales.forEach(function (p) { todo.push({ x: p.x, z: p.z, t: p.nombre, cls: 'portal' }); });
    var usados = 0;
    for (var i = 0; i < todo.length; i++) {
      var p = todo[i], y = altura(p.x, p.z) * ALTO + 6, X = p.x, Z = -p.z;
      var cw = mvp[3] * X + mvp[7] * y + mvp[11] * Z + mvp[15];
      if (cw <= 0) continue;
      var sx = (mvp[0] * X + mvp[4] * y + mvp[8] * Z + mvp[12]) / cw, sy = (mvp[1] * X + mvp[5] * y + mvp[9] * Z + mvp[13]) / cw;
      if (sx < -1 || sx > 1 || sy < -1 || sy > 1) continue;
      if (!p.fijo && cw > cam.dist * 2.2) continue;         // los portales lejanos, sin nombre: si no, no se ve nada
      if (usados >= 60) break;
      var e = etiquetas[usados] || (etiquetas[usados] = rotulos.appendChild(document.createElement('span')));
      usados++;
      if (e.textContent !== p.t) e.textContent = p.t;
      e.className = p.cls; e.style.color = p.color || ''; e.hidden = false;
      e.style.left = ((sx + 1) / 2 * w) + 'px'; e.style.top = ((1 - sy) / 2 * h) + 'px';
    }
    for (var k = usados; k < etiquetas.length; k++) etiquetas[k].hidden = true;
  }

  // ------------------------------------------------------------ dibujo
  var volando = false, antes = 0;
  function cuadro(ahora) {
    var dt = Math.min(0.1, (ahora - antes) / 1000); antes = ahora;
    if (volando) cam.giro += dt * 0.12;
    var r = lienzo.getBoundingClientRect(), dpr = Math.min(2, window.devicePixelRatio || 1);
    var w = Math.max(50, r.width), h = Math.max(50, r.height);
    if (lienzo.width !== Math.round(w * dpr) || lienzo.height !== Math.round(h * dpr)) { lienzo.width = Math.round(w * dpr); lienzo.height = Math.round(h * dpr); }
    gl.viewport(0, 0, lienzo.width, lienzo.height);
    gl.clearColor(0.078, 0.067, 0.059, 1); gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    if (nIndices) {
      var ty = altura(cam.tx, cam.tz) * ALTO;
      var ch = Math.cos(cam.inclinacion) * cam.dist, ojo = [cam.tx + Math.sin(cam.giro) * ch, ty + Math.sin(cam.inclinacion) * cam.dist, -cam.tz + Math.cos(cam.giro) * ch];
      var lejos = cam.dist * 6 + 3000;
      var mvp = por(perspectiva(0.8, w / h, Math.max(2, cam.dist / 200), lejos), mirar(ojo, [cam.tx, ty, -cam.tz]));
      gl.enable(gl.DEPTH_TEST);
      gl.uniformMatrix4fv(uMvp, false, new Float32Array(mvp)); gl.uniform3fv(uOjo, new Float32Array(ojo)); gl.uniform1f(uLejos, lejos);
      gl.drawElements(gl.TRIANGLES, nIndices, gl.UNSIGNED_INT, 0);
      rotular(mvp, w, h);
    }
    requestAnimationFrame(cuadro);
  }

  // ------------------------------------------------------------ manejo
  lienzo.style.touchAction = 'none';
  var dedos = {};
  function mover(dx, dy) {         // desplazar el punto que se mira, en el plano del suelo
    var k = cam.dist / 700;
    cam.tx -= (Math.cos(cam.giro) * dx + Math.sin(cam.giro) * dy) * k;
    cam.tz -= (Math.sin(cam.giro) * dx - Math.cos(cam.giro) * dy) * k;
    cam.tx = Math.max(limite.x0 - 500, Math.min(limite.x1 + 500, cam.tx)); cam.tz = Math.max(limite.z0 - 500, Math.min(limite.z1 + 500, cam.tz));
  }
  function acercar(f) { cam.dist = Math.max(60, Math.min(40000, cam.dist * f)); }
  lienzo.addEventListener('contextmenu', function (e) { e.preventDefault(); });
  lienzo.addEventListener('wheel', function (e) { e.preventDefault(); acercar(e.deltaY < 0 ? 1 / 1.15 : 1.15); }, { passive: false });
  lienzo.addEventListener('pointerdown', function (e) { lienzo.setPointerCapture(e.pointerId); dedos[e.pointerId] = { x: e.clientX, y: e.clientY, mover: e.button === 2 || e.shiftKey }; dejarDeVolar(); });
  lienzo.addEventListener('pointermove', function (e) {
    var d = dedos[e.pointerId]; if (!d) return;
    var dx = e.clientX - d.x, dy = e.clientY - d.y, ids = Object.keys(dedos);
    if (ids.length === 1) {
      if (d.mover) mover(dx, dy);
      else { cam.giro -= dx * 0.006; cam.inclinacion = Math.max(0.12, Math.min(1.5, cam.inclinacion + dy * 0.005)); }
    } else if (ids.length === 2) {
      var otro = dedos[ids[0] === String(e.pointerId) ? ids[1] : ids[0]];
      var d0 = Math.hypot(d.x - otro.x, d.y - otro.y), d1 = Math.hypot(e.clientX - otro.x, e.clientY - otro.y);
      if (d0 > 5) acercar(d0 / d1);
      mover(dx / 2, dy / 2);
    }
    d.x = e.clientX; d.y = e.clientY;
  });
  function soltar(e) { delete dedos[e.pointerId]; }
  lienzo.addEventListener('pointerup', soltar); lienzo.addEventListener('pointercancel', soltar);
  function dejarDeVolar() { if (volando) { volando = false; $('b-vuelo').textContent = '▶ Sobrevolar'; } }
  $('b-vuelo').onclick = function () { volando = !volando; this.textContent = volando ? '■ Parar' : '▶ Sobrevolar'; if (volando) cam.inclinacion = Math.min(cam.inclinacion, 0.55); };
  $('b-todo').onclick = function () { dejarDeVolar(); verTodo(); };
  $('b-nombres').onclick = function () { verNombres = !verNombres; this.textContent = verNombres ? 'Ocultar nombres' : 'Ver nombres'; };
  if (matchMedia('(pointer: coarse)').matches) $('ayuda').textContent = 'Un dedo para girar · dos dedos para moverte y acercar';

  // ------------------------------------------------------------ datos
  fetch('/api/terreno.bin' + (dia ? '?dia=' + dia : ''), { cache: 'no-store' })
    .then(function (r) {
      if (!r.ok) return r.json().then(function (d) { throw new Error(d.error || 'error'); });
      return r.arrayBuffer();
    })
    .then(function (buf) {
      var n = levantar(buf);
      var km2 = n * T.paso * T.paso / 1e6;
      $('sub').textContent = (dia ? 'El mundo como estaba el ' + new Date(dia + 'T12:00:00').toLocaleDateString('es-ES', { day: 'numeric', month: 'long', year: 'numeric' }) + ' · ' : '') +
        km2.toFixed(1).replace('.', ',') + ' km² explorados';
      requestAnimationFrame(cuadro);
      if (dia) fetch('/api/historia/dia?dia=' + dia).then(function (r) { return r.json(); }).then(function (d) { portales = d.portales || []; }).catch(function () { });
      else directo(true);
    })
    .catch(function (e) { aviso(e && e.message && e.message !== 'error' && e.message !== 'formato' && e.message !== 'Failed to fetch' ? e.message : 'No he podido cargar el terreno. Prueba a recargar la página.'); });

  function directo(primera) {
    fetch('/api/vivo?desde=' + (primera ? 0 : Math.floor(Date.now() / 1000)), { cache: 'no-store' }).then(function (r) { return r.json(); })
      .then(function (d) { if (d.mapa) portales = d.mapa.portales || []; vikingos = d.jugadores || []; })
      .catch(function () { })
      .then(function () { setTimeout(directo, document.hidden ? 15000 : 3000); });
  }
})();
