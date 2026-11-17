# Сборка и выкладка

Как собрать игру и поднять свой сервер. Официальный сервер — https://magicsurvival.neuraldeep.ru; ниже `game.example.com` — твой домен, `/srv/magicsurvival` — каталог на сервере.

**Сборка** — `Assets/Editor/Builder.cs`, меню Build/… в редакторе или из CLI:

```
Unity -batchmode -nographics -projectPath . -executeMethod Builder.Cli -builds web,mac,win -logFile Logs/batch-build.log
```

Итог — в `Builds/status.txt` (`done` в конце). Результаты: `Builds/Web`, `Builds/Mac/MagicSurvivors.app`, `Builds/Windows/MagicSurvivors.exe`. Web ≈ 29 МБ, gzip + decompression fallback, шаблон страницы `Assets/WebGLTemplates/Full` (весь экран, dpr ≤ 1.5, экран загрузки).

Android не собирается: нет JDK и Android SDK (модуль `AndroidPlayer` без `OpenJDK`/`SDK`).

**Скорость кода в браузере.** `Builder.Web` собирает wasm с оптимизацией по скорости (`WasmCodeOptimization.RuntimeSpeed`, по умолчанию у Unity стоит «быстрая сборка») и с SIMD (`wasm2023`: Chrome 91+, Firefox 89+, Safari 16.4+). В браузере анимацию и скиннинг сотен мобов считает одно ядро процессора, видеокарта почти простаивает — поэтому скорость кода важнее всего остального. Телеметрия забега несёт `gpu` (чем рисует браузер; «SwiftShader» или «Basic Render» — программный рендер, у игрока выключено аппаратное ускорение), `os` и `batches` (вызовов отрисовки в кадре).

**Выкладка браузерной версии**: `deploy/deploy.sh` — rsync (`--delay-updates`: файлы подменяются разом в конце) `Builds/Web` → `$DEPLOY_HOST:/srv/magicsurvival/dist` (`DEPLOY_HOST=user@host bash deploy/deploy.sh`).

- vhost: `deploy/nginx-magicsurvival.conf` → `/etc/nginx/sites-available/magicsurvival`; `.unityweb` отдаются с `Content-Encoding: gzip`, wasm — `application/wasm`; без SPA-фоллбэка.
- TLS: `certbot certonly --webroot -w /var/www/html -d game.example.com`.


**Сервер идей** (`server/ideas.py`, stdlib + SQLite): systemd `magicsurvival-ideas` (`server/magicsurvival-ideas.service`), слушает 127.0.0.1:8797, nginx проксирует `/api/`. База — `/srv/magicsurvival/data/ideas.db`, токен — `/srv/magicsurvival/ideas.env`. Обновить: `scp server/ideas.py $DEPLOY_HOST:/srv/magicsurvival/server/ && ssh $DEPLOY_HOST systemctl restart magicsurvival-ideas`.

**Сборка без редактора**: `Unity -batchmode -nographics -projectPath . -executeMethod Builder.Cli -builds web -logFile Logs/batch-build.log`.

**Телеметрия и статистика**: клиент шлёт срез забега раз в минуту и финал (`/api/telemetry`), лидерборд — `/api/hello`, `/api/run`, `/api/leaders`. Сводка: `https://game.example.com/api/stats`; подробный разбор для баланса — `ssh $DEPLOY_HOST 'sudo -u www-data python3 /srv/magicsurvival/server/ideas.py report 15'` (забеги от 15 минут).

**Аккаунт через хаб (необязательно)** (`server/account.py`, клиент `Assets/Scripts/GameAccount.cs` + `Assets/Plugins/WebGL/Account.jslib`): вход через OAuth2-провайдер (`/oauth/authorize|token|userinfo`, code + PKCE S256, scope `profile`); официальный сервер использует хаб neuraldeep. Переменные `HUB_URL`, `HUB_CLIENT_ID`, `HUB_CLIENT_SECRET`, `GAME_URL` (redirect — `$GAME_URL/api/login/callback`). Без `HUB_CLIENT_SECRET` вход выключен: `/api/login/start` отвечает 503, `LOGIN_REQUIRED` не действует, играть можно гостем. Браузер: кнопка → `/api/login/start?mode=web` → хаб → callback → `/?ms_login=<билет>` → `POST /api/login {ticket, voter, nick}` → игровой токен в PlayerPrefs. Desktop: `mode=app`, хаб показывает 8-значный код, его вводят в игре. Прогресс устройства (золото, покупки, рекорды, наряд, настройки) при входе вливается в аккаунт (`account.merge_wallet`, `server/prefs.py`).

