using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using ExileCore.Shared.Enums;
using RectangleF = SharpDX.RectangleF;
using Vector2 = SharpDX.Vector2;

namespace GemHelper.Layout;

/// <summary>
/// Геометрия слотов экипировки на панели инвентаря (M4a/M4b).
/// Основной путь — порт Walker (<c>EquippedItemResolver.GetSlotRectangle</c>): относительные позиции
/// из Slots.json + поиск дочернего элемента, содержащего точку слота.
/// <c>InventoryPanel.GetElementSlot(InventoryIndex)</c> в 330.5 отдаёт смещённые элементы,
/// поэтому он оставлен только как последний fallback.
/// </summary>
public static class EquipmentLayout
{
    // Отступ от найденной относительной точки, чтобы попасть внутрь прямоугольника слота.
    private const float SlotProbeOffset = 10f;

    // Относительные координаты (0..1) внутри InventoryPanel — из Walker Data/Slots/Slots.json.
    // Для Weapon2/Offhand2 позиций нет: они всегда идут через fallback.
    private static readonly Dictionary<InventorySlotE, Vector2> SlotPositions = new()
    {
        [InventorySlotE.BodyArmour1] = new Vector2(0.41987833f, 0.20999998f),
        [InventorySlotE.Weapon1] = new Vector2(0.11953338f, 0.1125f),
        [InventorySlotE.Offhand1] = new Vector2(0.73022324f, 0.1125f),
        [InventorySlotE.Helm1] = new Vector2(0.41987833f, 0.100625f),
        [InventorySlotE.Amulet1] = new Vector2(0.6146044f, 0.19812499f),
        [InventorySlotE.Ring1] = new Vector2(0.30425966f, 0.25875f),
        [InventorySlotE.Ring2] = new Vector2(0.6146044f, 0.25875f),
        [InventorySlotE.Gloves1] = new Vector2(0.22515208f, 0.31937498f),
        [InventorySlotE.Boots1] = new Vector2(0.6146044f, 0.31937498f),
        [InventorySlotE.Belt1] = new Vector2(0.41987833f, 0.36812496f),
    };

    /// <summary>
    /// Прямоугольник слота экипировки в координатах окна игры.
    /// false — панель инвентаря недоступна/невалидна или слот не найден ни одним способом.
    /// </summary>
    public static bool TryGetRect(GameController gameController, InventorySlotE slot, out RectangleF rect)
    {
        rect = default;

        if (gameController is null)
            return false;

        try
        {
            // InventoryPanel — UI-элемент панели экипировки/инвентаря.
            var panel = gameController.IngameState?.IngameUi?.InventoryPanel;
            if (panel is null || !panel.IsValid)
                return false;

            // 0. Приоритетный путь: UI-элемент надетого предмета из VisibleInventoryItems.
            if (TryGetByVisibleItem(panel, slot, out rect))
                return true;

            // 1. Основной путь Walker: относительная позиция слота + поиск по дочерним элементам.
            if (TryGetByPositions(panel, slot, out rect))
                return true;

            // 2/3. Fallback: InventoryUIElement инвентаря слота, затем GetElementSlot (в 330.5 может быть смещён).
            return TryGetByInventory(panel, slot, out rect);
        }
        catch
        {
            // Невалидная память/элементы UI не должны валить рендер.
            return false;
        }
    }

    /// <summary>
    /// Приоритетный путь: в инвентаре слота (<c>panel[index]</c>) лежит UI-элемент надетого предмета
    /// в <c>VisibleInventoryItems</c>. Не зависит от относительных позиций, поэтому работает и для swap-слотов.
    /// </summary>
    private static bool TryGetByVisibleItem(InventoryElement panel, InventorySlotE slot, out RectangleF rect)
    {
        rect = default;

        if (!TryMapSlot(slot, out var index))
            return false;

        try
        {
            var inventory = panel[index];
            var item = inventory?.VisibleInventoryItems?.FirstOrDefault();

            if (item is null || !item.IsValid)
                return false;

            var itemRect = item.GetClientRectCache;
            if (itemRect.Width <= 1f || itemRect.Height <= 1f)
                return false;

            rect = itemRect;
            return true;
        }
        catch
        {
            // panel[index] может бросать для swap-слотов — просто идём дальше по fallback'ам.
            return false;
        }
    }

