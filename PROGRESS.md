# GemHelper — прогресс разработки

> Плагин для ExileApi (330.5), визуализирующий гемы в надетых сокетах.
> Файл ведётся по ходу разработки. Риски и открытые вопросы не блокируют работу — фиксируем и решаем по мере появления.

- **Папка:** `D:\GH\ExileApi-Compiled-330.5\Plugins\Source\GemHelper`
- **Цель сборки:** `Plugins/Compiled/GemHelper`
- **Старт:** 2026-11-20
- **Статус:** ✅ M0–M6 + M4b + UX; исправлен расчёт опыта (самокалибровка/таблица/`ExperienceToNextLevel`), ожидает проверки в игре

---

## 1. Цель

Показывать информацию о гемах, вставленных в сокеты **надетых** предметов:

- слот экипировки → сокет → цвет / группа линка;
- имя гема, уровень, качество, требуемый уровень, опыт;
- визуально: рамки сокетов + подписи (опционально), и/или компактная таблица;
- обновление в реальном времени, без просадок FPS.

Вне scope (пока): вставка/извлечение гемов, фильтры по модам, работа со сташем.

---

## 2. Текущее состояние

| Компонент | Состояние |
|---|---|
`GemHelper.cs` | ✅ снимок в `Tick`, отладочная ImGui-панель в `Render` |
`GemHelperSettings.cs` | ✅ `Enable`, `ShowDebugSnapshot`, `UpdateIntervalMs` |
`GemHelper.csproj` | ✅ `net10.0-windows` + `Hexa.NET.ImGui`, сборка 0/0 |
`Core/GemSocketSnapshot.cs` | ✅ модель сокета + `DisplayName` |
`Core/GemSnapshot.cs` | ✅ снимок, `ContentHash`, `IsSameContent` |
`Core/GemSnapshotReader.cs` | ✅ чтение 12 слотов, защита от исключений |
Чтение сокетов | ✅ реализовано, ⏳ не проверено в игре |
Кэш (M2) | ✅ `ServerRequestCounter`-гейт, full refresh, статистика, R1 fallback |
UI-таблица (M3) | ✅ таблица с группировкой по предметам, режимы Compact/Exp/Empty |
Оверлей (M4a) | ✅ рамки предметов + список гемов; ⏳ ручной тест |
UX/диагностика (M5a) | ✅ хоткеи окна/оверлея, кнопка дампа снимка |
Опыт гема (M6) | ✅ `%`/`MAX` в таблице и оверлее, сырые значения в tooltip |
Оверлей (M4b) | ✅ посокетные рамки + линии линков; ⏳ калибровка в игре |
Оверлей (M4) | ⬜ не начат |

---

## 3. Источники знаний (референсы)

### 3.1. Walker — как читаются гемы в сокетах
`D:\GH\ExileApi-Compiled-3.24\Plugins\Source\fix\Walker`

| Файл | Что берём |
|---|---|
`Types\Equip\SocketedGem.cs` | поля гема из `Base` + `SkillGem` + `Quality` |
`Types\Equip\GemSocket.cs` | модель сокета (`SocketIndex`, `SocketColor`, `LinkGroup`, `IsEmpty`) |
`Types\Equip\EquippedItem.cs` | `GemSockets[]`, `Links` (группировка по `LinkGroup`) |
`Utils\EntitySocketExtensions.cs` | работа с `Sockets.SocketInfo`, линки, паттерны |
`Utils\Gems\SocketsExtensions.cs` | `GetLinks`, паттерны `RRR-GG-B`, размер линка |
`Managers\EquippedItemResolver.cs` | получение надетых предметов + `TryGetSockets` |
`Extensions\SocketLayout.cs` | геометрия сокетов из прямоугольника слота |
`Managers\EquipmentSlotsConfig.cs` + `Data\Slots\Slots.json` | позиции слотов в `InventoryPanel` |
`Managers\Links\PhysicalGemRegistry.cs` | нормализация в `PhysicalGem` (Source: Equipment/Inventory/Cursor) |
`Walker\Gems\World\GemWorld.cs` | агрегированный снимок + хэши для кэша |
`Walker.Render.cs` (`DebugRender`) | пример отрисовки рамок/подписей/линий линков |

### 3.2. WheresMyCraftAt — как работать с предметами/UI
`D:\GH\ExileApi-Compiled-330.5\Plugins\Source\WheresMyCraftAt`

| Файл | Что берём |
|---|---|
`Handlers\InventoryHandler.cs` | `PlayerInventories`, `InventSlotItem`, проверки валидности |
`Handlers\StashHandler.cs` | UI-инвентари (`VisibleInventoryItems`) |
`Handlers\ElementHandler.cs` | UI-элементы, курсор, `Address`, смена адреса |
`Handlers\ItemHandler.cs` | имя базы через `Files.BaseItemTypes.Translate(path)` |
`Handlers\FilterHandler.cs` | `ItemFilterLibrary.ItemData` как источник данных |
`ClientSettings`/`CraftingSequenceMenu.cs` | паттерны настроек, `CustomNode`, кэш компиляции фильтров |
`Logging\Logging.cs` | ImGui-лог (шаблон панели) |
`WheresMyCraftAt.cs` | `Tick` + `Render` + `TaskUtils.RunOrRestart`, CTS |

### 3.3. ExileCore 330.5 (декомпиляция и метаданные)
- `Sockets` (компонент): `SocketInfo`, `SocketedGems`, `Links`, `NumberOfSockets`.
- `SocketColor`: `Red=1, Green=2, Blue=3, White=4, Abyssal=5, Resonator=6`.
- `ItemFilterLibrary.dll` 330.5: `ItemData.SocketInfo` (`LargestLinkSize`, `SocketNumber`, `Links`, `SocketGroups`, `SocketedGems`), `GemInfo` (`Level`, `MaxLevel`, `QualityType`, `IsGem`), `PlayerInfo.EquippedItems/OwnedGems`.

---

## 4. Цепочка данных (принятое решение)

```
IngameState.ServerData.PlayerInventories      (IList<InventoryHolder>)
  └ holder.Inventory.InventSlot == InventorySlotE.Helm1/BodyArmour1/...   ← искать по InventSlot, НЕ по индексу
      └ InventorySlotItems[0].Item           (Entity)
          ├ Base.Name / Base.Info.BaseItemTypeDat.ClassName
          └ Sockets
              └ SocketInfo[i]                (SocketColor, LinkGroup, SocketedGemEntity)
                  ├ Base.Name
                  ├ SkillGem.Level / RequiredLevel / TotalExpGained
                  └ Quality.ItemQuality
```

