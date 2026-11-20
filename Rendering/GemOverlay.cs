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
/// Оверлей надетых предметов: рамка слота, рамки сокетов, линии линков и компактный список гемов.
/// M4b: посокетная геометрия считается из размеров предмета/инвентаря, прочитанных в Tick.
/// </summary>
public static class GemOverlay
{
    // Высота строки списка гемов (подобрана под стандартный шрифт HUD).
    private const float LineHeight = 14f;

    // Отступ текста от рамки предмета.
    private const float TextGap = 6f;

    // Цвет диагностической подписи слота.
    private static readonly Color DebugSlotColor = new(0, 255, 255);

    /// <summary>Рисует рамки и подписи. Никогда не бросает исключений наружу.</summary>
    public static void Draw(GameController gameController, GemHelperSettings settings, GemSnapshot snapshot, Graphics graphics)
    {
        if (gameController is null || settings is null || snapshot is null || graphics is null)
            return;

        if (!settings.ShowOverlay.Value)
            return;

        try
        {
            var panel = gameController.IngameState?.IngameUi?.InventoryPanel;

            // Панель инвентаря закрыта/недоступна — рисовать нечего.
            if (panel is null || !panel.IsValid || !panel.IsVisible)
                return;

            var windowWidth = TryGetWindowWidth(gameController);
            var lineHeight = ResolveLineHeight(graphics);
            var showExp = settings.OverlayShowExp.Value;

            // Сокеты в снимке идут в порядке слотов, поэтому группы — подряд идущие записи слота.
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
            // Ошибки геометрии/памяти не должны валить рендер.
        }
    }

    /// <summary>Рамка одного предмета + подписи гемов этого слота.</summary>
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

        if (!EquipmentLayout.TryGetRect(gameController, slot, out var rect))
            return;

        // Диагностика: подпись найденного слота в его левом верхнем углу.
        // Перегрузка с SharpDX.Vector2 помечена [Obsolete] в 330.5 — используем System.Numerics.
        if (settings.OverlayDebugSlots.Value)
            graphics.DrawText(slot.ToString(), new Vector2(rect.X, rect.Y), DebugSlotColor);

        // Считаем гемы и заранее измеряем строки (ширина нужна для решения «справа или слева»).
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

        // M4b: рамки сокетов и линии линков внутри предмета. Геометрия нужна и для линий «текст → сокет».
        var boxes = DrawSocketGeometry(settings, sockets, start, end, rect, graphics);

        if (gemCount == 0 || !settings.OverlayShowGemNames.Value)
            return;

        var showIndex = settings.OverlaySocketFrames.Value;
        var lines = new (string Text, Color Color, int SocketIndex, float Width)[gemCount];
        var maxWidth = 0f;
        var index = 0;

        for (var i = start; i < end; i++)
        {
            var socket = sockets[i];
            if (!HasGem(socket))
                continue;

            var text = BuildGemLine(socket, showExp, showIndex);
            var width = graphics.MeasureText(text).X;

            lines[index++] = (text, ToColor(socket.SocketColor), socket.SocketIndex, width);

            if (width > maxWidth)
                maxWidth = width;
        }

        // По умолчанию текст справа от рамки; у правого края экрана переносим влево от рамки.
        var x = rect.Right + TextGap;
        if (windowWidth > 0f && x + maxWidth > windowWidth)
            x = rect.Left - maxWidth - TextGap;

        var yBase = rect.Top;

        // Смещение блока: общее для всех слотов + индивидуальное для этого слота.
        var slotOffset = settings.TextOffsets.For(slot);
        x += settings.OverlayTextOffsetX.Value + slotOffset.X.Value;
        yBase += settings.OverlayTextOffsetY.Value + slotOffset.Y.Value;

        if (x < 0f)
            x = 0f;
        if (yBase < 0f)
            yBase = 0f;

