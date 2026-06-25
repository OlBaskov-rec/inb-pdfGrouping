# PDF Grouping — Regression checklist / Регрессионный чек-лист

> Run before/after a release (or on request) to make sure earlier features still work.
> Прогонять перед/после релиза (или по запросу), чтобы ранее сделанное не сломалось.

How to run / Как запускать: ask "проверь по чек-листу" — items can be checked manually in the
UI; many are also covered by automated screenshots and `dotnet test`.

---

## 1. Source PDF / Исходный файл
- [ ] Open via "Обзор…" and via drag & drop / Открытие кнопкой и перетаскиванием.
- [ ] Page count is shown ("Всего страниц") / Показывается число страниц.
- [ ] "✕" resets everything; new file = new session / Сброс и новая сессия.

## 2. Ranges / Диапазоны
- [ ] Numeric fields with up/down arrows; clearing a field doesn't error / Поля со стрелками, очистка не ломает.
- [ ] "+ Добавить диапазон" and "+ постранично" work / Обе кнопки работают.
- [ ] Ranges list caps at ~5 rows then scrolls / Список ограничен по высоте + прокрутка.
- [ ] "Очистить" empties the list / Очистка.

## 3. Overlaps / Пересечения
- [ ] Overlap warning shows aligned columns (range · source · "повторяются …") / Колонки выровнены.
- [ ] Buttons: "Добавить ещё раз" / "Добавить без пересечения" / "Убрать" / 3 кнопки.
- [ ] After "Добавить ещё раз" the decision buttons hide, warning stays / Кнопки скрываются.
- [ ] Page fields AND "Без пересечений" toggle are disabled while a question is pending / Блокируются.
- [ ] Conflict resolution: "Подтвердить" / "Убрать пересекающиеся" / delayed "Оставить имеющиеся" / Решение конфликта.
- [ ] "Без пересечений" doesn't stick when pressed during a pending question / Тумблер не залипает.

## 4. Groups / Группы
- [ ] "Создать группу для вывода" and "…по каждому диапазону" / Обе кнопки.
- [ ] Label A–E buttons; 15-char limit / Метки A–E, лимит 15.
- [ ] Merge prompt when label already exists / Запрос на объединение.
- [ ] "Текущие диапазоны" and "Сформированные группы" lists cap at ~7 then scroll; panels don't stretch down / Списки ограничены, секции не уезжают вниз.

## 5. Preview / Предпросмотр
- [ ] Thumbnails of first/last page / Миниатюры.
- [ ] Zoom via "🔍 Увеличить" AND by clicking the page image / Зум кнопкой и кликом по изображению.
- [ ] In zoom: rotate ↺ / ↻ by 90°; click outside toolbar closes / Поворот; клик вне панели закрывает.

## 6. Processing / Обработка
- [ ] Creates one PDF per group with correct page counts / По файлу на группу.
- [ ] Name collision → auto-index "Name (1).pdf"; existing files not overwritten / Авто-индекс.
- [ ] Results list + "Открыть папку" / Список результатов и открытие папки.

## 7. Layout / Компоновка
- [ ] "Сохранить в" + "Обработать" / "Без пересечений" pinned at bottom, always visible / Закреплены внизу.
- [ ] Small window: scrollbar appears, pinned buttons stay; clamps to screen / Прокрутка на низком разрешении.
- [ ] Window size is remembered between launches / Размер окна запоминается.

## 8. Localization / Локализация
- [ ] 9 languages; language button switches live / 9 языков, переключение на лету.
- [ ] No foreign-language leftovers, incl. when switching while a warning is shown / Без вкраплений.

## 9. About & Updates / О программе и обновления
- [ ] "ℹ" flyout: name, version, author / Сведения.
- [ ] "Проверить обновление" completes (does not hang on "Проверка…") / Проверка завершается, не виснет.
- [ ] Auto-detected update: only the "ℹ" icon blinks green (no bottom banner yet) / Мигание значка.
- [ ] Flyout green "Обнаружено обновление" → bottom banner with "Обновить и перезапустить" + "Отложить" / Кнопка в меню → баннер.
- [ ] Update applies and relaunches to the new version / Обновление ставится и перезапускает.

## 10. App / Приложение
- [ ] App icon in title bar / taskbar / Иконка.
- [ ] Portable: single launcher "PdfGrouping.exe"; self-contained / Один лаунчер, автономно.
- [ ] No leftover processes after closing / Нет висящих процессов.
- [ ] Window opens immediately (update check never blocks startup) / Запуск не виснет.
