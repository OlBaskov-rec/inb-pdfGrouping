# PDF Grouping 0.1.47

## English

### Changed (performance)
- The "Current ranges" list (Group label panel) is now virtualized: only the visible rows are
  created (about 7) instead of the whole list. With hundreds of per-page ranges the panel opens
  and updates instantly and uses far less memory. Verified: 300 ranges → 7 realized rows.
- The look is unchanged — same compact "P. 5 – 5 (1 p.)" rows on the grey backdrop with scrolling.

## Русский

### Изменено (быстродействие)
- Список «Текущие диапазоны» (панель «Метка группы») теперь виртуализирован: создаются только
  видимые строки (около 7), а не весь список. При сотнях постраничных диапазонов панель
  открывается и обновляется мгновенно и расходует заметно меньше памяти. Проверено: 300
  диапазонов → создано 7 строк.
- Внешний вид не изменился — те же компактные строки «Стр. 5 – 5 (1 стр.)» на серой подложке
  с прокруткой.
