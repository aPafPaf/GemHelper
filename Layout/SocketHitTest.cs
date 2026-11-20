using ExileCore;
using ExileCore.Shared.Enums;
using GemHelper.Core;
using Vector2 = System.Numerics.Vector2;

namespace GemHelper.Layout;

/// <summary>
/// Screen centers of sockets and inventory items, used as Swap click points.
/// Everything is defensive: invalid geometry/memory simply returns false.
/// </summary>
public static class SocketHitTest
{
    /// <summary>Center of the socket rectangle (game window coordinates, without the window offset).</summary>
    public static bool TryGetSocketCenter(GameController gameController, GemSocketSnapshot socket, out Vector2 center)
        => TryGetSocketCenter(gameController, socket, socket.Slot, out center);

    /// <summary>
    /// Socket center computed from the geometry of the given slot.
    /// Needed for the second weapon set: once switched, it is drawn on the Weapon1/Offhand1 slots.
    /// </summary>
    public static bool TryGetSocketCenter(
        GameController gameController, GemSocketSnapshot socket, InventorySlotE slot, out Vector2 center)
    {
        center = default;

        if (gameController is null)
            return false;

        try
        {
            if (!EquipmentLayout.TryGetRect(gameController, slot, out var slotRect))
                return false;

            var rects = SocketLayout.GetSocketRectangles(
                slotRect,
                socket.TotalSockets,
                socket.ItemSizeX,
                socket.ItemSizeY,
                socket.InventoryColumns,
                socket.InventoryRows,
                socket.InventoryType);

            if (socket.SocketIndex < 0 || socket.SocketIndex >= rects.Length)
                return false;

            var box = SocketLayout.HalveRectangle(rects[socket.SocketIndex]);
            if (box.Width <= 0f || box.Height <= 0f)
                return false;

            var boxCenter = box.Center;
            center = new Vector2(boxCenter.X, boxCenter.Y);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Center of an item in the main inventory, found by its address.</summary>
    public static bool TryGetInventoryItemCenter(GameController gameController, long itemAddress, out Vector2 center)
    {
        center = default;

        if (gameController is null || itemAddress == 0)
            return false;

        try
        {
            var inventories = gameController.IngameState?.ServerData?.PlayerInventories;
            if (inventories is null)
                return false;

            for (var i = 0; i < inventories.Count; i++)
            {
                var holder = inventories[i];
                if (holder?.TypeId != InventoryNameE.MainInventory1)
                    continue;

                var items = holder.Inventory?.InventorySlotItems;
                if (items is null)
                    return false;

                for (var j = 0; j < items.Count; j++)
                {
                    var slotItem = items[j];
                    if (slotItem?.Item?.Address != itemAddress)
                        continue;

                    var rect = slotItem.GetClientRect();
                    if (rect.Width <= 1f || rect.Height <= 1f)
                        return false;

                    var rectCenter = rect.Center;
                    center = new Vector2(rectCenter.X, rectCenter.Y);
                    return true;
                }

                return false;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}
