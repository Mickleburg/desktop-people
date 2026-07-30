# DesktopPeople — состояние проекта

Дата обновления: 2026-07-30

Этапы 0–1 завершены и вручную проверены пользователем. Этап 2 реализован; перед
переходом к этапу 3 требуется ручная приёмка окон-платформ на целевом desktop.

## Реализовано

- изолированный Windows adapter с DWM bounds и fallback;
- первоначальный scan и reconciliation раз в пять секунд;
- системные события через thread-safe очередь;
- фильтрация собственных, невидимых, свёрнутых, малых и служебных окон;
- screen-to-overlay mapping для отрицательных координат и Per-Monitor DPI;
- immutable platform snapshots и консервативные видимые сегменты;
- swept collision по ширине стоп и выбор ближайшей поверхности;
- attachment, move, resize, close, hide, minimize и restore;
- падение на нижнее окно или desktop work area;
- выключенная по умолчанию debug-визуализация;
- внутренний WindowHost и структурированные platform metrics.

## Автоматически проверено

- baseline этапа 1: 7/7;
- полный набор этапа 2: 38/38;
- strict Debug build: 0 warnings, 0 errors;
- fake API: DWM/fallback, invalid handles и фильтрация;
- collision, tunnelling, nearest platform, attachment и resize;
- create/move/minimize/restore/hide/destroy и missed-event reconciliation;
- реальный WindowHost обнаружен platform provider; первоначальный scan нашёл
  283 top-level окна, после фильтрации осталось 3 платформы, обновление ~28 мс;
- исчезновение реальной опоры дало `Idle → Fall` и последующее приземление.
- self-contained smoke: процесс жив после 6 секунд, без unhandled errors;
- измеренный idle interval: около 0,73% общего CPU и 71,4 МБ working set;
- `dotnet format --verify-no-changes`: успешно.

## Release artifact

- путь: `artifacts/win-x64/DesktopPeople.exe`;
- размер: 116 174 631 байт;
- SHA-256: `55EEFA597E3E0E3D04CBE400463344C6C2C7877C0721D47C71D94260E817AF4F`.

## Вручную проверено

- этап 1: ходьба, grab/drag/release, gravity и selective click-through.

## Пока не проверено вручную

- полный сценарий Блокнота: attachment при move/resize/minimize/close;
- падение с верхнего окна на нижнее;
- DPI 100%, 125% и 150%;
- несколько мониторов с разным DPI;
- десятиминутный stability/CPU test.

## Известные ограничения

1. Перекрытие основано на прямоугольниках и последнем известном z-order; shaped
   и сложные layered regions не вычисляются полностью.
2. Несколько мониторов представлены корректным virtual screen, но реальная
   mixed-DPI конфигурация ещё не проверена вручную.
3. WinEvent updates могут отставать на один UI tick; reconciliation исправляет
   пропущенные события раз в пять секунд.
4. Renderer всё ещё использует временного векторного персонажа и
   `TransparencyKey`.
5. Avatar Builder, несколько персонажей и installer не относятся к этапу 2 и не
   реализованы.

## Следующий рекомендуемый шаг

Выполнить сценарии D1–D6 из `docs/MANUAL_ACCEPTANCE.md`. Только после успешной
ручной приёмки зафиксировать этап 2 полностью завершённым и переходить к системе
анимаций/поведения этапа 3.
