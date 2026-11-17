// Вход через хаб neuraldeep: уход на хаб в этой же вкладке и уборка одноразового билета из адреса после возврата.
mergeInto(LibraryManager.library, {
  BrowserGo: function (urlPtr) {
    window.location.href = UTF8ToString(urlPtr);
  },
  BrowserDropLogin: function () {
    try {
      var u = new URL(window.location.href);
      u.searchParams.delete("ms_login");
      window.history.replaceState(null, "", u.pathname + u.search + u.hash);
    } catch (e) {}
  }
});