    /// <summary>
    /// Порт Walker: считаем абсолютную точку слота от прямоугольника панели и ищем элемент
    /// в <c>Children[3]</c> (EquipmentItems), чей прямоугольник содержит эту точку.
    /// </summary>
    private static bool TryGetByPositions(InventoryElement panel, InventorySlotE slot, out RectangleF rect)
    {
        rect = default;

        if (!SlotPositions.TryGetValue(slot, out var topLeft))
            return false;

        var root = panel.GetClientRectCache;
        if (root.Width <= 0f || root.Height <= 0f)
            return false;

        var absolute = new Vector2(
                           root.X + root.Width * topLeft.X,
                           root.Y + root.Height * topLeft.Y)
                       + new Vector2(SlotProbeOffset, SlotProbeOffset);

        var children = panel.Children;
        if (children is null || children.Count <= 3)
            return false;

        // Children[3] — EquipmentItems (как в Walker).
        var equipment = children[3];
        if (equipment is null || !equipment.IsValid)
            return false;

        var equipmentSlots = equipment.Children;
        if (equipmentSlots is null)
            return false;

        for (var i = 0; i < equipmentSlots.Count; i++)
        {
            var child = equipmentSlots[i];
            if (child is null || !child.IsValid)
                continue;

            var childRect = child.GetClientRectCache;
            if (childRect.Width <= 1f || childRect.Height <= 1f)
                continue;

            if (childRect.Contains(absolute))
            {
                rect = childRect;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Fallback: <c>panel[index]?.InventoryUIElement</c> (UI-элемент инвентаря слота),
    /// затем <c>panel.GetElementSlot(index)</c> — в 330.5 индексы смещены, поэтому это последний вариант.
    /// </summary>
    private static bool TryGetByInventory(InventoryElement panel, InventorySlotE slot, out RectangleF rect)
    {
        rect = default;

        if (!TryMapSlot(slot, out var index))
            return false;

        try
        {
            var inventory = panel[index];
            var inventoryUi = inventory?.InventoryUIElement;

            if (TryUseRect(inventoryUi, out rect))
                return true;
        }
        catch
        {
            // Смещённый индекс в 330.5 может дать невалидную память — переходим к GetElementSlot.
        }

        try
        {
            return TryUseRect(panel.GetElementSlot(index), out rect);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryUseRect(Element? element, out RectangleF rect)
    {
        rect = default;

        if (element is null || !element.IsValid)
            return false;

        var elementRect = element.GetClientRectCache;
        if (elementRect.Width <= 1f || elementRect.Height <= 1f)
            return false;

        rect = elementRect;
        return true;
    }

    /// <summary>Слот серверного инвентаря → индекс слота на панели инвентаря.</summary>
    private static bool TryMapSlot(InventorySlotE slot, out InventoryIndex index)
    {
        switch (slot)
        {
            case InventorySlotE.Helm1:
                index = InventoryIndex.Helm;
                return true;
            case InventorySlotE.BodyArmour1:
                index = InventoryIndex.Chest;
                return true;
            case InventorySlotE.Gloves1:
                index = InventoryIndex.Gloves;
                return true;
            case InventorySlotE.Boots1:
                index = InventoryIndex.Boots;
                return true;
            case InventorySlotE.Belt1:
                index = InventoryIndex.Belt;
                return true;
            case InventorySlotE.Amulet1:
                index = InventoryIndex.Amulet;
                return true;
            case InventorySlotE.Ring1:
                index = InventoryIndex.LRing;
                return true;
            case InventorySlotE.Ring2:
                index = InventoryIndex.RRing;
                return true;
            case InventorySlotE.Weapon1:
                index = InventoryIndex.LWeapon;
                return true;
            case InventorySlotE.Offhand1:
                index = InventoryIndex.RWeapon;
                return true;
            case InventorySlotE.Weapon2:
                index = InventoryIndex.LWeaponSwap;
                return true;
            case InventorySlotE.Offhand2:
                index = InventoryIndex.RWeaponSwap;
                return true;
            default:
                index = InventoryIndex.None;
                return false;
        }
    }
}
