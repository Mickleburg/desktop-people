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

## Будущие fixtures Avatar Builder

Локальные приватные изображения размещаются в `tests/fixtures/private/` и
игнорируются Git. Публичный manifest fixtures должен описывать категории:
`full_body`, `knees_up`, `waist_up`, `portrait`, сложный фон, перекрытия,
несколько людей, низкое разрешение, повреждённый и слишком большой файл.