- Данные — **серверные** (`PlayerInventories`), координаты — **UI** (`InventoryPanel`).
- `SocketInfo` — физический порядок сокетов; `SocketedGemIndex == -1` для пустых.
- Снимок в `Tick`, отрисовка в `Render` только по снимку (без обращения к памяти в рендере).

---

## 5. Планируемая структура файлов

```
GemHelper/
├── GemHelper.cs                 // plugin entry: Initialise / Tick / Render
├── GemHelperSettings.cs         // ISettings: Enable, отображение, стили
├── GemHelper.csproj             // net10.0-windows, Hexa.NET.ImGui
├── PROGRESS.md                  // этот файл
├── Core/
│   ├── GemSnapshot.cs           // immutable снимок
│   ├── GemSnapshotReader.cs     // чтение Sockets из PlayerInventories
│   └── SnapshotCache.cs         // пересборка по изменению (address/хэш)
├── Rendering/
│   ├── GemOverlay.cs            // Graphics: рамки сокетов, подписи
│   └── GemPanel.cs              // ImGui-таблица
└── Layout/
    ├── EquipmentLayout.cs       // слоты InventoryPanel -> RectangleF
    └── SocketLayout.cs          // геометрия сокетов (порт из Walker, при необходимости)
```

---

## 6. Этапы (чеклист)

### M0. Подготовка проекта ✅
- [x] Обновить `GemHelper.csproj`: `net10.0-windows`
- [x] Подключить `Hexa.NET.ImGui` через `$(ExileApiDir)` (ItemFilterLibrary пока не нужен)
- [x] Добавить `<Using Include="Hexa.NET.ImGui" />`
- [x] Проверить сборку (`dotnet build` — 0 ошибок / 0 предупреждений)
- [x] Проверить сборку с global properties загрузчика (`OutputPath`, `exapiPackage`, `RuntimeIdentifier=win-x64`, `SelfContained=False`, `PathMap=`, `TargetFramework=net10.0-windows`)
- [ ] Проверить загрузку плагина в HUD и работу `Enable` (ручной тест)

### M1. Модель и чтение данных ✅ (код + сборка; нужен ручной тест)
- [x] `GemSocketSnapshot` (record): Slot, SocketIndex, Color, LinkGroup, GemName, Level, Quality, RequiredLevel, Exp
- [x] `GemSnapshot`: список сокетов + `ContentHash` + `CapturedAtUtc` + `Empty`/`IsEmpty`/`IsSameContent`
- [x] `GemSnapshotReader.Capture(GameController)`:
  - [x] поиск holder по `Inventory.InventSlot` для всех 12 слотов (включая `Weapon2`/`Offhand2`)
  - [x] проверки `item.IsValid`, `Address != 0`
  - [x] `TryGetComponent<Sockets>` → `SocketInfo`
  - [x] чтение гема: `Base`, `SkillGem`, `Quality`
- [x] Подписи: `Base.Name` (UI) и `ClassName` (фильтрация/классификация)
- [x] Tick-throttle (`UpdateIntervalMs`, 50..2000 мс), снимок только в `Tick`
- [x] Отладочная ImGui-панель со списком сокетов/гемов
- [ ] **Ручной тест в игре:** надеть предметы с гемами и сверить слоты/цвета/уровни с игрой
- [ ] **Ручной тест:** пустые сокеты отображаются как `empty`, линки совпадают
- [ ] **Ручной тест:** второй набор оружия (`Weapon2`/`Offhand2`) — проверить, приходят ли holder'ы

### M2. Кэш и производительность ✅ (код + сборка)
- [x] Не читать память каждый кадр: обновление по таймеру (`UpdateIntervalMs`)
- [x] Хэш содержимого снимка для быстрого сравнения (`ContentHash`, `IsSameContent`)
- [x] Пропускать пересборку, если `ServerRequestCounter` не менялся (отсортированный хэш пар `(InventSlot, Counter)`)
- [x] Страховочное полное обновление `FullRefreshIntervalMs` (500..10000, по умолчанию 2000 мс)
- [x] Переиспользовать предыдущий объект снимка при неизменном содержимом
- [x] Замер времени `Capture` (Stopwatch) + статистика в отладочной панели (`capture`, `reads`, `skip`, `counter`, `hash`)
- [x] R1 fallback: если `SocketInfo` пуст при `NumberOfSockets > 0` — сокеты из `SocketList` + `SocketedGems` (`LinkGroup = -1`)
- [ ] Частичный `SocketInfo` (меньше, чем `NumberOfSockets`): добирать недостающие сокеты через fallback
- [ ] Проверить в игре, что `counter` реально меняется при смене предметов/гемов, и `skip` растёт в покое

### M3. UI: таблица (первый видимый результат) ✅ (код + сборка)
- [x] ImGui-таблица: `Slot | # | Color | Link | Gem | Lvl | Q | Req | Exp`
- [x] Группировка по предмету: заголовок слота + имя предмета + класс + число сокетов
- [x] Цветовая подсветка строк по `SocketColor` (R/G/B/W/A/S), пустые — приглушённые
- [x] Показ группы линка (`Link`), маркер `? R1` для `LinkGroup = -1`
- [x] Настройки отображения: `ShowEmptySockets`, `ShowExp`, `CompactMode`
- [x] Размер/позиция окна, `SetNextWindowSizeConstraints`, `ImGuiTableFlags` (Borders/RowBg/Resizable/ScrollY)
- [ ] **Ручной тест в игре:** читаемость колонок, группировка, компактный режим, Exp
- [ ] **Ручной тест в игре:** проверить, что кириллица в заголовках отображается (риск R9)

### M4a. Оверлей: рамки предметов + список гемов (безопасный этап) ✅ (код + сборка)
- [x] `Layout/EquipmentLayout.cs`: маппинг `InventorySlotE` → `InventoryIndex`, rect через `InventoryPanel.GetElementSlot(index)?.GetClientRectCache`
- [x] Подключить SharpDX в csproj (`SharpDX.dll`, `SharpDX.Mathematics.dll`) для `Graphics.DrawFrame`/`DrawText`
- [x] `Rendering/GemOverlay.cs`: рамка предмета (жёлтая при наличии гемов, серая иначе), рядом компактный список `Имя Lxx [Qxx]`
- [x] Настройки: `ShowOverlay` (по умолчанию false), `OverlayShowGemNames` (true), `OverlayOnlyWithGems` (true)
- [x] Скрывать, если `InventoryPanel` не виден, rect пустой или слот не найден
- [x] Всё в try/catch: оверлей не ломает Render
- [x] Развязать с `ShowDebugSnapshot` (окно и оверлей независимы)
- [x] Высота строки через `MeasureText("Ag").Y + 2`
- [ ] **Ручной тест в игре:** рамки на слотах, подписи, поведение у краёв экрана, перетаскивание панели

