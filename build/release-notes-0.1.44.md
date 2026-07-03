# PDF Grouping 0.1.44

## English

### Changed (internal, no visible changes)
- Code refactoring: the main view model (~1,470 lines) was split into focused partial files
  (ranges, overlaps, groups, preview, updates); duplicated validation/overlap-detection logic was
  extracted into shared helpers; dead code and the duplicated version helper were removed.
- No behaviour changes. The installed application layout is unchanged (same single executable) —
  this only improves source-code maintainability. Verified by an end-to-end smoke test
  (load → add ranges → overlap decision → groups → per-range groups) and the full test suite.

## Русский

### Изменено (внутреннее, без видимых изменений)
- Рефакторинг кода: главная модель представления (~1 470 строк) разнесена на компактные
  partial-файлы (диапазоны, пересечения, группы, предпросмотр, обновления); продублированная
  логика валидации/поиска пересечений вынесена в общие методы; удалён мёртвый код и дубль
  функции версии.
- Поведение не менялось. Состав установленной программы не изменился (тот же единственный
  исполняемый файл) — улучшена только сопровождаемость исходников. Проверено сквозным
  smoke-тестом (загрузка → диапазоны → решение по пересечению → группы → группы на диапазон)
  и полным набором тестов.
