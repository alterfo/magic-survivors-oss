// Ввод текста в браузере: на телефоне IMGUI-поле не вызывает клавиатуру, а window.prompt — вызывает.
mergeInto(LibraryManager.library, {
  BrowserPrompt: function (titlePtr) {
    var r = window.prompt(UTF8ToString(titlePtr), "");
    if (r === null) r = "";
    var n = lengthBytesUTF8(r) + 1;
    var p = _malloc(n);
    stringToUTF8(r, p, n);
    return p;
  }
});
