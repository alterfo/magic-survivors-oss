// Строка ввода для чата на телефоне: обычное поле браузера поверх игры (вверху экрана, клавиатура его не
// закрывает). Шрифт 16px — иначе iOS приближает страницу при фокусе. Игра опрашивает ChatInputPoll каждый кадр.
mergeInto(LibraryManager.library, {
  // mode: 0 — сообщение (кнопка «Отправить»), 1 — поле (имя, идея), 2 — цифры (код комнаты); init — текущее значение
  ChatInputOpen: function (hintPtr, max, initPtr, mode) {
    var st = window.msChat;
    if (!st) {
      st = window.msChat = { done: null };
      var wrap = st.wrap = document.createElement('div');
      wrap.style.cssText = 'position:fixed;left:0;right:0;top:0;z-index:1000;display:none;gap:8px;padding:8px;' +
        'box-sizing:border-box;background:rgba(8,6,12,.94);border-bottom:1px solid rgba(201,162,74,.5)';
      var el = st.el = document.createElement('input');
      el.type = 'text';
      el.autocomplete = 'off';
      el.setAttribute('enterkeyhint', 'send');
      el.style.cssText = 'flex:1;min-width:0;font:16px sans-serif;padding:10px 12px;border-radius:10px;' +
        'border:1px solid #c9a24a;background:#0b0910;color:#eee;outline:none';
      var btn = function (label, color) {
        var b = document.createElement('button');
        b.textContent = label;
        b.style.cssText = 'font:16px sans-serif;padding:0 16px;border-radius:10px;border:1px solid ' + color +
          ';background:#17121f;color:' + color;
        return b;
      };
      var ok = st.ok = btn('Готово', '#9fe8a8'), no = btn('✕', '#bbb');
      st.finish = function (text) {
        st.done = text;
        wrap.style.display = 'none';
        el.blur();
      };
      ok.onclick = function () { st.finish(el.value); };
      no.onclick = function () { st.finish(''); };
      // клавиши остаются в поле: игра не должна принять их за управление
      ['keydown', 'keyup', 'keypress'].forEach(function (name) {
        el.addEventListener(name, function (e) {
          e.stopPropagation();
          if (name === 'keydown' && e.key === 'Enter') st.finish(el.value);
          if (name === 'keydown' && e.key === 'Escape') st.finish('');
        });
      });
      wrap.appendChild(el); wrap.appendChild(ok); wrap.appendChild(no);
      document.body.appendChild(wrap);
    }
    st.done = null;
    st.el.maxLength = max;
    st.el.placeholder = UTF8ToString(hintPtr);
    st.el.value = UTF8ToString(initPtr);
    st.el.inputMode = mode === 2 ? 'numeric' : 'text';
    st.el.setAttribute('enterkeyhint', mode === 0 ? 'send' : 'done');
    st.ok.textContent = mode === 0 ? 'Отправить' : 'Готово';
    st.wrap.style.display = 'flex';
    st.el.focus();
  },

  // "" — поле ещё открыто; иначе первый символ служебный, дальше текст (пусто — отмена)
  ChatInputPoll: function () {
    var st = window.msChat, r = '';
    if (st && st.done !== null) { r = '\x01' + st.done; st.done = null; }
    var n = lengthBytesUTF8(r) + 1;
    var p = _malloc(n);
    stringToUTF8(r, p, n);
    return p;
  }
});