### M4b. Оверлей: рамки сокетов и линии линков (после ручного теста)
- [ ] `SocketLayout`: прямоугольники сокетов внутри предмета (порт из Walker; только нужные классы/размеры)
- [ ] Рамки каждого сокета по цвету + подпись `Имя Lxx`
- [ ] Линии линков между центрами сокетов (по `LinkGroup`)
- [ ] Настройка «включить посокетный оверлей» (по умолчанию false; M4a — основной режим)
- [ ] Проверить на разных классах предметов и UI-масштабах

### M6. Прогресс опыта гема (Stage A) ✅ (код + сборка)
- [x] Оффсеты `SkillGemOffsets` через `Marshal.OffsetOf` (330.5: `ExperiencePrevLevel=0x20`, `ExperienceMaxLevel=0x24` относительно компонента)
- [x] Читать `prev`/`max` напрямую из памяти (`gc.Memory.Read<uint>`), обходя баг `SkillGem.ExperiencePrevLevel`
- [x] `ExpPercent` (float, 0..100), `IsMaxLevel`, `ExpPrev`, `ExpMax` в `GemSocketSnapshot`
- [x] `ContentHash` учитывает округлённый `ExpPercent`
- [x] Таблица: колонка `Exp` показывает `42.7%` / `MAX`; tooltip с `total/prev/max`
- [x] Оверлей: `OverlayShowExp` (по умолчанию true) добавляет `%` / `MAX` к строке гема
- [x] `ShowExp` по умолчанию `true`
- [ ] Ручной тест: % растёт при убийствах, на 20 уровне — `MAX`
- [x] **Баг из игры:** проценты неверные → оффсеты `ExperiencePrevLevel/MaxLevel` в 330.5 могут быть сдвинуты
- [x] Самокалибровка пары `(prev, max)` по таблице cumulative XP обычных гемов (уровни 2..19)
- [x] Fallback-расчёт процента по `TotalExpGained` + `Level` + таблице
- [x] Fallback по `ExperienceToNextLevel` (различение «осталось»/«требуется» по расстоянию)
- [x] Tooltip: `L{level}/{maxLevel} total/prev/max next` для проверки

### M7. Посокетная геометрия (M4b) ✅ (код + сборка + harness)
- [x] Добавить в снимок `SizeX`, `SizeY`, `Columns`, `Rows`, `InventoryTypeE`, `TotalSockets`
- [x] Порт `SocketLayout` из Walker (`GetSocketsRectangle`, `GetSplitCount`, `SplitRectangleWithSocketsVortical`, `SplitRectangleToGrid`, `HalveRectangle`)
- [x] Оверлей: рамки сокетов по цвету + индекс, линии линков, подписи с индексом сокета
- [x] Настройки `OverlaySocketFrames`, `OverlayLinkLines`
- [x] Механический тест: 126 реалистичных комбинаций (2×2/2×3/2×4 × 1–6 сокетов × типы) — 0 ошибок
- [ ] **Ручной тест:** 4-link броня, 6-socket броня, 2×4 оружие с 3/5/6 сокетами, шлем/перчатки/ботинки
- [ ] **Ручной тест:** линии линков совпадают с игрой; цвета рамок совпадают с цветами сокетов
- [ ] **Ручной тест:** при `TotalSockets > записей в снимке` недостающие рамки серые, без исключений
- [ ] Калибровка долей/отступов, если рамки смещены относительно арта предмета

### M5. Полировка
- [ ] Настройки стилей (цвета, толщина, шрифт/размер) — по результатам теста
- [x] Горячие клавиши: окно / оверлей (`HotkeyNode`, по умолчанию `Keys.None`)
- [x] Кнопка «DumpSnapshot» → `GameController.InspectObject(_snapshot, ...)` для диагностики
- [ ] Переход с `HotkeyNode` на `HotkeyNodeV2` (в 330.5 `HotkeyNode` помечен `[Obsolete]`)
- [ ] Обработка смены набора оружия (Weapon2/Offhand2) — визуальная пометка неактивного
- [ ] Переключатель языка подписей (R9: если кириллица не отображается)
- [ ] Настройка высоты строки/отступа оверлея
- [x] Смещение блока текста гемов по X/Y (`OverlayTextOffsetX/Y`, пиксели)
- [x] Индивидуальное смещение текста на каждый слот (`TextOffsets.<Slot>.X/Y`)
- [x] Линии от строки текста к сокету (`OverlayTextToSocketLines`)
- [x] Подсветка сокета с гемом максимального уровня (`OverlayHighlightMaxLevelGems`, цвет `OverlayMaxLevelColor`)
- [ ] Счётчик ошибок чтения в статистике
- [ ] Проверка на 3440x1440 / 125–150 % UI scale

---

## 7. Риски (фиксируем, решаем по ходу)

