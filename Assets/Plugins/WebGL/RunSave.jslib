// Забег на случай закрытой вкладки (GameRunSave.cs): снимок держим в памяти и в localStorage,
// на pagehide (cmd+W, закрытие, переход) отправляем маяком. Сервер засчитывает забег по номеру один раз.
// У каждой вкладки свой ключ «ms.run.<вкладка>» и пульс раз в 5 с: чужой снимок забираем, только когда пульс
// замолк дольше 3 минут (вкладку закрыли или убили) — иначе вторая вкладка оборвала бы забег первой.
mergeInto(LibraryManager.library, {
  BrowserRunSnap: function (urlPtr, jsonPtr, tokPtr) {
    var url = UTF8ToString(urlPtr), json = UTF8ToString(jsonPtr), tok = UTF8ToString(tokPtr);
    if (!window.__msTab) window.__msTab = Math.random().toString(36).slice(2) + Date.now().toString(36);
    var key = "ms.run." + window.__msTab;
    var save = function () {
      try {
        var r = window.__msRun;
        if (r) localStorage.setItem(key, JSON.stringify({ at: Date.now(), json: r.json }));
        else localStorage.removeItem(key);
      } catch (e) {}
    };
    window.__msRun = json ? { url: url, json: json, tok: tok } : null;
    save();
    if (window.__msRunHooked) return;
    window.__msRunHooked = true;
    setInterval(save, 5000);
    window.addEventListener("pagehide", function () {
      var r = window.__msRun;
      if (!r || !r.url || !navigator.sendBeacon) return;
      var body = r.json;
      // токен входа (сервер засчитывает забег только аккаунту) — только в отправку, не в сохранённый снимок
      if (r.tok) { try { var o = JSON.parse(body); o.t = r.tok; body = JSON.stringify(o); } catch (e) {} }
      try { navigator.sendBeacon(r.url, new Blob([body], { type: "text/plain" })); } catch (e) {}
    });
    // случайный cmd+W или перезагрузка посреди забега — браузер переспросит «Покинуть сайт?»
    window.addEventListener("beforeunload", function (e) {
      if (!window.__msRun) return;
      e.preventDefault();
      e.returnValue = "";
    });
  },
  // проверка вёрстки (GameUiHook.cs, только с ?ui=): следующий экран из window.__msUi, пустая строка — нет
  BrowserUiNext: function () {
    var s = window.__msUi || "";
    window.__msUi = "";
    var n = lengthBytesUTF8(s) + 1, p = _malloc(n);
    stringToUTF8(s, p, n);
    return p;
  },
  // высота окна в CSS-пикселях: телефон в альбомной ниже 560 (холст рендерится с плотностью до 1,5 — по нему не понять)
  BrowserCssHeight: function () { return window.innerHeight || 0; },
  // итог забега — поделиться: на телефоне системное меню, на компьютере текст в буфер обмена
  BrowserShareText: function (textPtr) {
    var text = UTF8ToString(textPtr);
    var fallback = function () { window.prompt("Скопируй и отправь друзьям", text); };
    if (navigator.share && /Android|iPhone|iPad|Mobile/i.test(navigator.userAgent)) {
      navigator.share({ title: "Magic Survivors", text: text }).catch(function () {});
    } else if (navigator.clipboard && window.isSecureContext) {
      navigator.clipboard.writeText(text).catch(fallback);
    } else fallback();
  },
  // свой незаконченный забег (сцена перезагрузилась посреди него) и снимки умерших вкладок; строки через \n
  BrowserRunTake: function () {
    var out = [];
    if (window.__msRun) out.push(window.__msRun.json);
    window.__msRun = null;
    try {
      var own = window.__msTab ? "ms.run." + window.__msTab : "";
      var now = Date.now();
      for (var i = localStorage.length - 1; i >= 0; i--) {
        var k = localStorage.key(i);
        if (!k || k.indexOf("ms.run") !== 0) continue;
        if (k === own) { localStorage.removeItem(k); continue; }
        var v = null;
        try { v = JSON.parse(localStorage.getItem(k)); } catch (e) {}
        if (k === "ms.run") { localStorage.removeItem(k); continue; }   // старый формат первой сборки
        if (!v || !v.json) { localStorage.removeItem(k); continue; }
        // вкладка жива — её забег не трогаем. Фоновую вкладку Chrome будит раз в минуту, поэтому ждём три
        if (now - (v.at || 0) < 180000) continue;
        out.push(v.json);
        localStorage.removeItem(k);
      }
    } catch (e) {}
    var s = out.join("\n");
    var n = lengthBytesUTF8(s) + 1;
    var p = _malloc(n);
    stringToUTF8(s, p, n);
    return p;
  }
});
