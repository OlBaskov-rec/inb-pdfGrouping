# PDF Grouping 0.1.34

## English

### Fixed
- "Check for updates" no longer gets stuck on "Checking…": the check now has a timeout and, if
  the network does not respond, it reports a timeout and re-enables the button (this was broken
  in 0.1.31).

### Changed
- The window now remembers its size between launches, so it no longer resets to the default size.

### Added
- A regression checklist (`docs/CHECKLIST.md`) covering all features and UI behaviours, to verify
  before/after a release so earlier work doesn't silently break.

## Русский

### Исправлено
- «Проверить обновление» больше не зависает на «Проверка…»: у проверки появился таймаут, и если
  сеть не отвечает, выводится сообщение о превышении времени ожидания, а кнопка снова активна
  (было сломано в 0.1.31).

### Изменено
- Окно теперь запоминает свой размер между запусками — размер больше не сбрасывается к значению
  по умолчанию.

### Добавлено
- Регрессионный чек-лист (`docs/CHECKLIST.md`) по всем фичам и поведению интерфейса — для проверки
  до/после релиза, чтобы ранее сделанное не ломалось незаметно.
