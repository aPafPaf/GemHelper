using System.Globalization;
using ExileCore;
using ExileCore.Shared.Enums;
using GemHelper.Core;
using GemHelper.Layout;
using SharpDX;
using Color = SharpDX.Color;
using Graphics = ExileCore.Graphics;
using RectangleF = SharpDX.RectangleF;
using Vector2 = System.Numerics.Vector2;

namespace GemHelper.Rendering;

/// <summary>
/// Equipped item overlay: slot frame, socket frames, link lines and a compact gem list.
/// M4b: per-socket geometry is computed from item/inventory sizes read during Tick.
/// </summary>
public static class GemOverlay
{
    // Gem list line height (tuned for the default HUD font).
    private const float LineHeight = 14f;

    // Text gap from the item frame.
    private const float TextGap = 6f;

    // Color of the diagnostic slot label.
    private static readonly Color DebugSlotColor = new(0, 255, 255);

    /// <summary>Draws frames and labels. Never throws to the caller.</summary>
    public static void Draw(GameController gameController, GemHelperSettings settings, GemSnapshot snapshot, Graphics graphics)
    {
        if (gameController is null || settings is null || snapshot is null || graphics is null)
            return;

        if (!settings.ShowOverlay.Value)
            return;

        try
        {
            var panel = gameController.IngameState?.IngameUi?.InventoryPanel;

            // Inventory panel is closed/unavailable: nothing to draw.
            if (panel is null || !panel.IsValid || !panel.IsVisible)
                return;

            var windowWidth = TryGetWindowWidth(gameController);
            var lineHeight = ResolveLineHeight(graphics);
            var showExp = settings.OverlayShowExp.Value;

            // Snapshot sockets are ordered by slot, so groups are consecutive runs of the same slot.
            var sockets = snapshot.Sockets;
            var start = 0;

            while (start < sockets.Count)
            {
                var slot = sockets[start].Slot;
                var end = start + 1;
                while (end < sockets.Count && sockets[end].Slot == slot)
                    end++;

                DrawSlot(gameController, settings, sockets, start, end, windowWidth, lineHeight, showExp, graphics);
                start = end;
            }
        }
        catch
        {
            // Geometry/memory errors must not break rendering.
        }
    }

