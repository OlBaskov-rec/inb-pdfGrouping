# PDF Grouping 0.1.49

## English

### Added — Combine multiple PDFs into one output
- A new "Files to process" panel on the left lets you add several PDF files (button or
  drag & drop, one or many at once). Click a file in the list to make it the active source for
  picking page ranges; the ✕ button removes a file from the list without touching ranges/groups
  already built from it.
- Ranges from **different** source files can now be combined into the same output group — one
  output PDF can be assembled from pages of several different source documents.
- The "Source PDF file" field is renamed to "File path" and simply shows the path of the
  currently active file (selected in the list on the left); browsing now happens from the new
  panel's "+ Add file(s)…" button.
- When more than one file is loaded, each range in the list is tagged with its source file name
  so it's always clear which document a range belongs to; the same applies to the "Created
  groups" summary when a group spans multiple files.

### Changed
- Overlap detection now compares pages only *within the same source file* — page 5 of one file
  and page 5 of another are different pages and no longer falsely reported as a conflict.
  Same-file overlap detection, trimming and the "keep first occupier" resolution work exactly as
  before.

## Русский

### Добавлено — объединение нескольких PDF в один файл
- Новая панель «Файлы для обработки» слева позволяет добавить несколько PDF (кнопкой или
  перетаскиванием — сразу один или несколько). Клик по файлу в списке делает его активным
  источником для выбора диапазонов страниц; кнопка ✕ убирает файл из списка, не трогая уже
  созданные по нему диапазоны/группы.
- Диапазоны из **разных** исходных файлов теперь можно объединять в одну группу — один выходной
  PDF может собираться из страниц нескольких разных документов.
- Поле «Исходный PDF-файл» переименовано в «Путь к файлу» и просто показывает путь текущего
  активного файла (выбранного в списке слева); добавление файлов теперь происходит через кнопку
  «+ Добавить файл(ы)…» новой панели.
- Когда загружено больше одного файла, каждый диапазон в списке помечается именем своего
  исходного файла — всегда понятно, какому документу он принадлежит; то же самое — в сводке
  «Сформированные группы», если группа собрана из нескольких файлов.

### Изменено
- Проверка пересечений теперь сравнивает страницы только *в пределах одного и того же исходного
  файла* — страница 5 одного файла и страница 5 другого больше не считаются ложным конфликтом.
  Обнаружение пересечений внутри одного файла, обрезка и разрешение «оставить первый занявший»
  работают точно так же, как раньше.