**Админка онлайна**: `https://game.example.com/api/admin?key=<ADMIN_TOKEN>` (токен — `/srv/magicsurvival/ideas.env` на сервере). Код — `server/presence.py` (в `ideas.py` только роутинг), страница — `server/admin.html`, клиент — `Assets/Scripts/GamePresence.cs`, данные — `/api/admin/online`, обновление раз в 10 с. Онлайн = пинги клиента (`POST /api/ping {voter, state: menu|play}`, держится 75 с) ∪ забеги со срезом телеметрии свежее 150 с, поэтому забеги видно и в старых сборках без пинга. Раз в минуту число пишется в таблицу `online` (хранится 7 дней) для графика за сутки. Выкладка: `scp server/ideas.py server/presence.py server/admin.html $DEPLOY_HOST:/srv/magicsurvival/server/ && ssh $DEPLOY_HOST systemctl restart magicsurvival-ideas`.

**Павшие маги** (`server/fallen.py`, в `ideas.py` только роутинг): маг, погибший в забеге, приходит к другим игрокам элитным врагом. `POST /api/fallen/add {voter, nick, hero, hat, robe, staff, glow, element, spells, lvl, secs, map, diff}` — слепок после смерти (забег 180–1500 с, не чаще раза в 60 с, хранится 5 последних на игрока); `POST /api/fallen/get {voter, map}` — чужой павший не старше 30 дней, которого игрок ещё не упокоил (сначала с той же карты, потом кого реже упокаивали; `id: 0` — некого дать), выдача пишется в `fallen_given` и живёт 2 часа; `POST /api/fallen/slain {voter, id}` — только по выданному id, один раз на пару и не больше 10 в сутки: победителю +20 золота и трофеи на 1/3/10 упокоенных (`cos.glow_soul`, `cos.robe_ghost`, `cos.hat_fallen` — в ценах `wallet.py` их нет, купить нельзя), хозяину +30 золота (не больше 5 выплат в сутки, дальше новость без золота); `POST /api/fallen/news {voter}` — непрочитанные «твоего мага упокоил X», до 10, отдаются один раз. Золото и вещи идут через `wallet.grant_gold` / `wallet.grant_item` в кошелёк устройства или аккаунта; при входе через хаб таблицы `fallen*` переезжают в профиль аккаунта (`account.merge_fallen`). Выкладка: `scp server/fallen.py server/ideas.py server/wallet.py server/account.py $DEPLOY_HOST:/srv/magicsurvival/server/ && ssh $DEPLOY_HOST systemctl restart magicsurvival-ideas`.

**Кооператив** (`server/coop.py`, подробно — [coop.md](coop.md)): systemd `magicsurvival-coop` (`server/magicsurvival-coop.service`), 127.0.0.1:8798, nginx `location = /coop` с Upgrade (в `deploy/nginx-magicsurvival.conf`). Выкладка: `scp server/coop.py $DEPLOY_HOST:/srv/magicsurvival/server/ && ssh $DEPLOY_HOST systemctl restart magicsurvival-coop`.

Не пересобирать `Builds/Web`, пока идёт выкладка: rsync читает папку минутами. Выкладывать с копии сборки или дождаться конца.

**PWA.** В шаблоне `Assets/WebGLTemplates/Full` лежат `manifest.json` и значки `icon-192.png`, `icon-512.png` — игру можно поставить на главный экран, она открывается на весь экран. Офлайн-кэша (service worker) нет: игра сетевая.

## Объявления и вход

**Объявление всем игрокам** — окно в меню и в городе, «Понятно» закрывает его навсегда (по `id`):
`ssh $DEPLOY_HOST "cd /srv/magicsurvival && sudo -u www-data python3 server/news.py 'текст до 600 символов' [часов]"`.
Список — `--list`, снять — `--off <id>`. Клиент берёт объявления из `GET /api/news` при запуске и раз в две минуты.

**Обязательный вход** включается, когда задан `HUB_CLIENT_SECRET` и `LOGIN_REQUIRED=1` (по умолчанию): без аккаунта меню и город закрыты экраном входа, сервер
засчитывает забег и павшего только по токену (`X-Ms-Token`, у маяка закрытой вкладки — поле `t`). Если хаб лёг и
никто не может войти — `LOGIN_REQUIRED=0` в `/srv/magicsurvival/ideas.env` и `systemctl restart magicsurvival-ideas`,
пересборка не нужна: флаг приходит клиенту в `/api/news`.


## Свой сервер за 5 минут

```
mkdir -p data && cp server/.env.example server/.env   # поправь значения
set -a; . server/.env; set +a
python3 server/ideas.py &                                  # API: 127.0.0.1:8797
python3 server/coop.py &                                   # кооператив и город: 127.0.0.1:8798
DEV_API=http://127.0.0.1:8797 python3 tools/devserver.py 8088   # сайт из Builds/Web + прокси на API
```
Открой `http://127.0.0.1:8088/?ws=ws://127.0.0.1:8798/coop`. Для прода — nginx из `deploy/`, systemd-юниты из `server/*.service`.
