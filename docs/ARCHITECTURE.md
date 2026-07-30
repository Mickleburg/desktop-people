# Архитектура

Приложение разделено на UI, Windows adapter и независимое физическое ядро.

## Компоненты

- DesktopPeople.App: стартовое окно, tray, overlay и renderer.
- DesktopPeople.Windows: чтение окон, события и provider.
- DesktopPeople.Core: state machine, physics, filter, collision и attachment.

Только Windows-проект знает о системных handles. Core получает immutable
PlatformSnapshot; его тесты не требуют настоящих окон.

## Runtime

Overlay получает snapshot, следует за attachment, интегрирует физику и выполняет
swept collision. Пустая область сохраняет selective click-through. Screen-origin
вычитается из координат окон, включая мониторы с отрицательными координатами.

Подробности этапа: [STAGE2_IMPLEMENTATION.md](STAGE2_IMPLEMENTATION.md).