    /// <summary>Frame for a single item plus the gem labels of that slot.</summary>
    private static void DrawSlot(
        GameController gameController,
        GemHelperSettings settings,
        IReadOnlyList<GemSocketSnapshot> sockets,
        int start,
        int end,
        float windowWidth,
        float lineHeight,
        bool showExp,
        Graphics graphics)
    {
        var slot = sockets[start].Slot;

        // Per-slot settings: text visibility and offsets.
        var slotOffset = settings.TextOffsets.For(slot);

        if (!EquipmentLayout.TryGetRect(gameController, slot, out var rect))
            return;

        // Diagnostics: label of the resolved slot at its top-left corner.
        // The SharpDX.Vector2 overload is [Obsolete] in 330.5, so System.Numerics is used.
        if (settings.OverlayDebugSlots.Value)
            graphics.DrawText(slot.ToString(), new Vector2(rect.X, rect.Y), DebugSlotColor);

        // Count gems and measure lines up front (the width decides right vs left placement).
        var gemCount = 0;
        for (var i = start; i < end; i++)
        {
            if (HasGem(sockets[i]))
                gemCount++;
        }

        if (settings.OverlayOnlyWithGems.Value && gemCount == 0)
            return;

        var frameColor = gemCount > 0 ? Color.Yellow : Color.Gray;
        graphics.DrawFrame(rect, frameColor, 2);

        // M4b: socket frames and link lines inside the item. The geometry is also needed for text→socket lines.
        var boxes = DrawSocketGeometry(settings, sockets, start, end, rect, graphics);

        if (gemCount == 0 || !settings.OverlayShowGemNames.Value)
            return;

        // Text is disabled for this slot; item/socket frames and link lines are already drawn.
        if (!slotOffset.Show.Value)
            return;

        var showIndex = settings.OverlaySocketFrames.Value;
        var lines = new (string Main, string Suffix, Color MainColor, Color SuffixColor, int SocketIndex, float Width)[gemCount];
        var lineColors = new Color[gemCount];
        var maxWidth = 0f;
        var index = 0;

        for (var i = start; i < end; i++)
        {
            var socket = sockets[i];
            if (!HasGem(socket))
                continue;

            var text = BuildGemLine(socket, showExp, showIndex);

            // Text and line colors are configured separately: socket color or a fixed color.
            var gemTextColor = settings.OverlayUseSocketColorForText.Value
                ? ToColor(socket.SocketColor)
                : settings.OverlayGemTextColor.Value;
            // Line: a transparent setting color means use the socket color; an opaque one is used as is.
            var customLineColor = settings.OverlayGemLineColor.Value;
            var gemLineColor = customLineColor.A == 0
                ? ToColor(socket.SocketColor)
                : customLineColor;

            var main = text;
            var suffix = string.Empty;
            var mainColor = gemTextColor;
            var suffixColor = gemTextColor;

            if (socket.IsMaxLevel)
            {
                var maxColor = settings.OverlayMaxLevelColor.Value;

                if (settings.OverlayMaxLevelRecolorWholeText.Value)
                {
                    // Recolor the whole MAX gem text.
                    mainColor = maxColor;
                }
                else if (text.EndsWith(" MAX", StringComparison.Ordinal))
                {
                    // Otherwise highlight only the MAX word (drawn as a separate run).
                    // The trailing space is kept so the text does not stick to MAX.
                    main = text[..^4];
                    suffix = "MAX";
                    suffixColor = maxColor;
                }
            }

            // Full line width, used to position the whole block.
            var width = graphics.MeasureText(text).X;

            lines[index] = (main, suffix, mainColor, suffixColor, socket.SocketIndex, width);
            lineColors[index] = gemLineColor;
            index++;

            if (width > maxWidth)
                maxWidth = width;
        }

        // Text goes right of the frame by default; near the right screen edge it moves to the left of the frame.
        var x = rect.Right + TextGap;
        if (windowWidth > 0f && x + maxWidth > windowWidth)
            x = rect.Left - maxWidth - TextGap;

        var yBase = rect.Top;

        // Block offset: global for all slots plus per-slot for this one.
        x += settings.OverlayTextOffsetX.Value + slotOffset.X.Value;
        yBase += settings.OverlayTextOffsetY.Value + slotOffset.Y.Value;

        if (x < 0f)
            x = 0f;
        if (yBase < 0f)
            yBase = 0f;

        // Draw text→socket lines before the text so the labels stay on top.
        if (settings.OverlayTextToSocketLines.Value && boxes.Length > 0)
        {
            DrawTextToSocketLines(
                lines,
                lineColors,
                boxes,
                x,
                yBase,
                lineHeight,
                maxWidth,
                rect,
                settings.OverlayGemLineThickness.Value,
                graphics);
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var y = yBase + i * lineHeight;

            graphics.DrawTextWithBackground(lines[i].Main, new Vector2(x, y), lines[i].MainColor, Color.Black);

            if (lines[i].Suffix.Length > 0)
            {
                var mainWidth = graphics.MeasureText(lines[i].Main).X;
                graphics.DrawTextWithBackground(
                    lines[i].Suffix,
                    new Vector2(x + mainWidth, y),
                    lines[i].SuffixColor,
                    Color.Black);
            }
        }
    }