        // Линии «строка текста → сокет» рисуем до текста, чтобы подписи были сверху.
        if (settings.OverlayTextToSocketLines.Value && boxes.Length > 0)
            DrawTextToSocketLines(lines, boxes, x, yBase, lineHeight, maxWidth, rect, graphics);

        for (var i = 0; i < lines.Length; i++)
        {
            var y = yBase + i * lineHeight;
            graphics.DrawTextWithBackground(lines[i].Text, new Vector2(x, y), lines[i].Color, Color.Black);
        }
    }

    /// <summary>
    /// Тонкие линии от края блока подписей к центру соответствующего сокета.
    /// Анкер по X зависит от стороны: текст справа — линия начинается у его левого края,
    /// текст слева — у правого края блока (см. <c>textOnRight</c>).
    /// </summary>
    private static void DrawTextToSocketLines(
        (string Text, Color Color, int SocketIndex, float Width)[] lines,
        RectangleF[] boxes,
        float x,
        float yBase,
        float lineHeight,
        float maxWidth,
        RectangleF rect,
        Graphics graphics)
    {
        // С какой стороны от предмета стоит блок текста.
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

            // Не-obsolete перегрузка 330.5 — System.Numerics.Vector2.
            graphics.DrawLine(
                new Vector2(anchorX, lineY),
                new Vector2(center.X, center.Y),
                1.5f,
                lines[i].Color);
        }
    }

    /// <summary>
    /// Посокетная геометрия предмета: рамки сокетов по цвету и линии линков между соседними сокетами.
    /// Геометрия берётся из снимка (ItemSizeX/Y, InventoryColumns/Rows, InventoryType, TotalSockets).
    /// Возвращает прямоугольники сокетов (для линий «текст → сокет») или пустой массив.
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

        // totalSockets: максимум из снимка, иначе — число записей группы.
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
            // Кривая геометрия не должна ломать оверлей.
            rects = Array.Empty<RectangleF>();
        }

        if (rects.Length == 0)
            return Array.Empty<RectangleF>();

        // SocketIndex -> запись снимка (нужна для цвета рамки).
        var byIndex = new Dictionary<int, GemSocketSnapshot>(end - start);
        for (var i = start; i < end; i++)
            byIndex[sockets[i].SocketIndex] = sockets[i];

        var count = Math.Min(totalSockets, rects.Length);
        var boxes = new RectangleF[count];

        for (var i = 0; i < count; i++)
        {
            var box = SocketLayout.HalveRectangle(rects[i]);
            boxes[i] = box;

            if (!settings.OverlaySocketFrames.Value)
                continue;

            if (box.Width <= 0f || box.Height <= 0f)
                continue;

            var color = byIndex.TryGetValue(i, out var socket) ? ToColor(socket.SocketColor) : Color.Gray;
            graphics.DrawFrame(box, color, 2);
        }

        if (settings.OverlayLinkLines.Value)
            DrawLinkLines(byIndex, boxes, graphics);

        return boxes;
    }

    /// <summary>Линии внутри групп линков: соединяем соседние (по SocketIndex) сокеты одной группы.</summary>
    private static void DrawLinkLines(
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

                // Перегрузка с SharpDX.Vector2 помечена [Obsolete] в 330.5 — используем System.Numerics.
                var centerA = boxA.Center;
                var centerB = boxB.Center;

                graphics.DrawLine(
                    new Vector2(centerA.X, centerA.Y),
                    new Vector2(centerB.X, centerB.Y),
                    2f,
                    Color.Yellow);
            }
        }
    }

    /// <summary>
    /// Высота строки подписи: не меньше константы, но растёт под крупный шрифт HUD.
    /// Ошибка измерения не критична — откатываемся к константе.
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

    /// <summary>Строка гема: <c>[i] Имя L{level} [Q{quality}] [42%|MAX]</c>.</summary>
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

    /// <summary>Цвет подписи по цвету сокета (палитра M4a).</summary>
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
