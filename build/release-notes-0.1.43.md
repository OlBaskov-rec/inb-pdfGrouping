# PDF Grouping 0.1.43

## English

### Fixed
- No more UI lag when a new range overlaps hundreds of existing ones (e.g. per-page ranges over a
  whole book): the overlap banner now shows the first 50 detail rows plus "… and N more overlaps";
  the summary of duplicated pages is still complete.
- Page previews no longer show thumbnails of a previously selected range when switching ranges
  quickly (background renders are now generation-checked).
- Preview and zoom images are now released immediately when replaced or closed — memory no longer
  spikes while browsing pages (the zoom image alone is ~14 MB).

### Added
- Diagnostic log at `%AppData%/PdfGrouping/log.txt` (rotated at ~1 MB): app start, PDF/processing
  errors, update-check failures, unhandled exceptions. Makes user-reported issues diagnosable.
- A malformed translation template can no longer crash a command — the raw template is shown and
  the problem is logged.

## Русский

### Исправлено
- Больше нет подтормаживания интерфейса, когда новый диапазон пересекается с сотнями существующих
  (например, постраничные диапазоны на всю книгу): баннер пересечений показывает первые 50 строк
  и «… и ещё пересечений: N»; сводка дублируемых страниц по-прежнему полная.
- Предпросмотр больше не показывает миниатюры ранее выбранного диапазона при быстром переключении
  (фоновые рендеры проверяются на актуальность).
- Изображения предпросмотра и зума освобождаются сразу при замене/закрытии — память не растёт
  при листании страниц (одно изображение зума — ~14 МБ).

### Добавлено
- Диагностический лог `%AppData%/PdfGrouping/log.txt` (ротация на ~1 МБ): запуск, ошибки
  PDF/обработки, сбои проверки обновлений, необработанные исключения. Проблемы пользователей
  становятся диагностируемыми.
- Некорректный шаблон перевода больше не роняет команду — показывается сырой шаблон, проблема
  пишется в лог.