    /// <summary>
    /// Thin lines from the label block edge to the matching socket center.
    /// The X anchor depends on the side: for text on the right the line starts at its left edge,
    /// for text on the left at the block's right edge (see <c>textOnRight</c>).
    /// </summary>
    private static void DrawTextToSocketLines(
        (string Main, string Suffix, Color MainColor, Color SuffixColor, int SocketIndex, float Width)[] lines,
        Color[] lineColors,
        RectangleF[] boxes,
        float x,
        float yBase,
        float lineHeight,
        float maxWidth,
        RectangleF rect,
        float thickness,
        Graphics graphics)
    {
        // Which side of the item the text block is on.
        var textOnRight = x >= rect.Center.X;

        for (var i = 0; i < lines.Length; i++)
        {
            var socketIndex = lines[i].SocketIndex;
            if (socketIndex < 0 || socketIndex >= boxes.Length)
                continue;

            var box = boxes[socketIndex];
            if (box.Width <= 0f || box.Height <= 0f)
                continue;

            var lineY = yBase + i * lineHeight + lineHeight * 0.5f;
            var anchorX = textOnRight ? x - 2f : x + maxWidth + 2f;

            var center = box.Center;

            // Non-obsolete 330.5 overload: System.Numerics.Vector2.
            graphics.DrawLine(
                new Vector2(anchorX, lineY),
                new Vector2(center.X, center.Y),
                thickness,
                lineColors[i]);
        }
    }

    /// <summary>
    /// Per-socket item geometry: socket frames colored by socket and link lines between adjacent sockets.
    /// The geometry comes from the snapshot (ItemSizeX/Y, InventoryColumns/Rows, InventoryType, TotalSockets).
    /// Returns the socket rectangles (for text→socket lines) or an empty array.
    /// </summary>
    private static RectangleF[] DrawSocketGeometry(
        GemHelperSettings settings,
        IReadOnlyList<GemSocketSnapshot> sockets,
        int start,
        int end,
        RectangleF rect,
        Graphics graphics)
    {
        var first = sockets[start];

        // totalSockets: max from the snapshot, otherwise the number of group entries.
        var totalSockets = 0;
        for (var i = start; i < end; i++)
        {
            if (sockets[i].TotalSockets > totalSockets)
                totalSockets = sockets[i].TotalSockets;
        }

        if (totalSockets <= 0)
            totalSockets = end - start;

        RectangleF[] rects;
        try
        {
            rects = SocketLayout.GetSocketRectangles(
                rect,
                totalSockets,
                first.ItemSizeX,
                first.ItemSizeY,
                first.InventoryColumns,
                first.InventoryRows,
                first.InventoryType);
        }
        catch
        {
            // Bad geometry must not break the overlay.
            rects = Array.Empty<RectangleF>();
        }

        if (rects.Length == 0)
            return Array.Empty<RectangleF>();

        // SocketIndex -> snapshot entry (needed for the frame color).
        var byIndex = new Dictionary<int, GemSocketSnapshot>(end - start);
        for (var i = start; i < end; i++)
            byIndex[sockets[i].SocketIndex] = sockets[i];

        var count = Math.Min(totalSockets, rects.Length);
        var boxes = new RectangleF[count];

        for (var i = 0; i < count; i++)
        {
            var box = SocketLayout.HalveRectangle(rects[i]);
            boxes[i] = box;

            byIndex.TryGetValue(i, out var socket);

            // Regular socket frame.
            if (settings.OverlaySocketFrames.Value && box.Width > 0f && box.Height > 0f)
            {
                var color = socket is null ? Color.Gray : ToColor(socket.SocketColor);
                graphics.DrawFrame(box, color, 2);
            }

            // Max level gem: extra outer frame (drawn even without the regular frames).
            if (settings.OverlayHighlightMaxLevelGems.Value
                && socket is { IsEmpty: false, IsMaxLevel: true }
                && box.Width > 0f
                && box.Height > 0f)
            {
                var outer = new RectangleF(box.X - 2f, box.Y - 2f, box.Width + 4f, box.Height + 4f);
                graphics.DrawFrame(outer, settings.OverlayMaxLevelColor.Value, settings.OverlayMaxLevelThickness.Value);
            }
        }

        if (settings.OverlayLinkLines.Value)
            DrawLinkLines(settings, byIndex, boxes, graphics);

        return boxes;
    }

