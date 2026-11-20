using ExileCore.Shared.Enums;
using RectangleF = SharpDX.RectangleF;

namespace GemHelper.Layout;

/// <summary>
/// Per-socket geometry inside an item slot (M4b).
/// Ported from Walker 3.24 (<c>Extensions/SocketLayout.cs</c> + <c>WHelper.RectangleHelper.SplitRectangleToGrid</c>)
/// with memory reads replaced by sizes read earlier into a snapshot.
/// Walker's magic indices are kept unchanged.
/// </summary>
public static class SocketLayout
{
    public enum VerticalPosition
    {
        Top,
        Middle,
        Bottom,
    }

    /// <summary>
    /// Socket rectangles inside the slot rectangle, ordered by SocketIndex.
    /// An empty array means the geometry could not be computed (no sockets/sizes or unknown layout).
    /// </summary>
    public static RectangleF[] GetSocketRectangles(
        RectangleF slotRectangle,
        int numberOfSockets,
        int sizeX,
        int sizeY,
        int inventoryColumns,
        int inventoryRows,
        InventoryTypeE inventoryType)
    {
        if (numberOfSockets <= 0 || sizeX <= 0 || sizeY <= 0)
            return Array.Empty<RectangleF>();

        var splitCount = GetSplitCount(numberOfSockets, sizeX, sizeY);
        if (splitCount.row <= 0 || splitCount.columns <= 0)
            return Array.Empty<RectangleF>();

        Dictionary<VerticalPosition, List<RectangleF>> splitRect;
        try
        {
            splitRect = SplitRectangleWithSocketsVortical(
                slotRectangle,
                splitCount.row,
                splitCount.columns,
                numberOfSockets);
        }
        catch
        {
            // Walker throws for rows > 3; unreachable in theory, but better not to crash.
            return Array.Empty<RectangleF>();
        }

        // Extra room for Walker's magic indices (e.g. data[5] with 5 sockets);
        // the array is trimmed to numberOfSockets at the end.
        var data = new RectangleF[Math.Max(numberOfSockets, 6)];

        if (splitCount.columns == 2 && splitCount.row == 3)
        {
            foreach (var rect in splitRect)
            {
                if (inventoryType is not InventoryTypeE.BodyArmour)
                {
                    if (rect.Key == VerticalPosition.Top)
                    {
                        foreach (var rect2 in rect.Value)
                        {
                            var newRect = SplitRectangleToGrid(rect2, 7, 3);
                            if (slotRectangle.Center.X > rect2.X)
                                data[0] = newRect[5, 1];
                            else
                                data[1] = newRect[5, 1];
                        }

                        continue;
                    }

                    if (rect.Key == VerticalPosition.Middle)
                    {
                        foreach (var rect2 in rect.Value)
                        {
                            var newRect = SplitRectangleToGrid(rect2, 3, 3);
                            if (slotRectangle.Center.X > rect2.X)
                                data[3] = newRect[1, 1];
                            else
                                data[2] = newRect[1, 1];
                        }

                        continue;
                    }

                    if (rect.Key == VerticalPosition.Bottom)
                    {
                        foreach (var rect2 in rect.Value)
                        {
                            var newRect = SplitRectangleToGrid(rect2, 3, 3);
                            if (slotRectangle.Center.X > rect2.X)
                                data[4] = newRect[0, 1];
                            else
                                data[5] = newRect[0, 1];
                        }

                        continue;
                    }
                }
                else
                {
                    if (rect.Key == VerticalPosition.Top)
                    {
                        foreach (var rect2 in rect.Value)
                        {
                            var newRect = SplitRectangleToGrid(rect2, 7, 3);
                            if (slotRectangle.Center.X > rect2.X)
                                data[0] = newRect[3, 1];
                            else
                                data[1] = newRect[3, 1];
                        }

                        continue;
                    }

                    if (rect.Key == VerticalPosition.Middle)
                    {
                        foreach (var rect2 in rect.Value)
                        {
                            var newRect = SplitRectangleToGrid(rect2, 3, 3);
                            if (slotRectangle.Center.X > rect2.X)
                                data[3] = newRect[1, 1];
                            else
                                data[2] = newRect[1, 1];
                        }

                        continue;
                    }

                    if (rect.Key == VerticalPosition.Bottom)
                    {
                        foreach (var rect2 in rect.Value)
                        {
                            var newRect = SplitRectangleToGrid(rect2, 3, 3);
                            if (slotRectangle.Center.X > rect2.X)
                                data[4] = newRect[1, 1];
                            else
                                data[5] = newRect[1, 1];
                        }

                        continue;
                    }
                }
            }
        }

        if (splitCount.columns == 2 && splitCount.row == 2)
        {
            if (inventoryColumns == 2 && inventoryRows == 4
                && inventoryType is InventoryTypeE.Weapon)
            {
                foreach (var rect in splitRect)
                {
                    if (rect.Key == VerticalPosition.Top)
                    {
                        foreach (var rect2 in rect.Value)
                        {
                            var newRect = SplitRectangleToGrid(rect2, 7, 3);
                            if (slotRectangle.Center.X > rect2.X)
                                data[0] = newRect[5, 1];
                            else
                                data[1] = newRect[5, 1];
                        }

                        continue;
                    }

                    if (rect.Key == VerticalPosition.Bottom)
                    {
                        foreach (var rect2 in rect.Value)
                        {
                            var newRect = SplitRectangleToGrid(rect2, 7, 3);
                            if (numberOfSockets == 3)
                            {
                                data[2] = newRect[1, 1];
                            }
                            else
                            {
                                if (slotRectangle.Center.X > rect2.X)
                                    data[3] = newRect[1, 1];
                                else
                                    data[2] = newRect[1, 1];
                            }
                        }

                        continue;
                    }
                }
            }
            else if (inventoryColumns == 2 && inventoryRows == 4)
            {
                foreach (var rect in splitRect)
                {
                    if (rect.Key == VerticalPosition.Top)
                    {
                        foreach (var rect2 in rect.Value)
                        {
                            var newRect = SplitRectangleToGrid(rect2, 7, 3);
                            if (slotRectangle.Center.X > rect2.X)
                                data[0] = newRect[5, 1];
                            else
                                data[1] = newRect[5, 1];
                        }

                        continue;
                    }

                    if (rect.Key == VerticalPosition.Bottom)
                    {
                        foreach (var rect2 in rect.Value)
                        {
                            var newRect = SplitRectangleToGrid(rect2, 7, 3);
                            if (numberOfSockets == 3)
                            {
                                data[2] = newRect[1, 1];
                            }
                            else
                            {
                                if (slotRectangle.Center.X > rect2.X)
                                    data[3] = newRect[1, 1];
                                else
                                    data[2] = newRect[1, 1];
                            }
                        }

                        continue;
                    }
                }
            }
            else if (inventoryColumns == 2 && inventoryRows == 3)
            {
                foreach (var rect in splitRect)
                {
                    if (rect.Key == VerticalPosition.Top)
                    {
                        foreach (var rect2 in rect.Value)
                        {
                            var newRect = SplitRectangleToGrid(rect2, 7, 3);
                            if (slotRectangle.Center.X > rect2.X)
                                data[0] = newRect[4, 1];
                            else
                                data[1] = newRect[4, 1];
                        }

                        continue;
                    }

                    if (rect.Key == VerticalPosition.Bottom)
                    {
                        foreach (var rect2 in rect.Value)
                        {
                            var newRect = SplitRectangleToGrid(rect2, 7, 3);
                            if (numberOfSockets == 3)
                            {
                                data[2] = newRect[2, 1];
                            }
                            else
                            {
                                if (slotRectangle.Center.X > rect2.X)
                                    data[3] = newRect[2, 1];
                                else
                                    data[2] = newRect[2, 1];
                            }
                        }

                        continue;
                    }
                }
            }
            else if (inventoryColumns == 2 && inventoryRows == 2)
            {
                foreach (var rect in splitRect)
                {
                    if (rect.Key == VerticalPosition.Top)
                    {
                        foreach (var rect2 in rect.Value)
                        {
                            var newRect = SplitRectangleToGrid(rect2, 3, 3);
                            if (slotRectangle.Center.X > rect2.X)
                                data[0] = newRect[1, 1];
                            else
                                data[1] = newRect[1, 1];
                        }

                        continue;
                    }

                    if (rect.Key == VerticalPosition.Bottom)
                    {
                        foreach (var rect2 in rect.Value)
                        {
                            var newRect = SplitRectangleToGrid(rect2, 3, 3);
                            if (numberOfSockets == 3)
                            {
                                data[2] = newRect[1, 1];
                            }
                            else
                            {
                                if (slotRectangle.Center.X > rect2.X)
                                    data[3] = newRect[1, 1];
                                else
                                    data[2] = newRect[1, 1];
                            }
                        }

                        continue;
                    }
                }
            }
            else
            {
                foreach (var rect in splitRect)
                {
                    if (rect.Key == VerticalPosition.Top)
                    {
                        foreach (var rect2 in rect.Value)
                        {
                            var newRect = SplitRectangleToGrid(rect2, 3, 3);
                            if (slotRectangle.Center.X > rect2.X)
                                data[0] = newRect[1, 1];
                            else
                                data[1] = newRect[1, 1];
                        }

                        continue;
                    }

                    if (rect.Key == VerticalPosition.Bottom)
                    {
                        foreach (var rect2 in rect.Value)
                        {
                            var newRect = SplitRectangleToGrid(rect2, 3, 3);
                            if (numberOfSockets == 3)
                            {
                                data[2] = newRect[1, 1];
                            }
                            else
                            {
                                if (slotRectangle.Center.X > rect2.X)
                                    data[2] = newRect[1, 1];
                                else
                                    data[3] = newRect[1, 1];
                            }
                        }

                        continue;
                    }
                }
            }
        }

        if (splitCount.columns == 1 && splitCount.row == 3)
        {
            foreach (var rect in splitRect)
            {
                if (rect.Key == VerticalPosition.Top)
                {
                    foreach (var rect2 in rect.Value)
                    {
                        var newRect = SplitRectangleToGrid(rect2, 3, 3);
                        data[0] = newRect[2, 1];
                    }

                    continue;
                }

                if (rect.Key == VerticalPosition.Middle)
                {
                    foreach (var rect2 in rect.Value)
                    {
                        var newRect = SplitRectangleToGrid(rect2, 3, 3);
                        data[1] = newRect[1, 1];
                    }

                    continue;
                }

                if (rect.Key == VerticalPosition.Bottom)
                {
                    foreach (var rect2 in rect.Value)
                    {
                        var newRect = SplitRectangleToGrid(rect2, 3, 3);
                        data[2] = newRect[0, 1];
                    }

                    continue;
                }
            }
        }

        if (splitCount.columns == 1 && splitCount.row == 2)
        {
            foreach (var rect in splitRect)
            {
                if (rect.Key == VerticalPosition.Top)
                {
                    foreach (var rect2 in rect.Value)
                    {
                        var newRect = SplitRectangleToGrid(rect2, 10, 3);
                        data[0] = newRect[7, 1];
                    }

                    continue;
                }

                if (rect.Key == VerticalPosition.Bottom)
                {
                    foreach (var rect2 in rect.Value)
                    {
                        var newRect = SplitRectangleToGrid(rect2, 10, 3);
                        data[1] = newRect[2, 1];
                    }

                    continue;
                }
            }
        }

        if (splitCount.columns == 1 && splitCount.row == 1)
        {
            foreach (var rect in splitRect)
            {
                if (rect.Key == VerticalPosition.Middle)
                {
                    foreach (var rect2 in rect.Value)
                    {
                        var newRect = SplitRectangleToGrid(rect2, 9, 3);
                        data[0] = newRect[4, 1];
                    }

                    continue;
                }
            }
        }

        if (splitCount.columns == 2 && splitCount.row == 1)
        {
            foreach (var rect in splitRect)
            {
                if (rect.Key == VerticalPosition.Middle)
                {
                    foreach (var rect2 in rect.Value)
                    {
                        var newRect = SplitRectangleToGrid(rect2, 9, 3);
                        if (slotRectangle.Center.X > rect2.X)
                            data[0] = newRect[4, 1];
                        else
                            data[1] = newRect[4, 1];
                    }

                    continue;
                }
            }
        }

        // Drop the extra room added for the magic indices.
        Array.Resize(ref data, numberOfSockets);
        return data;
    }

