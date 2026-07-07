# PDF Grouping 0.1.46

## English

### Changed (internal, no visible changes)
- The overlap decision logic (detecting which pages a new range duplicates, cross-set and
  internal conflicts, the "remove overlapping" resolution) was extracted from the UI layer into
  the core library (`OverlapAnalysis`) as pure functions, and is now covered by 17 dedicated unit
  tests (60 in total). The UI layer only turns the analysis result into localized texts.
- Behaviour is unchanged — verified by an end-to-end smoke test against the previous version's
  baseline (overlap banner, resolution buttons, groups, "remove overlapping").

## Русский

### Изменено (внутреннее, без видимых изменений)
- Логика решений о пересечениях (какие страницы дублирует новый диапазон, конфликты между
  наборами и внутри набора, разрешение «убрать пересекающиеся») вынесена из слоя интерфейса в
  ядро (`OverlapAnalysis`) в виде чистых функций и покрыта 17 отдельными юнит-тестами (всего 60).
  Слой интерфейса теперь только превращает результат анализа в локализованные тексты.
- Поведение не изменилось — проверено сквозным smoke-тестом по эталону предыдущей версии
  (баннер пересечений, кнопки решения, группы, «убрать пересекающиеся»).
