# PDF Grouping 0.1.47 — cumulative changes since 0.1.42 / все изменения после 0.1.42

Includes versions 0.1.43–0.1.47. / Включает версии 0.1.43–0.1.47.

## English

### Fixed
- No more UI lag when a new range overlaps hundreds of existing ones (e.g. per-page ranges over a
  whole book): the overlap banner shows the first 50 detail rows plus "… and N more overlaps";
  the summary of duplicated pages is still complete. (0.1.43)
- Page previews no longer show thumbnails of a previously selected range when switching quickly;
  preview and zoom images are released immediately when replaced or closed, so memory no longer
  spikes while browsing pages. (0.1.43)
- Output files are created atomically: a file that appears on disk at the very moment of saving is
  never silently overwritten — the next free indexed name is used. (0.1.45)
- Robustness of page rendering: no colour distortion on pages with transparency; frame-buffer row
  copying is guarded against stride mismatch. (0.1.45)
- Opening the results folder on macOS/Linux handles any folder names safely; failures are logged.
  Automatic and manual update checks can no longer run simultaneously. (0.1.45)

### Changed (performance)
- The "Current ranges" list is virtualized: only the visible rows are created (300 ranges →
  7 realized rows), so the panel stays instant with hundreds of per-page ranges. The look is
  unchanged. (0.1.47)

### Added
- Diagnostic log at `%AppData%/PdfGrouping/log.txt` (~1 MB rotation): app start, PDF/processing
  errors, update-check failures, unhandled exceptions — user-reported issues become diagnosable.
  A malformed translation template can no longer crash a command. (0.1.43)

### Internal (no visible changes)
- The main view model was split into focused partial files; duplicated logic extracted into
  shared helpers; dead code removed. (0.1.44)
- The overlap decision logic was moved into the core library as pure functions and covered by
  17 dedicated unit tests (60 in total). (0.1.46)

## Русский

### Исправлено
- Больше нет подтормаживания интерфейса, когда новый диапазон пересекается с сотнями существующих
  (например, постраничные диапазоны на всю книгу): баннер показывает первые 50 строк и «… и ещё
  пересечений: N»; сводка дублируемых страниц по-прежнему полная. (0.1.43)
- Предпросмотр больше не показывает миниатюры ранее выбранного диапазона при быстром переключении;
  изображения предпросмотра и зума освобождаются сразу при замене/закрытии — память не растёт при
  листании страниц. (0.1.43)
- Выходные файлы создаются атомарно: файл, появившийся на диске прямо в момент сохранения, не
  перезаписывается молча — берётся следующее свободное имя с индексом. (0.1.45)
- Надёжность отрисовки страниц: нет искажений цвета на страницах с прозрачностью; построчное
  копирование в кадровый буфер защищено от несовпадения stride. (0.1.45)
- Открытие папки результатов на macOS/Linux безопасно для любых имён папок; ошибки пишутся в лог.
  Автоматическая и ручная проверки обновлений больше не идут одновременно. (0.1.45)

### Изменено (быстродействие)
- Список «Текущие диапазоны» виртуализирован: создаются только видимые строки (300 диапазонов →
  7 созданных строк), панель мгновенна при сотнях постраничных диапазонов. Внешний вид не
  изменился. (0.1.47)

### Добавлено
- Диагностический лог `%AppData%/PdfGrouping/log.txt` (ротация ~1 МБ): запуск, ошибки
  PDF/обработки, сбои проверки обновлений, необработанные исключения — проблемы пользователей
  становятся диагностируемыми. Некорректный шаблон перевода больше не роняет команду. (0.1.43)

### Внутреннее (без видимых изменений)
- Главная модель представления разнесена на компактные partial-файлы; дублированная логика
  вынесена в общие методы; удалён мёртвый код. (0.1.44)
- Логика решений о пересечениях вынесена в ядро в виде чистых функций и покрыта 17 отдельными
  юнит-тестами (всего 60). (0.1.46)
