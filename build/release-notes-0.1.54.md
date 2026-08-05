# PDF Grouping 0.1.54 — cumulative changes since 0.1.47 / все изменения после 0.1.47

Includes versions 0.1.48–0.1.54. / Включает версии 0.1.48–0.1.54.

## English

### Added
- New "Files to process" panel (left) to add multiple PDFs — button or drag&drop, click a row to
  make it the active file, ✕ to remove; ranges from **different** source files can now be combined
  into one output group/file. "Source PDF file" renamed to "File path" (display-only, shows the
  active file); ranges and group summaries show the source file name/number whenever more than one
  file is loaded. (0.1.49)

### Changed
- Removed the duplicate "Current ranges" list from the "Group label" panel (same data already
  shown in "Page ranges"); create buttons renamed to reference the output file rather than
  "group". (0.1.48)
- Overlap detection now compares pages only within the same source file — no more false conflicts
  between same-numbered pages of different files. (0.1.49)
- The file list shows a "No." column; a thin separator line was added between file rows; "Group
  label" create buttons moved right below A–E with standard spacing. (0.1.50)
- "Group label" renamed to "File name", "Created groups" renamed to "Output files"; the gap before
  create buttons in "File name" and before "Clear"/"Preview" in "Page ranges" is now larger and
  equal in both places; the range list inside "Output files" uses the same "p. X–Y (N p.)" wording
  as "Page ranges" instead of a bare "X–Y" list. (0.1.51)
- Source-number badges switched from blue to brown; "Files to process" renamed to "Sources to
  process"; the range source tag reads "[Source N]" instead of "[N]", used consistently in the
  ranges list, the "Output files" summaries, and the conflict-resolution proposal list. (0.1.52)
- The language-switch and "i" (About) buttons moved from the top-right corner of the window down
  into the "File path" panel; faint arrow markers now show the flow between "Page ranges" →
  "File name" → "Output files"; the right-edge margin was made to match the left. (0.1.53)
- "Sources to process" now shares the same pale background as "File path" (both describe the
  active source); the language/"i" buttons moved off that shared background into their own row
  below it; "+ Add range" is about 25% taller and "+ Add range per page" about 20% taller. (0.1.54)

### Fixed
- Long file names no longer overlap the ✕ button in the "Page ranges" list — ranges are tagged
  with a short file number instead of the full name. (0.1.50)
- Shrinking the window vertically could make the auto-hiding scrollbar overlap the interface
  (especially wide on hover); the right-edge gap now widens automatically only while a scrollbar
  is actually needed, and otherwise stays flush with the left edge. (0.1.54)

## Русский

### Добавлено
- Новая панель «Файлы для обработки» (слева) для добавления нескольких PDF — кнопкой или
  перетаскиванием, клик по строке делает файл активным, ✕ убирает файл; диапазоны из **разных**
  исходных файлов теперь можно объединять в одну группу/файл. «Исходный PDF-файл» переименован в
  «Путь к файлу» (только отображение активного файла); диапазоны и сводки групп показывают имя/
  номер файла-источника, если загружено больше одного. (0.1.49)

### Изменено
- Убран дублирующий список «Текущие диапазоны» из панели «Метка группы» (те же данные уже
  показаны в «Диапазоны страниц»); кнопки создания переименованы с акцентом на файл, а не на
  «группу». (0.1.48)
- Проверка пересечений теперь сравнивает страницы только в пределах одного файла-источника —
  больше нет ложных конфликтов между одинаковыми номерами страниц разных файлов. (0.1.49)
- Список файлов показывает номер в колонке «№»; добавлена тонкая линия-разделитель между строками
  файлов; кнопки создания в «Метке группы» перенесены сразу под A–E со стандартным интервалом.
  (0.1.50)
- «Метка группы» переименована в «Имя файла», «Сформированные группы» — в «Файлы для вывода»;
  отступ перед кнопками создания в «Имени файла» и перед «Очистить»/«Предпросмотр» в «Диапазонах
  страниц» стал больше и одинаковым в обоих местах; список диапазонов внутри «Файлы для вывода»
  оформлен теми же словами, что и «Диапазоны страниц» («Стр. X–Y (N стр.)»), вместо голого
  перечня «X–Y». (0.1.51)
- Метки-номера источника сменили цвет с синего на коричневый; «Файлы для обработки» переименованы
  в «Источники для обработки»; метка источника у диапазона теперь пишется «[N источник]» вместо
  «[N]» — та же формулировка используется везде: в списке диапазонов, в сводках «Файлы для
  вывода» и в списке разрешения конфликтов. (0.1.52)
- Кнопки смены языка и «i» (О программе) перенесены из правого верхнего угла окна в панель «Путь
  к файлу»; между «Диапазоны страниц» → «Имя файла» → «Файлы для вывода» появились бледные
  стрелки, показывающие порядок работы; правое поле окна приведено в соответствие с левым.
  (0.1.53)
- «Источники для обработки» теперь той же бледной подложки, что и «Путь к файлу» (оба поля — про
  активный источник); кнопки языка/«i» вынесены с этой общей подложки в свой ряд под ней;
  «+ Добавить диапазон» стала выше примерно на 25%, «+ Добавить диапазон постранично» — на 20%.
  (0.1.54)

### Исправлено
- Длинные имена файлов больше не наезжают на кнопку ✕ в списке «Диапазоны страниц» — диапазоны
  помечаются коротким номером файла вместо полного имени. (0.1.50)
- При уменьшении окна по высоте появляющаяся полоса прокрутки могла наезжать на интерфейс
  (особенно сильно при наведении мышью); правое поле теперь увеличивается автоматически только
  пока полоса прокрутки реально нужна, а в остальное время остаётся вровень с левым. (0.1.54)
