using ExileCore.Shared.Attributes;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using System.Windows.Forms; // Keys — для HotkeyNode

namespace GemHelper;

public class GemHelperSettings : ISettings
{
    /// <summary>
    /// Общий выключатель плагина. При false HUD не вызывает Tick/Render.
    /// По умолчанию выключен, чтобы плагин не работал случайно.
    /// </summary>
    public ToggleNode Enable { get; set; } = new ToggleNode(false);

    /// <summary>
    /// Отладочная ImGui-панель «GemHelper»: таблица сокетов надетых предметов и статистика чтения.
    /// </summary>
    public ToggleNode ShowDebugSnapshot { get; set; } = new ToggleNode(true);

    /// <summary>
    /// Показывать пустые сокеты строками таблицы. Заголовок предмета выводится всегда,
    /// даже если все его сокеты пусты и скрыты.
    /// </summary>
    public ToggleNode ShowEmptySockets { get; set; } = new ToggleNode(true);

    /// <summary>Показывать колонку Exp (прогресс гема до следующего уровня, % / MAX).</summary>
    public ToggleNode ShowExp { get; set; } = new ToggleNode(true);

    /// <summary>
    /// Компактный режим: скрыть колонки «Цвет» и «Req». Код цвета при этом остаётся
    /// в колонке «Link» (например, «R·0»), информация о цвете не теряется.
    /// </summary>
    public ToggleNode CompactMode { get; set; } = new ToggleNode(false);

    /// <summary>
    /// Оверлей на панели инвентаря: рамка вокруг надетого предмета и список гемов рядом.
    /// По умолчанию выключен.
    /// </summary>
    public ToggleNode ShowOverlay { get; set; } = new ToggleNode(false);

    /// <summary>Показывать в оверлее имена гемов с уровнем/качеством.</summary>
    public ToggleNode OverlayShowGemNames { get; set; } = new ToggleNode(true);

    /// <summary>
    /// Рисовать только предметы с гемами. По умолчанию выключено: рамки и сокеты видны у всех
    /// найденных предметов, даже пустых.
    /// </summary>
    public ToggleNode OverlayOnlyWithGems { get; set; } = new ToggleNode(false);

    /// <summary>Добавлять прогресс гема (%/MAX) к строке гема в оверлее.</summary>
    public ToggleNode OverlayShowExp { get; set; } = new ToggleNode(true);

    /// <summary>Рисовать рамки отдельных сокетов внутри надетого предмета (M4b).</summary>
    public ToggleNode OverlaySocketFrames { get; set; } = new ToggleNode(true);

    /// <summary>Рисовать линии линков между связанными сокетами (M4b).</summary>
    public ToggleNode OverlayLinkLines { get; set; } = new ToggleNode(true);

    /// <summary>
    /// Диагностика: подписывать найденные слоты их именем (Slot) у левого верхнего угла рамки.
    /// Помогает понять в игре, какие слоты распознаны.
    /// </summary>
    public ToggleNode OverlayDebugSlots { get; set; } = new ToggleNode(false);

    /// <summary>Смещение блока подписей гемов по X, пиксели (-300..300). Помогает развести наезжающий текст.</summary>
    public RangeNode<int> OverlayTextOffsetX { get; set; } = new(0, -300, 300);

    /// <summary>Смещение блока подписей гемов по Y, пиксели (-300..300).</summary>
    public RangeNode<int> OverlayTextOffsetY { get; set; } = new(0, -300, 300);

    /// <summary>Рисовать тонкие линии от каждой строки гема к её сокету.</summary>
    public ToggleNode OverlayTextToSocketLines { get; set; } = new(true);

    /// <summary>Подсвечивать сокеты, в которых гем достиг максимального уровня (внешняя рамка).</summary>
    public ToggleNode OverlayHighlightMaxLevelGems { get; set; } = new(true);

    /// <summary>Цвет подсветки гемов максимального уровня (по умолчанию золотой).</summary>
    public ColorNode OverlayMaxLevelColor { get; set; } = new(new SharpDX.Color(255, 215, 0));

