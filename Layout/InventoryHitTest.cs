using ExileCore;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using RectangleF = SharpDX.RectangleF;
using Vector2 = System.Numerics.Vector2;

namespace GemHelper.Layout;

/// <summary>
/// Empty cell lookup in the main inventory, used for Unequip (dropping a gem off the cursor).
/// 12×5 grid: occupancy is built from <c>ServerInventory.InventorySlotItems</c>.
/// </summary>
public static class InventoryHitTest
{
    /// <summary>Center of the first free cell (top-left order) in game window coordinates.</summary>
    public static bool TryGetEmptyCellCenter(GameController gameController, out Vector2 center)
    {
        center = default;

        if (gameController is null)
            return false;

        try
        {
            var panel = gameController.IngameState?.IngameUi?.InventoryPanel;
            if (panel is null || !panel.IsValid)
                return false;

            var inventory = panel[InventoryIndex.PlayerInventory];
            var server = inventory?.ServerInventory;
            if (server is null)
                return false;

            var columns = server.Columns;
            var rows = server.Rows;
            if (columns <= 0 || rows <= 0)
                return false;

            var grid = ResolveGridRectangle(panel, inventory);
            if (grid is null)
                return false;

            var items = server.InventorySlotItems;
            if (items is null)
                return false;

            // Occupancy: [x, y].
            var occupied = new bool[columns, rows];

            for (var i = 0; i < items.Count; i++)
            {
                var slotItem = items[i];
                if (slotItem is null)
                    continue;

                var right = slotItem.PosX + slotItem.SizeX;
                var bottom = slotItem.PosY + slotItem.SizeY;

                for (var x = slotItem.PosX; x < right; x++)
                {
                    for (var y = slotItem.PosY; y < bottom; y++)
                    {
                        if (x >= 0 && x < columns && y >= 0 && y < rows)
                            occupied[x, y] = true;
                    }
                }
            }

            var gridRect = grid.Value;

            for (var y = 0; y < rows; y++)
            {
                for (var x = 0; x < columns; x++)
                {
                    if (occupied[x, y])
                        continue;

                    center = new Vector2(
                        gridRect.X + (x + 0.5f) * gridRect.Width / columns,
                        gridRect.Y + (y + 0.5f) * gridRect.Height / rows);

                    return true;
                }
            }
        }
        catch
        {
            // Invalid memory/UI: treat as "no free cell".
        }

        return false;
    }

    /// <summary>Main inventory grid rectangle: UI element → slot → Children[2] (same as Walker).</summary>
    private static RectangleF? ResolveGridRectangle(InventoryElement panel, Inventory? inventory)
    {
        try
        {
            var ui = inventory?.InventoryUIElement;
            if (ui is not null && ui.IsValid)
            {
                var rect = ui.GetClientRectCache;
                if (rect.Width > 1f && rect.Height > 1f)
                    return rect;
            }
        }
        catch
        {
            // Try the next option.
        }

        try
        {
            var slot = panel.GetElementSlot(InventoryIndex.PlayerInventory);
            if (slot is not null && slot.IsValid)
            {
                var rect = slot.GetClientRectCache;
                if (rect.Width > 1f && rect.Height > 1f)
                    return rect;
            }
        }
        catch
        {
            // Try the next option.
        }

        try
        {
            var children = panel.Children;
            if (children is not null && children.Count > 2)
            {
                var child = children[2];
                if (child is not null && child.IsValid)
                {
                    var rect = child.GetClientRectCache;
                    if (rect.Width > 1f && rect.Height > 1f)
                        return rect;
                }
            }
        }
        catch
        {
            // Nothing left.
        }

        return null;
    }
}