    /// <summary>Lines within link groups: connect adjacent (by SocketIndex) sockets of the same group.</summary>
    private static void DrawLinkLines(
        GemHelperSettings settings,
        Dictionary<int, GemSocketSnapshot> byIndex,
        RectangleF[] boxes,
        Graphics graphics)
    {
        var groups = new Dictionary<int, List<int>>();

        for (var i = 0; i < boxes.Length; i++)
        {
            if (!byIndex.TryGetValue(i, out var socket))
                continue;

            if (socket.LinkGroup < 0)
                continue;

            if (!groups.TryGetValue(socket.LinkGroup, out var list))
            {
                list = new List<int>(6);
                groups[socket.LinkGroup] = list;
            }

            list.Add(socket.SocketIndex);
        }

        foreach (var group in groups.Values)
        {
            if (group.Count < 2)
                continue;

            group.Sort();

            for (var i = 0; i + 1 < group.Count; i++)
            {
                var a = group[i];
                var b = group[i + 1];

                if (a < 0 || b < 0 || a >= boxes.Length || b >= boxes.Length)
                    continue;

                var boxA = boxes[a];
                var boxB = boxes[b];

                if (boxA.Width <= 0f || boxA.Height <= 0f || boxB.Width <= 0f || boxB.Height <= 0f)
                    continue;

                // The SharpDX.Vector2 overload is [Obsolete] in 330.5, so System.Numerics is used.
                var centerA = boxA.Center;
                var centerB = boxB.Center;

                graphics.DrawLine(
                    new Vector2(centerA.X, centerA.Y),
                    new Vector2(centerB.X, centerB.Y),
                    settings.OverlayLinkLineThickness.Value,
                    settings.OverlayLinkLineColor.Value);
            }
        }
    }

    /// <summary>
    /// Label line height: at least the constant, but it grows for a larger HUD font.
    /// A measurement failure is not critical, so fall back to the constant.
    /// </summary>
    private static float ResolveLineHeight(Graphics graphics)
    {
        try
        {
            return MathF.Max(LineHeight, graphics.MeasureText("Ag").Y + 2f);
        }
        catch
        {
            return LineHeight;
        }
    }

    private static bool HasGem(GemSocketSnapshot socket)
        => !socket.IsEmpty && !string.IsNullOrEmpty(socket.GemName);

    /// <summary>Gem line: <c>[i] Name L{level} [Q{quality}] [42%|MAX]</c>.</summary>
    private static string BuildGemLine(GemSocketSnapshot socket, bool showExp, bool prefixIndex)
    {
        var name = prefixIndex
            ? $"[{socket.SocketIndex.ToString(CultureInfo.InvariantCulture)}] {socket.GemName}"
            : socket.GemName;

        var line = $"{name} L{socket.GemLevel.ToString(CultureInfo.InvariantCulture)}";

        if (socket.GemQuality > 0)
            line += $" Q{socket.GemQuality.ToString(CultureInfo.InvariantCulture)}";

        if (showExp)
        {
            line += socket.IsMaxLevel
                ? " MAX"
                : $" {socket.ExpPercent.ToString("0", CultureInfo.InvariantCulture)}%";
        }

        return line;
    }

    private static float TryGetWindowWidth(GameController gameController)
    {
        try
        {
            var windowRect = gameController.Window.GetWindowRectangle();
            return windowRect.Width;
        }
        catch
        {
            return 0f;
        }
    }

    /// <summary>Label color by socket color (M4a palette).</summary>
    private static Color ToColor(SocketColor socketColor) => socketColor switch
    {
        SocketColor.Red => new Color(255, 90, 90),
        SocketColor.Green => new Color(100, 255, 100),
        SocketColor.Blue => new Color(120, 160, 255),
        SocketColor.White => Color.White,
        SocketColor.Abyssal => new Color(190, 120, 255),
        SocketColor.Resonator => new Color(255, 230, 80),
        _ => Color.Gray,
    };
}
