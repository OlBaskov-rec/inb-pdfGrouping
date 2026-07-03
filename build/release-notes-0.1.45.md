# PDF Grouping 0.1.45

## English

### Fixed (robustness, no visible changes)
- Output files are now created atomically (`CreateNew`): if a file with the chosen name appears
  on disk at the very moment of saving, it is never silently overwritten — the next free indexed
  name is used instead.
- Rendering safety nets: colour-blending to white can no longer wrap around a byte on
  non-premultiplied pages; row copying into the frame buffer is guarded against stride mismatch.
- Opening the results folder on macOS/Linux now passes the path as a single argument (quotes or
  special characters in a folder name can't break the command); failures are logged.
- Automatic (at startup) and manual update checks can no longer run simultaneously.

## Русский

### Исправлено (надёжность, без видимых изменений)
- Выходные файлы создаются атомарно (`CreateNew`): если файл с выбранным именем появится на диске
  прямо в момент сохранения, он не будет молча перезаписан — берётся следующее свободное имя
  с индексом.
- Страховки в отрисовке: подмешивание белого фона больше не может «завернуть» байт на страницах
  без премультипликации; построчное копирование в кадровый буфер защищено от несовпадения stride.
- Открытие папки результатов на macOS/Linux передаёт путь одним аргументом (кавычки и спецсимволы
  в имени папки не ломают команду); ошибки открытия пишутся в лог.
- Автоматическая (при старте) и ручная проверки обновлений больше не могут идти одновременно.