| ID | Риск | Влияние | План |
|---|---|---|---|
R1 | `Sockets.SocketInfo` пуст, если `LinkSizes.ElementCount > 6` (мусор в памяти) | нет данных о сокетах | fallback в M2: `SocketList` + `SocketedGems`, `LinkGroup = -1` |
R2 | Сущности живут один кадр, `Address` меняется при вставке/извлечении гема | «прыгающие» данные | снимок + пересборка по смене адреса/хэша |
R3 | Геометрия сокетов — эвристика (`SocketLayout`), зависит от класса предмета и UI | неточные рамки | начать с таблицы (M3), оверлей — опционально; при расхождении — отключаемые рамки |
R4 | Reflection `InventoryPanel.AllInventories` / `Children[3]` ломается между версиями | падение/пустой список | не использовать; `PlayerInventories` + UI-элементы по `InventoryElement` |
R5 | `TryGetComponent<Quality>()` может отсутствовать у не-гемов в сокете | пустое имя/поля | читать `Base` независимо, остальное — опционально |
R6 | Позиции слотов из `Slots.json` привязаны к разрешению/масштабу | смещение рамок | хранить как относительные координаты (как Walker), пересчитывать от `InventoryPanel` |
R7 | Старый `GemHelper.csproj` (net8 + ImGui.NET) несовместим с 330.5 | не собирается/не грузится | ✅ решено в M0: net10 + Hexa, сборка 0/0 |
R8 | Смена языка игры меняет `Base.Name` | сравнения имён ломаются | для сравнений — `BaseItemTypes.Translate(path).BaseName`/`ClassName` |
R9 | Шрифт HUD может не содержать кириллицу | русские заголовки таблицы отображаются как `?` | проверить в игре; при проблеме — английские подписи или переключатель языка |
R10 | `GetElementSlot`/`GetClientRectCache` могут вернуть пустой rect или элемент | оверлей рисуется не там или падает | проверять `IsValid`/размеры, рисовать только валидные слоты, всё в try/catch |
R11 | `GetElementSlot` может давать rect с отступом/рамкой, отличным от слотовой геометрии Walker | посокетные рамки смещены на несколько px | калибровка долей/offsets в `SocketLayout` по результатам теста |
R12 | Walker-код для 5 сокетов в 2×3 мог писать `data[5]` за границей | один сокет без рамки | порт обрезает массив до `NumberOfSockets`; проверить 5L body armour |
R13 | `InventoryTypeE` у щитов/offhand может быть `Offhand`, а не `Weapon` | выбрана другая ветка раскладки | проверено harness'ом на обоих типах; сверить в игре |
R14 | `InventoryPanel.GetElementSlot(InventoryIndex)` в 330.5 отдаёт неверные элементы (индексы слотов смещены) | рамка рисуется только у одного слота (offhand), остальные не находятся | ✅ исправлено: `VisibleInventoryItems[0]` → Walker-позиции/`Children[3]` → `InventoryUIElement` → `GetElementSlot`; `OverlayDebugSlots` для диагностики |
R15 | `GameOffsets.SkillGemOffsets` может не совпадать с фактическим layout 330.5 (опыт сдвинут) | неверный `%` опыта при корректном уровне | ✅ самокалибровка пары `(prev,max)` по таблице cumulative XP + fallback от `TotalExpGained`/`Level`/`ExperienceToNextLevel` |

---

## 8. Открытые вопросы

| ID | Вопрос | Статус |
|---|---|---|
Q1 | Нужны ли рамки на сокетах или достаточно таблицы? | открыт (решаем после M3) |
Q2 | Показывать ли опыт гема в процентах/абсолюте? | ✅ решено: показываем `%` до следующего уровня (и `MAX` на максимуме), сырые значения — в tooltip |
Q3 | Нужны ли фильтры (например, «только 6-link», «только гемы < 20 lvl»)? | открыт (ItemFilterLibrary как опция) |
Q4 | Показывать ли гемы в неактивном наборе оружия? | открыт (по умолчанию — да, помечать) |
Q5 | Нужна ли поддержка 3.24-версии Walker-геометрии? | неактуально для 330.5 |
Q6 | Целевая версия API: только 330.5 или совместимость с 3.24? | по умолчанию 330.5 |

---

## 9. Журнал изменений

### 2026-11-20
- Проведён анализ Walker: цепочка `Entity → Sockets.SocketInfo → SocketedGemEntity → Base/SkillGem/Quality`, геометрия сокетов, `GemWorld`/`PhysicalGemRegistry`, пример рендера.
- Проведён анализ WheresMyCraftAt: доступ к предметам через `PlayerInventories` и UI-элементы, `Address`-идентичность, `ItemFilterLibrary` (`SocketInfo`, `GemInfo`, `PlayerInfo`), паттерны настроек и асинхронности.
- Определена архитектура GemHelper (M0–M5), список рисков и открытых вопросов.
- Создан этот файл.
- Код плагина не изменялся.

### 2026-11-20 — M0 + M1 (senior-developer) + ревью
- M0: `GemHelper.csproj` переведён на `net10.0-windows` + `Hexa.NET.ImGui`; путь к DLL — свойство `ExileApiDir` (дефолт `..\..\..`, переопределяется через `-p:`).
- M1: добавлены `Core/GemSocketSnapshot.cs`, `Core/GemSnapshot.cs`, `Core/GemSnapshotReader.cs`; `GemHelper.cs` строит снимок в `Tick` и рисует отладочную панель в `Render`; в настройках `ShowDebugSnapshot` и `UpdateIntervalMs`.
- Ревью: сборка 0 ошибок / 0 предупреждений; отдельно проверена сборка с global properties загрузчика (`OutputPath`, `exapiPackage`, `RuntimeIdentifier`, `SelfContained`, `PathMap`, `TargetFramework`) — успешно.
- Зафиксированы факты: `ServerInventory.Item` — индексатор; загрузчик принудительно ставит `net10.0-windows` и кэширует сборку по манифесту.
- Ручной тест в игре — ожидает выполнения (см. чеклист в §12).

### 2026-11-20 — M2 (senior-developer) + ревью
- `GemSnapshotReader`: добавлен `CaptureServerCounter` (хэш `(InventSlot, ServerRequestCounter)`), рефакторинг билдера сокета, R1 fallback через `SocketList` + `SocketedGems`.
- `GemHelper`: кэш по счётчику, `FullRefreshIntervalMs`, Stopwatch-статистика (`capture/reads/skip/counter/hash`), переиспользование снимка при том же `ContentHash`.
- Настройки: `FullRefreshIntervalMs` (500..10000, по умолчанию 2000).
- Ревью: обе сборки (Debug и имитация загрузчика) — 0/0; замечание про последовательный запуск сборок зафиксировано.
- Backlog: частичный `SocketInfo` (меньше `NumberOfSockets`) — добирать остаток; сохранять последний удачный снимок при временной ошибке чтения.

### 2026-11-20 — M3 (senior-developer) + ревью
- `GemHelper.Render` разбит на `DrawStatistics` + `DrawSocketsTable`; добавлены палитра цветов сокетов и строки-заголовки предметов.
- Таблица: `Слот | # | Цвет | Link | Гем | Ур | К | Req | Exp`, группировка по `(Slot, ItemAddress)` линейным проходом, маркер `? R1` для `LinkGroup = -1`.
- Настройки: `ShowEmptySockets`, `ShowExp`, `CompactMode` (цвет переносится в колонку Link как `R·0`).
- Ревью: обе сборки — 0/0; `GemHelper.dll` ссылается на `ExileCore`, `Hexa.NET.ImGui`, `System.Numerics.Vectors` и BCL.
- M4 разделён на M4a (рамки предметов + список гемов, безопасно) и M4b (посокетная геометрия, после теста).

