// Кооператив в браузере: WebSocket к server/coop.py. Unity опрашивает очередь каждый кадр (без колбэков в C#).
// Плюс «поделиться ссылкой»: системное меню на телефоне, буфер обмена на ПК, иначе окно с ссылкой для копирования.
mergeInto(LibraryManager.library, {
  CoopOpen: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    var c = window.msCoop || (window.msCoop = {});
    if (c.ws) { try { c.ws.onclose = null; c.ws.close(); } catch (e) {} }
    c.q = []; c.state = 0;
    try {
      var ws = new WebSocket(url);
      ws.binaryType = "arraybuffer";
      // события принимаем только от текущего сокета: запоздалая ошибка старого не должна ронять новый
      ws.onopen = function () { if (c.ws === ws) c.state = 1; };
      ws.onclose = function () { if (c.ws === ws) c.state = 3; };
      ws.onerror = function () { if (c.ws === ws) c.state = 3; };
      ws.onmessage = function (e) { if (c.ws === ws && e.data instanceof ArrayBuffer) c.q.push(new Uint8Array(e.data)); };
      c.ws = ws;
    } catch (e) { c.state = 3; }
  },
  // 0 — соединяемся, 1 — открыто, 3 — закрыто или ошибка
  CoopState: function () { var c = window.msCoop; return c ? c.state : 3; },
  CoopSend: function (ptr, len) {
    var c = window.msCoop;
    if (c && c.ws && c.state == 1) c.ws.send(HEAPU8.slice(ptr, ptr + len));
  },
  // длина следующего сообщения или -1, если очередь пуста
  CoopPeek: function () { var c = window.msCoop; return c && c.q.length ? c.q[0].length : -1; },
  CoopRecv: function (ptr, max) {
    var m = window.msCoop.q.shift();
    var n = Math.min(m.length, max);
    HEAPU8.set(m.subarray(0, n), ptr);
    return n;
  },
  CoopClose: function () {
    var c = window.msCoop;
    if (c && c.ws) { try { c.ws.onclose = null; c.ws.close(); } catch (e) {} c.ws = null; c.state = 3; }
  },
  BrowserShare: function (textPtr, urlPtr) {
    var text = UTF8ToString(textPtr), url = UTF8ToString(urlPtr);
    var fallback = function () { window.prompt(text, url); };
    if (navigator.share && /Android|iPhone|iPad|Mobile/i.test(navigator.userAgent)) {
      navigator.share({ title: "Magic Survivors", text: text, url: url }).catch(function () {});
    } else if (navigator.clipboard && window.isSecureContext) {
      navigator.clipboard.writeText(url).catch(fallback);
    } else fallback();
  },
  // убрать ?room=… из адреса после входа, чтобы перезагрузка не тащила в старую комнату
  BrowserDropParam: function (namePtr) {
    try {
      var u = new URL(window.location.href);
      u.searchParams.delete(UTF8ToString(namePtr));
      window.history.replaceState(null, "", u.pathname + u.search + u.hash);
    } catch (e) {}
  }
});
