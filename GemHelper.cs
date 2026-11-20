using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Windows.Forms; // Keys — для хоткеев
using ExileCore;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Nodes;
using GemHelper.Core;
using GemHelper.Rendering;
using SocketColorEnum = ExileCore.Shared.Enums.SocketColor;

namespace GemHelper;

public class GemHelper : BaseSettingsPlugin<GemHelperSettings>
{
    // Палитра для таблицы (M3). Цвета сокетов — по ТЗ.
    private static readonly Vector4 SocketRed = new(1f, 0.35f, 0.35f, 1f);
    private static readonly Vector4 SocketGreen = new(0.4f, 1f, 0.4f, 1f);
    private static readonly Vector4 SocketBlue = new(0.45f, 0.6f, 1f, 1f);
    private static readonly Vector4 SocketWhite = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 SocketAbyssal = new(0.75f, 0.45f, 1f, 1f);
    private static readonly Vector4 SocketResonator = new(1f, 0.9f, 0.3f, 1f);
    private static readonly Vector4 SocketUnknown = new(0.6f, 0.6f, 0.6f, 1f);

    private static readonly Vector4 TextNormal = new(0.92f, 0.92f, 0.92f, 1f);
    private static readonly Vector4 TextDim = new(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Vector4 TextFallback = new(1f, 0.6f, 0.2f, 1f); // маркер R1-fallback
    private static readonly Vector4 GroupHeaderText = new(0.85f, 0.92f, 1f, 1f);
    private static readonly Vector4 GroupHeaderBg = new(0.20f, 0.27f, 0.38f, 0.85f);

    // Снимок строится только в Tick, Render его лишь читает (никаких обращений к памяти игры в рендере).
    private GemSnapshot _snapshot = GemSnapshot.Empty;

    // Момент (UTC), раньше которого следующий Tick не будет пересобирать снимок.
    private DateTime _nextCaptureUtc = DateTime.MinValue;

    // Кэш по ServerRequestCounter: читаем предметы только когда сервер сообщил об изменениях.
    private int _lastServerCounter;
    private bool _hasServerCounter;
    private DateTime _lastFullRefreshUtc = DateTime.MinValue;

    // Статистика (только для отладочной панели).
    private double _lastCaptureMs;
    private int _captureCount;
    private int _skippedCount;
    private DateTime _lastCaptureUtc = DateTime.MinValue;

    public override bool Initialise()
    {
        _snapshot = GemSnapshot.Empty;
        _nextCaptureUtc = DateTime.MinValue;

        _lastServerCounter = 0;
        _hasServerCounter = false;
        _lastFullRefreshUtc = DateTime.MinValue;

        _lastCaptureMs = 0;
        _captureCount = 0;
        _skippedCount = 0;
        _lastCaptureUtc = DateTime.MinValue;

        // Хоткеи: регистрируем и перерегистрируем при смене значения в настройках.
        RegisterHotkey(Settings.ToggleWindowHotkey);
        RegisterHotkey(Settings.ToggleOverlayHotkey);

        // Кнопка «дамп снимка»: показываем текущий GemSnapshot в инспекторе HUD.
        Settings.DumpSnapshotButton.OnPressed = () =>
        {
            var controller = GameController;
            controller?.InspectObject(_snapshot, "GemHelper snapshot");
        };

        return true;
    }

    /// <summary>Регистрирует хоткей в HUD и обновляет регистрацию при его переназначении.</summary>
#pragma warning disable CS0618 // HotkeyNode помечен [Obsolete] в 330.5, но рабочий и используется другими плагинами HUD
    private static void RegisterHotkey(HotkeyNode hotkey)
    {
        Input.RegisterKey(hotkey);
        hotkey.OnValueChanged += () => Input.RegisterKey(hotkey);
    }
#pragma warning restore CS0618

    public override void AreaChange(AreaInstance area)
    {
        // Смена зоны/акта: сбрасываем снимок и кэш, чтобы первый же Tick прочитал данные заново.
        _snapshot = GemSnapshot.Empty;
        _nextCaptureUtc = DateTime.MinValue;

        _lastServerCounter = 0;
        _hasServerCounter = false;
        _lastFullRefreshUtc = DateTime.MinValue;
    }

    public override Job? Tick()
    {
        var gameController = GameController;
        if (gameController is null)
            return null;

        // Хоткеи обрабатываем до проверки InGame: окно и оверлей можно переключать и вне игры.
        if (Settings.ToggleWindowHotkey.PressedOnce())
            Settings.ShowDebugSnapshot.Value = !Settings.ShowDebugSnapshot.Value;

        if (Settings.ToggleOverlayHotkey.PressedOnce())
            Settings.ShowOverlay.Value = !Settings.ShowOverlay.Value;

        // Вне игры читать нечего.
        bool inGame;
        try
        {
            inGame = gameController.InGame;
        }
        catch
        {
            inGame = false;
        }

        if (!inGame)
            return null;

        // Throttle: не чаще, чем раз в UpdateIntervalMs (настройка, 50..2000 мс).
        var intervalMs = Math.Clamp(Settings.UpdateIntervalMs.Value, 50, 2000);
        var now = DateTime.UtcNow;
        if (_nextCaptureUtc > now)
            return null;

        _nextCaptureUtc = now.AddMilliseconds(intervalMs);

        try
        {
            // 1. Дешёвый отпечаток инвентарей (без чтения предметов/гемов).
            var counter = GemSnapshotReader.CaptureServerCounter(gameController);

            // 2. Полное обновление нужно, если снимка ещё нет или истёк страховочный интервал.
            var fullRefreshMs = Math.Clamp(Settings.FullRefreshIntervalMs.Value, 500, 10000);
            var needFullRefresh = _snapshot.IsEmpty
                                  || now - _lastFullRefreshUtc >= TimeSpan.FromMilliseconds(fullRefreshMs);

            // 3. Сервер ничего не менял — память предметов не трогаем.
            if (_hasServerCounter && counter == _lastServerCounter && !needFullRefresh)
            {
                _skippedCount++;
                return null;
            }

            // 4. Реальное чтение снимка с замером времени.
            var stopwatch = Stopwatch.StartNew();
            var fresh = GemSnapshotReader.Capture(gameController);
            stopwatch.Stop();

            _lastCaptureMs = stopwatch.Elapsed.TotalMilliseconds;
            _lastCaptureUtc = now;
            _captureCount++;

            _lastServerCounter = counter;
            _hasServerCounter = true;

            if (needFullRefresh)
                _lastFullRefreshUtc = now;

            // 5. Если содержимое не изменилось — оставляем прежний объект (меньше давления на GC,
            //    а CapturedAtUtc продолжает означать «время последнего изменения содержимого»).
            if (!_snapshot.IsSameContent(fresh))
                _snapshot = fresh;
        }
        catch
        {
            // Capture/CaptureServerCounter сами всё ловят; это последняя линия обороны, чтобы не валить Tick.
        }

        return null;
    }

    public override void Render()
    {
        // Локальная копия ссылки: рендер не должен видеть частично обновлённое состояние.
        var snapshot = _snapshot;

        // Отладочное окно и оверлей независимы: каждый управляется своей настройкой.
        if (Settings.ShowDebugSnapshot.Value)
            DrawDebugWindow(snapshot);

        // M4a: оверлей на панели инвентаря. Использует только готовый снимок и UI-геометрию,
        // память предметов здесь не читается.
        if (Settings.ShowOverlay.Value)
            GemOverlay.Draw(GameController, Settings, snapshot, Graphics);
    }

    /// <summary>Отладочное ImGui-окно: статистика чтения и таблица сокетов.</summary>
    private void DrawDebugWindow(GemSnapshot snapshot)
    {
        ImGui.SetNextWindowSize(new Vector2(820, 460), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(420, 180), new Vector2(2000, 2000));

        // Уникальный ID окна: заголовок остаётся «GemHelper», но ID стабилен.
        if (!ImGui.Begin("GemHelper##GemHelperDebug"))
        {
            ImGui.End();
            return;
        }

        DrawStatistics(snapshot);
        ImGui.Separator();
        DrawSocketsTable(snapshot);

        ImGui.End();
    }

    /// <summary>Шапка со статистикой чтения (только поля, обновлённые в Tick).</summary>
    private void DrawStatistics(GemSnapshot snapshot)
    {
        var counterText = _hasServerCounter ? $"0x{_lastServerCounter:X8}" : "n/a";
        var lastCaptureText = _lastCaptureUtc == DateTime.MinValue
            ? "—"
            : _lastCaptureUtc.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

        ImGui.TextUnformatted(
            $"сокеты={snapshot.SocketCount} гемы={snapshot.GemCount} | " +
            $"capture={_lastCaptureMs.ToString("0.00", CultureInfo.InvariantCulture)} ms | " +
            $"reads={_captureCount} skip={_skippedCount} | " +
            $"counter={counterText} | " +
            $"hash=0x{snapshot.ContentHash:X8} | " +
            lastCaptureText);

        if (snapshot.IsEmpty)
            ImGui.TextUnformatted("Снимок пуст (нет надетых предметов с сокетами).");
    }

    /// <summary>
    /// Таблица сокетов, сгруппированная по предметам: строка-заголовок предмета,
    /// затем строки его сокетов. Сокеты в снимке уже идут в порядке слотов/сокетов,
    /// поэтому группы — это подряд идущие записи с одинаковыми (Slot, ItemAddress).
    /// </summary>
    private void DrawSocketsTable(GemSnapshot snapshot)
    {
        if (snapshot.IsEmpty)
            return;

        var compact = Settings.CompactMode.Value;
        var showExp = Settings.ShowExp.Value;
        var showEmpty = Settings.ShowEmptySockets.Value;

        // Компактный режим прячет «Цвет» и «Req», ShowExp добавляет «Exp».
        var columns = 6 + (compact ? 0 : 2) + (showExp ? 1 : 0);

        var available = ImGui.GetContentRegionAvail();
        if (available.Y < 32f)
            return;

        var flags = ImGuiTableFlags.Borders
                    | ImGuiTableFlags.RowBg
                    | ImGuiTableFlags.Resizable
                    | ImGuiTableFlags.SizingFixedFit
                    | ImGuiTableFlags.ScrollY;

        // ID зависит от числа колонок: ImGui не любит менять их количество у одной и той же таблицы.
        if (!ImGui.BeginTable($"gemhelper_table_{columns}", columns, flags, new Vector2(0, available.Y)))
            return;

        ImGui.TableSetupColumn("Слот");
        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 34);
        if (!compact)
            ImGui.TableSetupColumn("Цвет", ImGuiTableColumnFlags.WidthFixed, 46);
        ImGui.TableSetupColumn("Link", ImGuiTableColumnFlags.WidthFixed, 58);
        ImGui.TableSetupColumn("Гем");
        ImGui.TableSetupColumn("Ур", ImGuiTableColumnFlags.WidthFixed, 40);
        ImGui.TableSetupColumn("К", ImGuiTableColumnFlags.WidthFixed, 36);
        if (!compact)
            ImGui.TableSetupColumn("Req", ImGuiTableColumnFlags.WidthFixed, 44);
        if (showExp)
            ImGui.TableSetupColumn("Exp", ImGuiTableColumnFlags.WidthFixed, 84);

        ImGui.TableHeadersRow();

        var sockets = snapshot.Sockets;
        var start = 0;

        while (start < sockets.Count)
        {
            var first = sockets[start];

            // Группа = подряд идущие сокеты одного и того же предмета.
            var end = start + 1;
            while (end < sockets.Count
                   && sockets[end].Slot == first.Slot
                   && sockets[end].ItemAddress == first.ItemAddress)
            {
                end++;
            }

            DrawItemHeaderRow(first, end - start);

            for (var i = start; i < end; i++)
            {
                var socket = sockets[i];
                if (socket.IsEmpty && !showEmpty)
                    continue;

                DrawSocketRow(socket, compact, showExp);
            }

            start = end;
        }

        ImGui.EndTable();
    }

