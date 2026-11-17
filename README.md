# Magic Survivors

Браузерная survivors-like игра на Unity 6: маг отбивается от орды нежити, качает магию, собирает снаряжение, ходит в данжи с пати до четырёх человек, а раз в день вместе с городом держит осаду.

Играть: https://magicsurvival.neuraldeep.ru

*A browser survivors-like on Unity 6 (WebGL): auto-casting mage vs undead hordes, spell schools with supers and fusions, gear with enchanting up to +16, a shared town with chat, tavern and parties, 4-player co-op runs and a daily town siege. Client in C# (IMGUI), servers in plain Python + SQLite. Docs are in Russian.*

## Что внутри

- 5 карт, 3 героя, 4 школы магии с суперсилами и слияниями, боссы, павшие маги других игроков
- город: общая площадь, чат, порталы, таверна с барменом и бильярдом, пати до четырёх
- кооператив до 4 магов (WebSocket-ретранслятор, дельта-снимки)
- снаряжение: 7 слотов, грейды D–S, 20 видов оружия, заточка до +16, рынок между игроками
- осада города раз в день, системный чат с объявлениями
- лаборатория баланса: автопилот гоняет забеги без графики

## Быстрый старт

1. Unity **6000.6.5f1**, открыть папку проекта.
2. Сборка: `Unity -batchmode -nographics -projectPath . -executeMethod Builder.Cli -builds web -logFile Logs/batch-build.log` → `Builds/Web`.
3. Свой сервер (Python 3.10+, без зависимостей кроме `websockets` для тестов): раздел «Свой сервер за 5 минут» в [docs/build-deploy.md](docs/build-deploy.md).

Без сервера игра тоже запускается: одиночные забеги работают офлайн.

## Документация

[docs/README.md](docs/README.md) — архитектура, как добавить контент, кооператив, город, снаряжение, грабли, карта интерфейса.

После клона: `git config core.hooksPath .githooks` — хук не пустит в коммит секреты и базу игроков.

## Лицензии

Код — [MIT](LICENSE). Модели, звуки и шрифты — CC0 и OFL, иконки и музыка сгенерированы ИИ: подробно в [CREDITS.md](CREDITS.md).

## Авторы

Игру сделал [vakovalskii](https://github.com/vakovalskii) вместе с CLI-агентами [Claude Code](https://claude.com/claude-code) и [Qwen Code CLI](https://github.com/QwenLM/qwen-code) (модель Qwen Coder).
