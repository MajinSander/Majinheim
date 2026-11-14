// Majinheim Web: página de ayuda. El contenido sale de /ayuda.json (lo mismo que usa la IA para contestar).
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var todo = [];

  function el(tag, cls, text) { var n = document.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; }
  function plano(s) { return (s || '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase(); }

  // Texto con los enlaces https:// convertidos en enlaces (sin HTML: todo con textContent).
  function conEnlaces(nodo, texto) {
    var partes = texto.split(/(https:\/\/[^\s]+)/);
    partes.forEach(function (p, i) {
      if (i % 2 === 1) {
        var url = p.replace(/[.,)]+$/, ''), resto = p.slice(url.length);
        var a = el('a', null, url); a.href = url; a.rel = 'noopener noreferrer'; a.target = '_blank';
        nodo.appendChild(a);
        if (resto) nodo.appendChild(document.createTextNode(resto));
      } else if (p) nodo.appendChild(document.createTextNode(p));
    });
    return nodo;
  }

  function pintar(datos) {
    var cont = $('secciones');
    (datos.secciones || []).forEach(function (s) {
      var sec = el('section', 'tarjeta ancha');
      sec.appendChild(el('h2', null, s.titulo));
      var items = [];
      (s.preguntas || []).forEach(function (q) {
        var d = el('details', 'ayuda-pregunta');
        d.appendChild(el('summary', null, q.p));
        var r = q.r || [];
        if (q.pasos) {
          var ol = el('ol');
          r.forEach(function (t) { ol.appendChild(conEnlaces(el('li'), t)); });
          d.appendChild(ol);
        } else r.forEach(function (t) { d.appendChild(conEnlaces(el('p'), t)); });
        sec.appendChild(d);
        items.push({ nodo: d, texto: plano(q.p + ' ' + r.join(' ') + ' ' + (q.claves || '')) });
      });
      cont.appendChild(sec);
      todo.push({ nodo: sec, items: items });
    });
  }

  function filtrar() {
    var palabras = plano($('buscar').value).split(/[^a-z0-9]+/).filter(function (w) { return w.length >= 3; });
    var alguno = false;
    todo.forEach(function (s) {
      var visibles = 0;
      s.items.forEach(function (it) {
        var ok = !palabras.length || palabras.some(function (w) { return it.texto.indexOf(w) >= 0; });
        it.nodo.hidden = !ok;
        if (palabras.length) it.nodo.open = ok;
        if (ok) visibles++;
      });
      s.nodo.hidden = visibles === 0;
      if (visibles) alguno = true;
    });
    $('sin-resultados').hidden = alguno;
  }

  $('form-buscar').addEventListener('submit', function (ev) { ev.preventDefault(); filtrar(); });
  $('buscar').addEventListener('input', filtrar);

  fetch('/ayuda.json', { cache: 'no-store' }).then(function (r) { return r.json(); }).then(function (d) {
    pintar(d);
    if (location.hash.length > 1) { $('buscar').value = decodeURIComponent(location.hash.slice(1)); filtrar(); }
  }).catch(function () {
    $('secciones').appendChild(el('p', 'aviso', 'No se ha podido cargar la ayuda. Prueba a recargar la página.'));
  });
})();