### 2026-11-20 — M4a (senior-developer) + ревью
- Добавлены `Layout/EquipmentLayout.cs` (слот → `InventoryIndex` → rect) и `Rendering/GemOverlay.cs` (рамка + подписи гемов).
- Настройки: `ShowOverlay=false`, `OverlayShowGemNames=true`, `OverlayOnlyWithGems=true`; в csproj добавлены `SharpDX`/`SharpDX.Mathematics`.
- Ревью нашло связку оверлея с `ShowDebugSnapshot` — исправлено: `Render` вызывает `DrawDebugWindow` и `GemOverlay.Draw` независимо.
- Высота строки подписей теперь вычисляется через `MeasureText`, а не фиксированные 14 px.
- Обе сборки — 0/0. Остался ручной тест в игре.

### 2026-11-20 — M5a (senior-developer) + ревью
- Добавлены хоткеи `ToggleWindowHotkey`/`ToggleOverlayHotkey` (по умолчанию `Keys.None`), регистрация через `Input.RegisterKey` + перерегистрация по `OnValueChanged`.
- Добавлена кнопка `DumpSnapshotButton` → `GameController.InspectObject(_snapshot, "GemHelper snapshot")`.
- Хоткеи обрабатываются в `Tick` до проверки `InGame` (окно/оверлей можно переключать в меню); при `Enable = false` Tick не вызывается — это поведение HUD.
- `HotkeyNode` в 330.5 помечен `[Obsolete]`; используется с точечным `#pragma warning disable CS0618`. Технический долг: перейти на `HotkeyNodeV2`.
- Обе сборки — 0/0.

### 2026-11-20 — M6 (senior-developer) + ревью
- Оффсеты `SkillGemOffsets.ExperiencePrevLevel/ExperienceMaxLevel` вычисляются через `Marshal.OffsetOf` (0x20/0x24 в 330.5) и читаются напрямую из памяти.
- В снимок добавлены `ExpPercent`, `IsMaxLevel`, `ExpPrev`, `ExpMax`; `ContentHash` учитывает округлённый процент.
- Таблица: `42.7%` / `MAX`, tooltip `total/prev/max`; оверлей: `OverlayShowExp` (по умолчанию true).
- `ShowExp` по умолчанию `true`; обе сборки — 0/0.

### 2026-11-20 — фикс процента опыта гема (R15)
- Симптом из игры: проценты прокачки неверные; уровень при этом корректен.
- Добавлена таблица cumulative XP обычных гемов (1..20) и самокалибровка пары `(prev, max)` сканированием `0x18..0x60` при первом геме уровня 2..19.
- Fallback: расчёт по `TotalExpGained` + `Level` + таблица; дополнительно `ExperienceToNextLevel` с различением «осталось/требуется» по расстоянию.
- В снимок добавлены `MaxLevel`, `ExpToNext`; tooltip Exp показывает `L{level}/{maxLevel} total/prev/max next`.
- Обе сборки — 0/0. Нужен тест в игре (см. tooltip).

### 2026-11-20 — M4b (senior-developer) + ревью
- Добавлен `Layout/SocketLayout.cs` — порт `GetSocketsRectangle` и хелперов Walker с заменой `ServerInventory` на заранее прочитанные размеры.
- Снимок расширен геометрией: `ItemSizeX/ItemSizeY/InventoryColumns/InventoryRows/InventoryType/TotalSockets`; в reader — `EquippedItemInfo` с fallback `Columns/Rows ↔ SizeX/SizeY`.
- Оверлей: `DrawSocketGeometry` — рамки сокетов (цвет из снимка, отсутствующие — серые), линии линков по `LinkGroup`, префикс `[SocketIndex]` в подписях; настройки `OverlaySocketFrames`/`OverlayLinkLines` (по умолчанию true).
- Механический harness (без загрузки защищённого ExileCore): 126 комбинаций 2×2/2×3/2×4 × 1–6 сокетов × типы — 0 ошибок/исключений, длины и границы корректны.
- Обе сборки — 0/0. Осталась визуальная калибровка в игре (R11–R13).

### 2026-11-20 — фикс поиска слотов (R14)
- Симптом из игры: рамка обводила только слот оффхенда.
- `EquipmentLayout` переписан: приоритет `panel[index].VisibleInventoryItems[0].GetClientRectCache`, затем Walker-позиции + `Children[3]`, затем `InventoryUIElement`, затем `GetElementSlot` (последний, т.к. индексы в 330.5 смещены).
- `OverlayOnlyWithGems` по умолчанию `false`; добавлен `OverlayDebugSlots` (циан-подпись слота).
- Обе сборки — 0/0. Нужен повторный тест в игре.

### 2026-11-20 — смещение текста и линии к сокетам
- Добавлены `OverlayTextOffsetX/Y` (RangeNode<int>, −300..300): сдвиг всего блока подписей гемов.
- Добавлен `OverlayTextToSocketLines` (по умолчанию true): линия от каждой строки текста к центру её сокета; цвет линии = цвет сокета.
- `DrawSocketGeometry` теперь возвращает прямоугольники сокетов; anchor линии выбирается по стороне текста (слева/справа от предмета).
- Обе сборки — 0/0.

### 2026-11-20 — индивидуальные оффсеты по слотам
- Добавлено подменю `TextOffsets` с 12 слотами (`Helm1 … Offhand2`), у каждого `X/Y` (RangeNode<int>, −300..300).
- Итоговое смещение текста = общий `OverlayTextOffsetX/Y` + `TextOffsets.<Slot>.X/Y`; линии к сокетам считаются от итоговой позиции.
- Обе сборки — 0/0.

---

## 10. Шпаргалка API (для быстрой сверки)

```csharp
// Перечисления
using ExileCore.Shared.Enums;          // InventorySlotE, SocketColor, InventoryNameE, MouseActionType

// Компоненты
using ExileCore.PoEMemory.Components;  // Sockets, Base, SkillGem, Quality, Mods

// Данные сервера
GameController.IngameState.ServerData.PlayerInventories   // IList<InventoryHolder>
holder.Inventory.InventSlot                               // InventorySlotE
holder.Inventory.InventorySlotItems[0].Item               // Entity
holder.Inventory.InventorySlotItems[0].PosX/PosY          // сетка
holder.Inventory.InventorySlotItems[0].GetClientRect()    // RectangleF

// UI
GameController.IngameState.IngameUi.InventoryPanel
GameController.IngameState.IngameUi.StashElement?.VisibleStash?.VisibleInventoryItems
GameController.IngameState.IngameUi.Cursor?.Action        // MouseActionType

// Сокеты
sockets.NumberOfSockets
sockets.SocketInfo                                        // List<Sockets.Socket>
sockets.SocketedGems                                      // List<Sockets.SocketedGem>
// Socket: SocketColor, LinkGroup, SocketedGemIndex (-1 = пусто), SocketedGemEntity, GemAddress

// Гем
base.Name
skillGem.Level / RequiredLevel / TotalExpGained
quality.ItemQuality                                       // int

// Датамайн
GameController.Files.BaseItemTypes.Translate(entity.Path) // ClassName, BaseName, Width, Height, Tags

// ItemFilterLibrary (опционально)
var data = new ItemData(entity, GameController);
data.SocketInfo.SocketedGems; data.GemInfo.Level; data.PlayerInfo.EquippedItems;
```

