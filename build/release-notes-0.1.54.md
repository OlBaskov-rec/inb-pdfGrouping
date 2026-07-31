# PDF Grouping 0.1.54

## English

### Fixed
- Shrinking the window vertically could make the auto-hiding scrollbar overlap the interface
  (especially wide on hover). The right-edge gap now widens automatically only while a scrollbar
  is actually needed, and stays flush with the left edge the rest of the time.

### Changed
- "Sources to process" (the file list panel) now shares the same pale background as "File path" —
  both describe the active source, so they now read as one visual group.
- The language-switch and "i" (About) buttons no longer sit on the "File path" panel's background;
  they're in their own row just below it, on the normal window background.
- "+ Add range" is about 25% taller, "+ Add range per page" about 20% taller — these are the main
  actions in "Page ranges", so they stand out more.

## Русский

### Исправлено
- При уменьшении окна по высоте появляющаяся полоса прокрутки могла наезжать на интерфейс (особенно
  сильно при наведении мышью — тогда она расширяется). Теперь правое поле увеличивается автоматически
  только пока полоса прокрутки реально нужна, а в остальное время остаётся вровень с левым.

### Изменено
- «Источники для обработки» (список файлов слева) теперь той же бледной подложки, что и «Путь к
  файлу» — оба поля про активный источник, теперь это единая визуальная группа.
- Кнопки смены языка и «i» (О программе) больше не сидят на подложке «Путь к файлу» — они в своём
  ряду сразу под ней, на обычном фоне окна.
- «+ Добавить диапазон» стала выше примерно на 25%, «+ Добавить диапазон постранично» — на 20%: это
  основные действия в «Диапазонах страниц», теперь они заметнее выделяются.
