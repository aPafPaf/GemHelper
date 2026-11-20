using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using ExileCore.Shared.Enums;
using RectangleF = SharpDX.RectangleF;
using Vector2 = SharpDX.Vector2;

namespace GemHelper.Layout;

/// <summary>
/// Equipment slot geometry on the inventory panel (M4a/M4b).
/// Primary path is a Walker port (<c>EquippedItemResolver.GetSlotRectangle</c>): relative positions
/// from Slots.json plus a search for the child element containing the slot point.
/// In 330.5 <c>InventoryPanel.GetElementSlot(InventoryIndex)</c> returns offset elements,
/// so it is kept only as the last fallback.
/// </summary>
public static class EquipmentLayout
{
    // Offset from the resolved relative point to land inside the slot rectangle.
    private const float SlotProbeOffset = 10f;

    // Relative coordinates (0..1) inside InventoryPanel, taken from Walker Data/Slots/Slots.json.
    // Weapon2/Offhand2 have no positions: they always go through the fallback path.
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
    /// Equipment slot rectangle in game window coordinates.
    /// false: the inventory panel is unavailable/invalid or the slot was not found by any method.
    /// </summary>
    public static bool TryGetRect(GameController gameController, InventorySlotE slot, out RectangleF rect)
    {
        rect = default;

        if (gameController is null)
            return false;

        try
        {
            // InventoryPanel is the equipment/inventory panel UI element.
            var panel = gameController.IngameState?.IngameUi?.InventoryPanel;
            if (panel is null || !panel.IsValid)
                return false;

            // 0. Preferred path: UI element of the equipped item from VisibleInventoryItems.
            if (TryGetByVisibleItem(panel, slot, out rect))
                return true;

            // 1. Main Walker path: slot relative position plus child element lookup.
            if (TryGetByPositions(panel, slot, out rect))
                return true;

            // 2/3. Fallback: the slot inventory's InventoryUIElement, then GetElementSlot (may be offset in 330.5).
            return TryGetByInventory(panel, slot, out rect);
        }
        catch
        {
            // Invalid memory/UI elements must not break rendering.
            return false;
        }
    }

    /// <summary>
    /// Preferred path: the slot inventory (<c>panel[index]</c>) holds the equipped item's UI element
    /// in <c>VisibleInventoryItems</c>. Independent of relative positions, so it also works for swap slots.
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
            // panel[index] may throw for swap slots, so just continue with the fallbacks.
            return false;
        }
    }

    /// <summary>
    /// Walker port: compute the slot's absolute point from the panel rectangle and find the element
    /// in <c>Children[3]</c> (EquipmentItems) whose rectangle contains that point.
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

        // Children[3] is EquipmentItems (same as Walker).
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
    /// Fallback: <c>panel[index]?.InventoryUIElement</c> (the slot inventory's UI element),
    /// then <c>panel.GetElementSlot(index)</c>; indices are offset in 330.5, so this is the last resort.
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
            // An offset index in 330.5 may yield invalid memory, so fall through to GetElementSlot.
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

    /// <summary>Server inventory slot to inventory panel slot index.</summary>
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