### Цвета сокетов → отображение
| SocketColor | Значение | Цвет |
|---|---|---|
`Red` | 1 | красный |
`Green` | 2 | зелёный |
`Blue` | 3 | синий |
`White` | 4 | белый |
`Abyssal` | 5 | фиолетовый |
`Resonator` | 6 | жёлтый/оранжевый |

---

## 11. Сборка и запуск (330.5)

Фактический `GemHelper.csproj`:

```xml
<TargetFramework>net10.0-windows</TargetFramework>
<ExileApiDir Condition="'$(ExileApiDir)' == ''">$(MSBuildProjectDirectory)\..\..\..</ExileApiDir>
<Reference Include="ExileCore"><HintPath>$(ExileApiDir)\ExileCore.dll</HintPath><Private>False</Private></Reference>
<Reference Include="GameOffsets"><HintPath>$(ExileApiDir)\GameOffsets.dll</HintPath><Private>False</Private></Reference>
<Reference Include="Hexa.NET.ImGui"><HintPath>$(ExileApiDir)\Hexa.NET.ImGui.dll</HintPath><Private>False</Private></Reference>
<Reference Include="SharpDX"><HintPath>$(ExileApiDir)\SharpDX.dll</HintPath><Private>False</Private></Reference>
<Reference Include="SharpDX.Mathematics"><HintPath>$(ExileApiDir)\SharpDX.Mathematics.dll</HintPath><Private>False</Private></Reference>
<ItemGroup><Using Include="Hexa.NET.ImGui" /></ItemGroup>
```

- `ExileCore.dll` 330.5 собран под `.NET 10` и `Hexa.NET.ImGui 3.1.0-test1` (см. `ExileCore.deps.json`).
- Переменная окружения `exapiPackage` в системе указывает на **3.24** — специально не используем её; путь считается от папки проекта.
- Загрузчик 330.5 (`PluginCompiler`, декомпиляция) при сборке сам задаёт:
  - `exapiPackage = <корень HUD>`
  - `OutputPath = <HUD>/Plugins/Temp/<Plugin>`
  - `RuntimeIdentifier = win-x64`, `SelfContained = False`, `PathMap = ""`
  - принудительно `TargetFramework = net10.0-windows` (для не-net4 проектов)
  - `Restore` + `Build` через MSBuild, кэш по хэшам файлов в `Plugins/Temp/<Plugin>/__compilationManifest.json`
- Для отладки удобно: `GameController.InspectObject(obj, "title")`.

### Команды проверки

```powershell
# Обычная сборка
dotnet build "D:\GH\ExileApi-Compiled-330.5\Plugins\Source\GemHelper\GemHelper.csproj" -c Debug

# Имитация сборки загрузчиком (те же global properties, что у PluginCompiler)
dotnet build "D:\GH\ExileApi-Compiled-330.5\Plugins\Source\GemHelper\GemHelper.csproj" -c Release `
  -p:exapiPackage="D:\GH\ExileApi-Compiled-330.5" `
  -p:OutputPath="C:\Users\hk222\AppData\Local\Temp\opencode\gemhelper_loader_sim" `
  -p:RuntimeIdentifier=win-x64 -p:SelfContained=False -p:PathMap= -p:TargetFramework=net10.0-windows
