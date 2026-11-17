#!/bin/bash
# Выкладка Web-сборки на игровой сервер: Builds/Web -> /srv/magicsurvival/dist, твой сайт
# Собрать: в Unity меню Build/Web (или Builder.Queue("web") через unity CLI).
set -euo pipefail
cd "$(dirname "$0")/.."
HOST=${DEPLOY_HOST:?задай DEPLOY_HOST=user@host}
test -f Builds/Web/index.html || { echo "нет Builds/Web — сначала собери Web"; exit 1; }
ssh "$HOST" 'mkdir -p /srv/magicsurvival/dist'
# --delay-updates: новые файлы копятся рядом и подменяются разом в конце. Иначе при медленном канале сайт
# несколько минут отдаёт новые данные со старым кодом, и игра у игроков падает на загрузке.
rsync -az --delete --delay-updates Builds/Web/ "$HOST":/srv/magicsurvival/dist/
echo "ok: твой сайт"