    /// <summary>
    /// Индивидуальные смещения блока подписей по слотам экипировки.
    /// Суммируются с общими <see cref="OverlayTextOffsetX"/> / <see cref="OverlayTextOffsetY"/>.
    /// </summary>
    public SlotTextOffsets TextOffsets { get; set; } = new();

    /// <summary>Смещения текста по каждому слоту экипировки (пиксели, −300..300).</summary>
    [Submenu(CollapsedByDefault = true)]
    public class SlotTextOffsets
    {
        public OffsetXY Helm1 { get; set; } = new();
        public OffsetXY BodyArmour1 { get; set; } = new();
        public OffsetXY Gloves1 { get; set; } = new();
        public OffsetXY Boots1 { get; set; } = new();
        public OffsetXY Belt1 { get; set; } = new();
        public OffsetXY Amulet1 { get; set; } = new();
        public OffsetXY Ring1 { get; set; } = new();
        public OffsetXY Ring2 { get; set; } = new();
        public OffsetXY Weapon1 { get; set; } = new();
        public OffsetXY Offhand1 { get; set; } = new();
        public OffsetXY Weapon2 { get; set; } = new();
        public OffsetXY Offhand2 { get; set; } = new();

        /// <summary>Смещение для конкретного слота (неизвестный слот — как у Helm1).</summary>
        public OffsetXY For(InventorySlotE slot) => slot switch
        {
            InventorySlotE.Helm1 => Helm1,
            InventorySlotE.BodyArmour1 => BodyArmour1,
            InventorySlotE.Gloves1 => Gloves1,
            InventorySlotE.Boots1 => Boots1,
            InventorySlotE.Belt1 => Belt1,
            InventorySlotE.Amulet1 => Amulet1,
            InventorySlotE.Ring1 => Ring1,
            InventorySlotE.Ring2 => Ring2,
            InventorySlotE.Weapon1 => Weapon1,
            InventorySlotE.Offhand1 => Offhand1,
            InventorySlotE.Weapon2 => Weapon2,
            InventorySlotE.Offhand2 => Offhand2,
            _ => Helm1,
        };
    }

    /// <summary>Пара смещений X/Y в пикселях для одного слота.</summary>
    [Submenu(CollapsedByDefault = true)]
    public class OffsetXY
    {
        public RangeNode<int> X { get; set; } = new(0, -300, 300);
        public RangeNode<int> Y { get; set; } = new(0, -300, 300);
    }

    /// <summary>
    /// Интервал пересборки снимка в миллисекундах (50..2000, по умолчанию 250).
    /// Снимок строится только в Tick, поэтому значение влияет лишь на частоту чтения памяти.
    /// </summary>
    public RangeNode<int> UpdateIntervalMs { get; set; } = new RangeNode<int>(250, 50, 2000);

    /// <summary>
    /// Принудительное полное чтение снимка, мс (500..10000, по умолчанию 2000).
    /// Между полными обновлениями снимок перестраивается только при смене ServerRequestCounter,
    /// что защищает от «залипания» данных при изменениях, не меняющих счётчик.
    /// </summary>
    public RangeNode<int> FullRefreshIntervalMs { get; set; } = new RangeNode<int>(2000, 500, 10000);

    /// <summary>
    /// Хоткей показа/скрытия окна с таблицей сокетов.
    /// По умолчанию не назначен (Keys.None), чтобы ничего не перехватывать.
    /// </summary>
#pragma warning disable CS0618 // HotkeyNode помечен [Obsolete] в 330.5, но рабочий и используется другими плагинами HUD
    public HotkeyNode ToggleWindowHotkey { get; set; } = new(Keys.None);

    /// <summary>
    /// Хоткей включения/выключения оверлея на панели инвентаря.
    /// По умолчанию не назначен (Keys.None).
    /// </summary>
    public HotkeyNode ToggleOverlayHotkey { get; set; } = new(Keys.None);
#pragma warning restore CS0618

    /// <summary>
    /// Кнопка «дамп снимка»: открывает текущий GemSnapshot в инспекторе HUD
    /// (GameController.InspectObject) для диагностики.
    /// </summary>
    public ButtonNode DumpSnapshotButton { get; set; } = new();
}