    /// <summary>Grid rows/columns occupied by the item for the given socket count.</summary>
    public static (int row, int columns) GetSplitCount(int numberSocket, int sizeX, int sizeY)
    {
        if (sizeX == 2)
        {
            switch (numberSocket)
            {
                case 1:
                    return (1, 1);
                case 2:
                    return (1, 2);
                case > 2 and <= 4:
                    return (2, 2);
                case > 4:
                    return (3, 2);
            }
        }

        if (sizeX == 1)
        {
            switch (numberSocket)
            {
                case 1:
                    return (1, 1);
                case 2:
                    return (2, 1);
                case 3:
                    return (3, 1);
            }
        }

        return (0, 0);
    }

    /// <summary>Splits the rectangle into horizontal sections (Top/Middle/Bottom).</summary>
    public static Dictionary<VerticalPosition, List<RectangleF>> SplitRectangleWithSocketsVortical(
        RectangleF rect,
        int rows,
        int columns,
        int numberSocket)
    {
        if (rows <= 0 || rows > 3)
            throw new ArgumentException("Rows must be between 1 and 3.", nameof(rows));

        if (columns <= 0 || columns > 2)
            throw new ArgumentException("Columns must be between 1 and 2.", nameof(columns));

        var cellWidth = rect.Width / columns;
        var cellHeight = rect.Height / rows;

        var result = new Dictionary<VerticalPosition, List<RectangleF>>();

        for (var row = 0; row < rows; row++)
        {
            var position = GetVerticalPosition(row, rows);

            if (!result.ContainsKey(position))
                result[position] = new List<RectangleF>();

            for (var col = 0; col < columns; col++)
            {
                // e.g. 3 sockets instead of 4 (skip the middle-right cell)
                if (numberSocket == 3 && row == 1 && col == 0 && rows != 3)
                    continue;

                var cell = new RectangleF(
                    rect.Left + col * cellWidth,
                    rect.Top + row * cellHeight,
                    cellWidth,
                    cellHeight);

                result[position].Add(cell);
            }
        }

        return result;
    }