```

---

## 12. Заметки ревью M0–M1

- `ServerInventory.Item` — это **индексатор** `this[int x, int y]`, а не свойство «предмет целиком». Fallback в reader сделан через `ServerInventory.Items[0]`; при необходимости точного доступа — `inventory[x, y]`.
- `InventorySlotE`: `Weapon2 = 14`, `Offhand2 = 15` (проверено по `ExileCore.Shared.Enums`).
- `Sockets.Socket` — публичные **поля** (`SocketColor`, `LinkGroup`, `SocketedGemIndex`, `SocketedGemEntity`, `GemAddress`), не свойства.
- `Sockets.SocketedGem`: `GemEntity`, `SocketIndex`.
- `Quality.ItemQuality` — уже `int` (без `int.Parse`).
- `SkillGem.RequiredLevel` вычисляется через `GrantedEffect` и может кидать — обёрнут в try/catch.
- `GemHelper.dll` ссылается только на `ExileCore`, `Hexa.NET.ImGui` и BCL (`System.Runtime`, `System.Collections`, `System.Threading`) — SharpDX/GameOffsets не тянутся, это хорошо для загрузчика.
- `Hexa.NET.ImGui`: `ImGui.Text` — format-string; весь пользовательский текст выводится через `TextUnformatted`.
- `Render` вызывается только при `IsEnable && CanRender && InGame` (см. `Core.cs` 330.5), поэтому отдельная проверка `Enable` в `Render` не нужна.
- **Важно:** поставляемый `ExileCore.dll` (и 330.5, и 3.24) содержит **заглушки IL** (`ldarg.0; ret`) в телах методов (защита/обфускация); реальные тела восстанавливаются загрузчиком при запуске. Для разработки используем метаданные (сигнатуры/поля), а поведение сверяем по декомпиляции 3.24 (`D:\GH\dnSpy\ExileCore`) и исходникам `D:\GH\PoeAPI\Core`.

### M2 (ревью)

- `ServerRequestCounter`-гейт читает только инвентари игрока (хэш пар `(InventSlot, Counter)`), поэтому снимок перечитывается при подборе лута — это ожидаемо; в покое `skip` растёт.
- `HashCode` рандомизирован в пределах процесса; между запусками значение `counter` нестабильно — это не проблема, т.к. `_hasServerCounter` сбрасывается.
- Если `Capture` вернёт пустой снимок из-за временной ошибки, панель мигнёт «пусто» до следующего `FullRefreshIntervalMs` (2 с). При необходимости — сохранять последний удачный снимок (backlog).
- **Сборки запускать последовательно:** параллельный `dotnet build` одного проекта ломает `obj/project.assets.json` (NETSDK1047 про `net10.0-windows/win-x64`).
- Известное ограничение: `SocketInfo` может быть **частичным** (меньше `NumberOfSockets`) — добирание остатка через fallback вынесено в backlog M2.

### M3 (ревью)

- Таблица: колонки считаются динамически (`6 + 2 + Exp`), порядок `TableSetupColumn` совпадает с числом колонок; ID таблицы включает число колонок, чтобы переключение `CompactMode`/`ShowExp` не ломало ImGui.
- `TextColored` в Hexa — **не** printf на уровне обёртки (`TextColoredNative(Vector4, byte*)`), но экранирование `%` оставлено как страховка от ImGui-форматирования; `TextUnformatted` используется для шапки.
- `TableSetBgColor(RowBg0, ...)` вызывается сразу после `TableNextRow()` — корректно для строки-заголовка предмета.
- `ScrollY` + `SizingFixedFit`: высота таблицы = `GetContentRegionAvail().Y`, шапка таблицы липкая; при высоте < 32 px таблица не рисуется.
- Осталось проверить в игре: кириллица (R9) и читаемость на широких/узких разрешениях.

### M4a (ревью)

- Найден и исправлен баг: оверлей вызывался в конце `Render` после раннего `return` по `!ShowDebugSnapshot`; теперь `ShowDebugSnapshot` управляет только ImGui-окном, `ShowOverlay` — только оверлеем.
- `Graphics` в 330.5:
  - `DrawFrame(SharpDX.RectangleF, SharpDX.Color, int)` — есть;
  - `DrawTextWithBackground(string, System.Numerics.Vector2, SharpDX.Color, SharpDX.Color)` — вектор `System.Numerics`, цвет `SharpDX`;
  - `MeasureText(string)` → `System.Numerics.Vector2`.
  Поэтому в `GemOverlay` используются алиасы (`Vector2 = System.Numerics.Vector2`, `Color = SharpDX.Color`, `RectangleF = SharpDX.RectangleF`).
- Высота строки подписей теперь вычисляется через `MeasureText("Ag").Y + 2` (не жёсткие 14 px).
- `EquipmentLayout` использует только UI: `InventoryPanel.GetElementSlot(InventoryIndex)` → `GetClientRectCache`; чтения памяти предметов в Render нет.

### Чеклист ручного теста в игре

1. Включить `GemHelper` в HUD → должна появиться панель «GemHelper».
2. Надеть предмет с 4+ сокетами и гемами → проверить: слот, индекс сокета, цвет, `link`, имя/уровень/качество гема.
3. Пустые сокеты должны быть `empty`, без ложных гемов.
4. Сменить предмет (надеть другой) → снимок обновляется не позднее `UpdateIntervalMs`.
5. Переключить набор оружия → проверить `Weapon2`/`Offhand2` (если holder'ы приходят).
6. Оставить плагин на 5–10 минут → нет роста лога/ошибок, FPS не просаживается.
7. Включить `ShowOverlay` → открыть инвентарь: рамки на слотах с гемами, подписи `Имя Lxx` читаемы, не выходят за края экрана.
8. Отключить `ShowDebugSnapshot` при включённом `ShowOverlay` → оверлей продолжает рисоваться (проверка развязки).
9. Перетащить/перемасштабировать панель инвентаря → рамки остаются на слотах (или корректно исчезают).
10. Назначить хоткеи окна/оверлея в настройках → переключение работает без конфликтов.
11. Нажать `DumpSnapshotButton` → открывается инспектор HUD со структурой снимка (для диагностики).
12. Посокетные рамки: 6-link броня (6 рамок + 5 линий), 4-link, 2×4 оружие (3/5/6 сокетов), шлем/перчатки/ботинки.
13. Цвета рамок совпадают с сокетами; пустые/недостающие — серые, без исключений.
14. Опыт: в таблице `Exp` показывает `xx.x%`/`MAX`; в оверлее `Имя L20 42%`; tooltip содержит `total/prev/max`.
15. Убить мобов с надетым гемом → процент растёт без перезапуска (в пределах `FullRefreshIntervalMs`).
16. `OverlayTextOffsetX/Y`: блок подписей двигается целиком; при наезжании текстов подобрать смещение.
17. `OverlayTextToSocketLines`: от каждой строки идёт линия к её сокету; при выключении линии исчезают.
18. `TextOffsets.<Slot>.X/Y`: сдвиг конкретного слота не влияет на остальные; общий `OverlayTextOffsetX/Y` суммируется с индивидуальным.
19. MAX-гем: вокруг его сокета внешняя золотая рамка, в таблице `MAX` золотым, подпись/линия в оверлее золотые; при `OverlayHighlightMaxLevelGems=false` подсветка исчезает.

---

## 13. Следующее

1. **Ручной тест в игре** (обязательно перед M4b): таблица, оверлей, кириллица, кэш (`counter`/`skip`), второй набор оружия.
2. Если оверлея-рамки предмета достаточно — перейти к M5 (полировка/настройки).
3. Если нужна посокетная точность — M4b по плану §15 (сначала добавить `SizeX/SizeY/InventoryTypeE` в снимок, затем порт геометрии и калибровочный режим).

---

## 14. Исследование для M4 (геометрия)

Проверено по метаданным `ExileCore.dll` 330.5:

- `InventoryElement` (это `GameController.IngameState.IngameUi.InventoryPanel`) имеет:
  - `Inventory this[InventoryIndex index]` (`get_Item(InventoryIndex)`)
  - `Element GetElementSlot(InventoryIndex index)`
  - `IList<...> GetItemsInInventory()`
- `InventoryIndex` включает слоты экипировки: `Helm, Amulet, Chest, LWeapon, RWeapon, LWeaponSwap, RWeaponSwap, LRing, RRing, Gloves, Belt, Boots, Flask, Trinket` и др.
- `Inventory` (UI) содержит `ServerInventory`, `InventoryUIElement`, `VisibleInventoryItems`, `SubInventories`.

Вывод: для рамок слотов **не нужны** `Slots.json` и Reflection `AllInventories` (как в Walker). Достаточно:

```csharp
var panel = GameController.IngameState.IngameUi.InventoryPanel;
var slotElement = panel.GetElementSlot(InventoryIndex.Helm);
var rect = slotElement.GetClientRectCache;
```

Остаётся геометрия **внутри** слота: позиции сокетов зависят от класса и размера предмета — либо портировать `SocketLayout`/`GetSocketsRectangle` из Walker, либо (проще) рисовать рамку всего предмета + список гемов. Решение примем после M3 и ручного теста.

---

## 15. Подготовка M4b (посокетная геометрия)

Чтобы посчитать прямоугольники сокетов **без чтения памяти игры в Render**, нужно заранее (в `Tick`) сохранить в снимок метаданные предмета:

- `SizeX`, `SizeY` — из `ServerInventory.InventorySlotItems[0]` (`InventSlotItem.SizeX/SizeY`);
- `Columns`, `Rows` — из `ServerInventory.Columns/Rows` (нужны ветке `splitCount 2x2` в Walker);
- `InventoryTypeE` — из `ServerInventory.InventType`;
- `TotalSockets` — из `Sockets.NumberOfSockets` (может быть больше, чем записей в `SocketInfo`).

Добавить в `GemSocketSnapshot` необязательными параметрами (после полей M6):
```csharp
int ItemSizeX = 0,
int ItemSizeY = 0,
int InventoryColumns = 0,
int InventoryRows = 0,
InventoryTypeE InventoryType = InventoryTypeE.MainInventory,
int TotalSockets = 0
```

Порт из `D:\GH\ExileApi-Compiled-3.24\Plugins\Source\fix\Walker\Extensions\SocketLayout.cs`:

```csharp
public static RectangleF[] GetSocketRectangles(
    RectangleF slotRectangle,
    int numberOfSockets,
    int sizeX,
    int sizeY,
    int inventoryColumns,
    int inventoryRows,
    InventoryTypeE inventoryType)
