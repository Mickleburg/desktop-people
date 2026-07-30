# Тестирование

## Автоматические проверки

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test.ps1
```

Runner проверяет:

- жизненный цикл конечного автомата;
- отключение и восстановление персонажа;
- падение и приземление;
- позиционирование при удержании мышью;
- сохранение настроек;
- сериализацию формата персонажа;
- защиту относительных путей от выхода из каталога аватара.
- фильтрацию обычных, невидимых, свёрнутых, собственных и служебных окон;
- DWM bounds и fallback на `GetWindowRect` через fake Win32 API;
- отрицательные координаты и DPI mapping;
- swept collision, направление падения и выбор ближайшей платформы;
- attachment, move, resize и потерю опоры;
- очередь create/move/minimize/destroy и periodic reconciliation.

Текущий результат: 38/38 тестов.

Полная строгая сборка:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

CI выполняет обе команды на `windows-latest`.

## Smoke test

Для автоматического smoke test следует запустить self-contained
`DesktopPeople.exe`, убедиться, что процесс остаётся жив не менее пяти секунд и
что в `%LOCALAPPDATA%\DesktopPeople\logs` появился `application_started`.
Завершать нормальную ручную проверку нужно через tray, чтобы проверить весь
жизненный цикл.

## WindowHost

`scripts/window-host.ps1` запускает обычное WinForms-окно с кнопками move,
resize, hide и close. Оно позволяет проверить реальный HWND без зависимости от
конкретной версии Блокнота. Лог host сохраняется в
`%TEMP%\DesktopPeople.WindowHost\last-run.jsonl`.

## Будущие fixtures Avatar Builder

Локальные приватные изображения размещаются в `tests/fixtures/private/` и
игнорируются Git. Публичный manifest fixtures должен описывать категории:
`full_body`, `knees_up`, `waist_up`, `portrait`, сложный фон, перекрытия,
несколько людей, низкое разрешение, повреждённый и слишком большой файл.