    private static VerticalPosition GetVerticalPosition(int row, int rows)
    {
        return rows switch
        {
            1 => VerticalPosition.Middle,

            2 => row == 0
                ? VerticalPosition.Top
                : VerticalPosition.Bottom,

            3 => row switch
            {
                0 => VerticalPosition.Top,
                1 => VerticalPosition.Middle,
                2 => VerticalPosition.Bottom,
                _ => throw new ArgumentOutOfRangeException(nameof(row)),
            },

            _ => throw new ArgumentOutOfRangeException(nameof(rows)),
        };
    }

    /// <summary>Halves the rectangle around its center (Walker port).</summary>
    public static RectangleF HalveRectangle(RectangleF rect)
    {
        var newWidth = rect.Width / 2f;
        var newHeight = rect.Height / 2f;
        var newLeft = rect.Left + (rect.Width - newWidth) / 2f;
        var newTop = rect.Top + (rect.Height - newHeight) / 2f;
        return new RectangleF(newLeft, newTop, newWidth, newHeight);
    }

    /// <summary>Uniform rows × columns grid (Walker <c>RectangleHelper.SplitRectangleToGrid</c> port).</summary>
    private static RectangleF[,] SplitRectangleToGrid(RectangleF rect, int rows, int columns)
    {
        if (rows <= 0 || columns <= 0)
            return new RectangleF[0, 0];

        var result = new RectangleF[rows, columns];

        var cellWidth = rect.Width / columns;
        var cellHeight = rect.Height / rows;

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < columns; col++)
            {
                result[row, col] = new RectangleF(
                    rect.X + col * cellWidth,
                    rect.Y + row * cellHeight,
                    cellWidth,
                    cellHeight);
            }
        }

        return result;
    }
}