    /// <summary>Строка-заголовок предмета: слот, имя, класс и число сокетов в первой колонке.</summary>
    private static void DrawItemHeaderRow(GemSocketSnapshot first, int socketCount)
    {
        ImGui.TableNextRow();
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.ColorConvertFloat4ToU32(GroupHeaderBg));
        ImGui.TableSetColumnIndex(0);

        TextColoredSafe(GroupHeaderText, BuildItemHeader(first, socketCount));

        // Адрес предмета — в подсказке, чтобы не раздувать строку.
        if (first.ItemAddress != 0 && ImGui.IsItemHovered())
            ImGui.SetTooltip($"ItemAddress=0x{first.ItemAddress:X}");
    }

    private static string BuildItemHeader(GemSocketSnapshot first, int socketCount)
    {
        var name = string.IsNullOrEmpty(first.ItemName) ? "?" : first.ItemName;
        var className = string.IsNullOrEmpty(first.ItemClassName)
            ? string.Empty
            : $" [{first.ItemClassName}]";

        return $"{first.Slot} — {name}{className} — сокетов={socketCount}";
    }

    /// <summary>Строка одного сокета: цвет, линк, гем и числовые характеристики.</summary>
    private static void DrawSocketRow(GemSocketSnapshot socket, bool compact, bool showExp)
    {
        var isEmpty = socket.IsEmpty;
        var socketColor = ToColor(socket.SocketColor);
        var plainColor = isEmpty ? TextDim : TextNormal;

        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        TextColoredSafe(plainColor, socket.Slot.ToString());

        ImGui.TableNextColumn();
        TextColoredSafe(plainColor, socket.SocketIndex.ToString(CultureInfo.InvariantCulture));

        if (!compact)
        {
            ImGui.TableNextColumn();
            TextColoredSafe(isEmpty ? TextDim : socketColor, socket.SocketColorCode);
        }

        // Link: для R1-fallback LinkGroup неизвестен (-1) — показываем оранжевый маркер.
        var isFallback = socket.LinkGroup < 0;
        var linkText = isFallback
            ? "?"
            : socket.LinkGroup.ToString(CultureInfo.InvariantCulture);

        if (compact)
            linkText = $"{socket.SocketColorCode}·{linkText}"; // цвет не теряется при скрытой колонке
        else if (isFallback)
            linkText = "? R1";

        ImGui.TableNextColumn();
        TextColoredSafe(isFallback ? TextFallback : plainColor, linkText);

        // Гем: пустой сокет — приглушённый «empty», иначе имя цветом сокета.
        ImGui.TableNextColumn();
        if (isEmpty)
            TextColoredSafe(TextDim, "empty");
        else
            TextColoredSafe(socketColor, string.IsNullOrEmpty(socket.GemName) ? "—" : socket.GemName);

        ImGui.TableNextColumn();
        TextColoredSafe(plainColor, isEmpty ? "—" : socket.GemLevel.ToString(CultureInfo.InvariantCulture));

        ImGui.TableNextColumn();
        TextColoredSafe(plainColor, isEmpty ? "—" : socket.GemQuality.ToString(CultureInfo.InvariantCulture));

        if (!compact)
        {
            ImGui.TableNextColumn();
            TextColoredSafe(plainColor, isEmpty ? "—" : socket.RequiredLevel.ToString(CultureInfo.InvariantCulture));
        }

        if (showExp)
        {
            ImGui.TableNextColumn();

            if (isEmpty)
            {
                TextColoredSafe(plainColor, "—");
            }
            else
            {
                var expText = socket.IsMaxLevel
                    ? "MAX"
                    : socket.ExpPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%";

                TextColoredSafe(plainColor, expText);

                // Сырые значения — в подсказке, чтобы не занимать колонку.
                if (ImGui.IsItemHovered())
                {
                    var maxLevelText = socket.MaxLevel > 0
                        ? socket.MaxLevel.ToString(CultureInfo.InvariantCulture)
                        : "?";

                    ImGui.SetTooltip(
                        $"L{socket.GemLevel.ToString(CultureInfo.InvariantCulture)}/{maxLevelText}  " +
                        $"total={socket.TotalExp.ToString("N0", CultureInfo.InvariantCulture)}  " +
                        $"prev={socket.ExpPrev.ToString("N0", CultureInfo.InvariantCulture)}  " +
                        $"max={socket.ExpMax.ToString("N0", CultureInfo.InvariantCulture)}  " +
                        $"next={socket.ExpToNext.ToString("N0", CultureInfo.InvariantCulture)}");
                }
            }
        }
    }

    /// <summary>Цвет текста по цвету сокета (палитра из ТЗ).</summary>
    private static Vector4 ToColor(SocketColorEnum socketColor) => socketColor switch
    {
        SocketColorEnum.Red => SocketRed,
        SocketColorEnum.Green => SocketGreen,
        SocketColorEnum.Blue => SocketBlue,
        SocketColorEnum.White => SocketWhite,
        SocketColorEnum.Abyssal => SocketAbyssal,
        SocketColorEnum.Resonator => SocketResonator,
        _ => SocketUnknown,
    };

    /// <summary>
    /// Hexa-перегрузка <c>TextColored(Vector4, string)</c> трактует строку как printf-формат,
    /// поэтому экранируем «%» (в именах гемов его быть не должно, но это дешёвая страховка).
    /// </summary>
    private static void TextColoredSafe(Vector4 color, string text)
        => ImGui.TextColored(color, text.Replace("%", "%%"));

    public override void EntityAdded(Entity entity)
    {
        // M3: пер-сущностная обработка не нужна — снимок читается из PlayerInventories по счётчику/таймеру.
    }
}
