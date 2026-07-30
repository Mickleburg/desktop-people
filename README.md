# DesktopPeople

DesktopPeople превращает идею «маленький человек живёт на рабочем столе» в
локальное Windows-приложение. Текущий репозиторий содержит проверяемый
технический прототип desktop-runtime: прозрачный overlay, тестового персонажа,
перетаскивание, бросок, гравитацию, простую ходьбу и управление через системный
трей.

Обработка фотографий пока не реализована и не имитируется. Векторный персонаж
нужен для проверки самого рискованного основания продукта — корректной жизни
поверх обычных Windows-приложений.

## Требования для разработки

- Windows 10/11 x64;
- .NET 10 SDK.

В рабочем окружении Codex SDK может находиться в `.tools/dotnet`; эта папка не
попадает в Git. Обычный разработчик может установить официальный .NET 10 SDK.

## Быстрый старт

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\test.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

После запуска:

1. нажмите «Выпустить на рабочий стол»;
2. кликните по персонажу, чтобы увидеть реакцию;
3. зажмите левую кнопку мыши, перетащите и бросьте персонажа;
4. используйте значок DesktopPeople в системном трее для паузы, скрытия и
   завершения.

Пустая область overlay переключается в Win32 click-through и не должна мешать
работе с другими приложениями.

## Self-contained сборка

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

Готовый прототип будет находиться в `artifacts/win-x64/DesktopPeople.exe` и не
требует установленного .NET Runtime. Installer относится к этапу упаковки и пока
не создан.

## Документация

- [Архитектура](docs/ARCHITECTURE.md)
- [Решения](docs/DECISIONS.md)
- [Roadmap](docs/ROADMAP.md)
- [Тестирование](docs/TESTING.md)
- [Ручная приёмка](docs/MANUAL_ACCEPTANCE.md)
- [Текущее состояние](PROJECT_STATUS.md)

Приложение не выполняет сетевых запросов, не содержит телеметрии и пишет только
технические JSONL-логи без пользовательских изображений в
`%LOCALAPPDATA%\DesktopPeople\logs`.