```

Замены при порте:
- `componentSockets.NumberOfSockets` → `numberOfSockets`;
- `serverInventory.InventorySlotItems[0].SizeX/SizeY` → `sizeX/sizeY`;
- `serverInventory.Columns/Rows` → `inventoryColumns/inventoryRows`;
- убрать `serverInventory`/`componentSockets` из сигнатуры.

Портировать helpers:
- `GetSplitCount` (строки 705–742 Walker);
- `SplitRectangleWithSocketsVortical` + `GetVerticalPosition` + `VerticalPosition` (744–812);
- `SplitRectangleToGrid` из `WHelper.RectangleHelper` (68–90);
- `HalveRectangle` (842–849).

Важно: Walker возвращает «клетки», а `EquippedItemResolver` затем применяет `HalveRectangle` — в оверлее применить **один раз**.

Отрисовка:
- рамки всех `TotalSockets` сокетов (цвет из снимка по индексу; отсутствующие — серые);
- линии линков: группировать по `LinkGroup >= 0`, соединять соседние по `SocketIndex` центры;
- подписи оставить сбоку, но добавить `[SocketIndex]` в начало строки, чтобы связать с рамкой;
- настройки `OverlaySocketFrames` (true) и `OverlayLinkLines` (true).

План:
1. Расширить снимок геометрией предмета.
2. Портировать `SocketLayout` чистым статическим классом.
3. Отрисовка рамок/линий в `GemOverlay`.
4. Калибровка в игре: если рамки смещены — добавить `OverlaySocketScale`/offsets.
5. После проверки решить, нужны ли M4b-настройки в UI.

### Статус M4b

- Порт выполнен: `Layout/SocketLayout.cs` (см. M7).
- Механический harness: 126 комбинаций `2×2/2×3/2×4` × `1..6` сокетов × `BodyArmour/Weapon/Offhand/Helm/Gloves/Boots/MainInventory` — 0 ошибок, 0 исключений, все прямоугольники в границах слота.
- Комбинации `sizeX = 1` с 4+ сокетами не проверялись: в PoE таких сокетных предметов нет (`GetSplitCount` для них возвращает `(0,0)`).

### Если рамки смещены в игре

Порядок калибровки:
1. Сравнить рамку предмета (`rect` из `GetElementSlot`) с артом: возможно, нужен инсет `+2..+10 px` по краям.
2. Проверить глубину `HalveRectangle`: Walker в некоторых путях применял её трижды; сейчас — один раз.
3. Подправить доли в `SplitRectangleToGrid(rect2, 7, 3)` / `(3, 3)` / `(9, 3)` / `(10, 3)` — именно они задают позицию внутри ячейки.
4. Для 1–2 сокетов при необходимости вернуть спец-кейс Walker (10×10 по центру).

---

## 16. Быстрый старт для теста в игре

1. Перезапустить HUD (Loader) — он сам пересоберёт `Plugins/Source/GemHelper` в `Plugins/Temp/GemHelper`.
2. Включить `GemHelper` в списке плагинов.
3. Настройки:
   - `ShowDebugSnapshot` — окно с таблицей (по умолчанию включено);
   - `ShowOverlay` — оверлей на панели инвентаря (по умолчанию выключено);
   - `OverlaySocketFrames` / `OverlayLinkLines` — посокетные рамки и линии линков (по умолчанию включены);
   - `OverlayShowGemNames` / `OverlayOnlyWithGems` / `OverlayShowExp` — подписи и фильтр оверлея (`OverlayOnlyWithGems` теперь по умолчанию `false` — видны все предметы с сокетами);
   - `OverlayTextOffsetX` / `OverlayTextOffsetY` — общий сдвиг блока подписей гемов (пиксели), если тексты наезжают;
   - `TextOffsets` — индивидуальный сдвиг `X/Y` для каждого слота (`Helm1`, `BodyArmour1`, …, `Offhand2`); суммируется с общим;
   - `OverlayTextToSocketLines` — линии от строк текста к сокетам;
   - `OverlayHighlightMaxLevelGems` / `OverlayMaxLevelColor` — внешняя рамка вокруг сокета с MAX-гемом, золотой `MAX` в таблице;
   - `OverlayDebugSlots` — подписи найденных слотов для диагностики;
   - `ShowEmptySockets`, `ShowExp`, `CompactMode` — вид таблицы (`ShowExp` по умолчанию включён);
   - `UpdateIntervalMs` / `FullRefreshIntervalMs` — частота чтения;
   - `ToggleWindowHotkey` / `ToggleOverlayHotkey` — хоткеи;
   - `DumpSnapshotButton` — открыть инспектор HUD со структурой снимка.
4. Открыть инвентарь (`I`) → проверить таблицу, оверлей и посокетные рамки по чеклисту §12.
5. Если рамки не на всех слотах — включить `OverlayDebugSlots` (подписи слотов на найденных рамках) и сообщить, какие слоты подписались.

Если что-то не отображается:
- `DumpSnapshotButton` → посмотреть, есть ли в снимке сокеты/гемы;
- проверить статистику: `reads` растёт при смене предметов, `skip` — в покое;
- `counter` должен меняться при надевании/снятии предметов;
- логи HUD: `[GemHelper] ...` (дедуплицированные).

