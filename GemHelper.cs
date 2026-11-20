using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Forms; // MouseButtons — for Input clicks
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Nodes;
using GemHelper.Core;
using GemHelper.Layout;
using GemHelper.Rendering;
using SocketColorEnum = ExileCore.Shared.Enums.SocketColor;

namespace GemHelper;

public class GemHelper : BaseSettingsPlugin<GemHelperSettings>
{
    // Table palette (M3). Socket colors as specified.
    private static readonly Vector4 SocketRed = new(1f, 0.35f, 0.35f, 1f);
    private static readonly Vector4 SocketGreen = new(0.4f, 1f, 0.4f, 1f);
    private static readonly Vector4 SocketBlue = new(0.45f, 0.6f, 1f, 1f);
    private static readonly Vector4 SocketWhite = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 SocketAbyssal = new(0.75f, 0.45f, 1f, 1f);
    private static readonly Vector4 SocketResonator = new(1f, 0.9f, 0.3f, 1f);
    private static readonly Vector4 SocketUnknown = new(0.6f, 0.6f, 0.6f, 1f);

    private static readonly Vector4 TextNormal = new(0.92f, 0.92f, 0.92f, 1f);
    private static readonly Vector4 TextDim = new(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Vector4 TextFallback = new(1f, 0.6f, 0.2f, 1f); // R1-fallback marker
    private static readonly Vector4 GroupHeaderText = new(0.85f, 0.92f, 1f, 1f);
    private static readonly Vector4 GroupHeaderBg = new(0.20f, 0.27f, 0.38f, 0.85f);

    // The snapshot is built in Tick only; Render merely reads it (no game memory access during render).
    private GemSnapshot _snapshot = GemSnapshot.Empty;

    // UTC time before which the next Tick will not rebuild the snapshot.
    private DateTime _nextCaptureUtc = DateTime.MinValue;

    // Cache by ServerRequestCounter: read items only when the server reports changes.
    private int _lastServerCounter;
    private bool _hasServerCounter;
    private DateTime _lastFullRefreshUtc = DateTime.MinValue;

    // Statistics (debug panel only).
    private double _lastCaptureMs;
    private int _captureCount;
    private int _skippedCount;
    private DateTime _lastCaptureUtc = DateTime.MinValue;

    // === Gem swap in an equipped socket (clicks in Tick) ===
    // Operation timings live in the Settings.SwapTiming submenu (StepDelayMs, TimeoutMs, CompletionTimeoutMs).
    // Waiting for game focus must not consume the whole timeout: the user may click into the game.

    // Build tag: shown in the GemHelper window to confirm the HUD loaded a fresh DLL.
    private const string BuildTag = "2026-11-20-weaponset";

    private enum SwapStep
    {
        None,
        RightClickSocket,
        WaitCursorOriginal,
        LeftClickInventory,
        WaitCursorTarget,
        LeftClickSocket,
        WaitCursorEmpty,
    }

    private SwapStep _swapStep = SwapStep.None;
    private DateTime _swapStepAtUtc = DateTime.MinValue;
    private DateTime _swapStartedUtc = DateTime.MinValue;
    private GemSocketSnapshot? _swapSocket;
    private InventoryGemInfo? _swapTarget;

    // === Unequip all gems of an item into the inventory (clicks in Tick) ===
    private enum UnequipStep
    {
        None,
        RightClickSocket,
        WaitCursorGem,
        ClickEmptyCell,
        WaitCursorEmpty,
    }

    private UnequipStep _unequipStep = UnequipStep.None;
    private DateTime _unequipStepAtUtc = DateTime.MinValue;
    private DateTime _unequipStartedUtc = DateTime.MinValue;
    private readonly List<GemSocketSnapshot> _unequipQueue = new();
    private int _unequipIndex;

    // Second weapon set: while it is inactive, Weapon2/Offhand2 sockets are not clicked.
    // Before clicking, press the weapon-set swap key; after the operation, restore the original set.
    private bool _weaponSetToggled;
    private bool _weaponSetRetryWithoutToggle;
    private Vector2 _unequipClickedCenter;
    private Vector2 _swapClickedCenter;

    // Status of the last Swap/Unequip operation — shown in the GemHelper window.
    private string _operationStatus = "idle";

    // Button click counters (diagnostics: whether clicks reach the handler).
    private int _swapButtonClicks;
    private int _unequipButtonClicks;

    private void SetOperationStatus(string status) => _operationStatus = status;

    // ==================== Second weapon set ====================

    /// <summary>Second weapon set slots (inactive until the set is switched).</summary>
    private static bool IsSecondWeaponSet(InventorySlotE slot)
        => slot is InventorySlotE.Weapon2 or InventorySlotE.Offhand2;

    /// <summary>Active slot where the game draws the second set after switching.</summary>
    private static InventorySlotE GetActiveSlot(InventorySlotE slot)
        => slot switch
        {
            InventorySlotE.Weapon2 => InventorySlotE.Weapon1,
            InventorySlotE.Offhand2 => InventorySlotE.Offhand1,
            _ => slot,
        };

    /// <summary>Slot whose geometry is used for socket clicks (the second set is drawn over the first set slot).</summary>
    private InventorySlotE GetClickSlot(InventorySlotE slot)
        => IsSecondWeaponSet(slot) && (_weaponSetToggled || _weaponSetRetryWithoutToggle)
            ? GetActiveSlot(slot)
            : slot;

    /// <summary>Press the weapon-set swap key. Returns false when the key is unbound or input is unavailable.</summary>
    private bool PressWeaponSwapKey()
    {
        try
        {
            var key = Settings.SwapTiming.WeaponSwapKey.Value.Key;
            if (key == Keys.None)
                key = Keys.X;

            // Input.KeyPress is a coroutine: the key is not pressed without the update loop, so send down/up manually.
            Input.KeyDown(key);
            System.Threading.Thread.Sleep(40);
            Input.KeyUp(key);

            DebugWindow.LogMsg($"[GemHelper] weapon swap key {key}", 3f);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Restore the original weapon set if the operation switched it.</summary>
    private void RestoreWeaponSetIfNeeded()
    {
        if (!_weaponSetToggled)
            return;

        _weaponSetToggled = false;
        PressWeaponSwapKey();
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private const uint RdwInvalidate = 0x0001;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwUpdateNow = 0x0100;

    private const int SwHide = 0;
    private const int SwShowNa = 8; // SW_SHOWNA: show without activating

    // ExileCore HUD overlay (ClickableTransparentOverlay) is the native topmost "ExileApi" window.
    // While it is visible, Input clicks go to it instead of the game; hide it during operations.
    private static IntPtr _overlayWindow;
    private static bool _overlayHidden;

    private void HideOverlayForInput(GameController gameController)
    {
        if (_overlayHidden)
            return;

        try
        {
            // HUD hiding can be disabled in settings — then behave as before.
            if (!Settings.SwapTiming.HideOverlayDuringOperations.Value)
                return;

            _overlayWindow = FindWindow(null, "ExileApi");
            if (_overlayWindow != IntPtr.Zero)
            {
                ShowWindow(_overlayWindow, SwHide);
                _overlayHidden = true;
            }

            // Return focus to the game after hiding the overlay.
            var gameWindow = gameController.Window.Process?.MainWindowHandle ?? IntPtr.Zero;
            if (gameWindow != IntPtr.Zero)
                SetForegroundWindow(gameWindow);
        }
        catch
        {
            // Ignore: on failure behave as before.
        }
    }

    private static void RestoreOverlay()
    {
        if (!_overlayHidden)
            return;

        try
        {
            // The window may have been recreated (or the handle was not found): look it up again.
            if (_overlayWindow == IntPtr.Zero || !IsWindow(_overlayWindow))
                _overlayWindow = FindWindow(null, "ExileApi");

            if (_overlayWindow != IntPtr.Zero)
            {
                // Force the window to show (SWP_SHOWWINDOW) and redraw immediately:
                // so the HUD appears in the same frame instead of sticking for a few seconds.
                SetWindowPos(
                    _overlayWindow,
                    HwndTopmost,
                    0, 0, 0, 0,
                    SwpNoSize | SwpNoMove | SwpNoActivate | SwpShowWindow);

                ShowWindow(_overlayWindow, SwShowNa); // SW_SHOWNA — show without activating
                RedrawWindow(_overlayWindow, IntPtr.Zero, IntPtr.Zero,
                    RdwInvalidate | RdwAllChildren | RdwUpdateNow);
            }
        }
        catch
        {
            // Ignore.
        }

        _overlayHidden = false;
        _overlayWindow = IntPtr.Zero;
    }

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

        // Hotkeys: register and re-register when the setting value changes.
        RegisterHotkey(Settings.ToggleWindowHotkey);
        RegisterHotkey(Settings.ToggleOverlayHotkey);

        // "Dump snapshot" button: show the current GemSnapshot in the HUD inspector.
        Settings.DumpSnapshotButton.OnPressed = () =>
        {
            var controller = GameController;
            controller?.InspectObject(_snapshot, "GemHelper snapshot");
        };

        return true;
    }

    /// <summary>Plugin is unloading/closing: restore the native HUD overlay window.</summary>
    public override void OnClose()
    {
        RestoreWeaponSetIfNeeded();
        RestoreOverlay();
        base.OnClose();
    }

    /// <summary>Registers a hotkey in the HUD and refreshes the registration when it is rebound.</summary>
#pragma warning disable CS0618 // HotkeyNode is marked [Obsolete] in 330.5 but still works and is used by other HUD plugins
    private static void RegisterHotkey(HotkeyNode hotkey)
    {
        Input.RegisterKey(hotkey);
        hotkey.OnValueChanged += () => Input.RegisterKey(hotkey);
    }
#pragma warning restore CS0618

    public override void AreaChange(AreaInstance area)
    {
        // Area/act change: reset the snapshot and cache so the next Tick re-reads the data.
        _snapshot = GemSnapshot.Empty;
        _nextCaptureUtc = DateTime.MinValue;

        _lastServerCounter = 0;
        _hasServerCounter = false;
        _lastFullRefreshUtc = DateTime.MinValue;

        // Unfinished operations are no longer relevant after an area change.
        ResetSwap();
        ResetUnequip();
        RestoreOverlay();
        SetOperationStatus("idle");
    }

    public override Job? Tick()
    {
        // Safety net: the operation finished/reset but the overlay is still hidden — restore it.
        if (_swapStep == SwapStep.None && _unequipStep == UnequipStep.None)
            RestoreOverlay();

        var gameController = GameController;
        if (gameController is null)
            return null;

        // Gem swap/unequip: all memory/input work happens in Tick (Render only starts it).
        ProcessSwap(gameController);
        ProcessUnequip(gameController);

        // Handle hotkeys before the InGame check: window and overlay can be toggled outside the game.
        if (Settings.ToggleWindowHotkey.PressedOnce())
            Settings.ShowDebugSnapshot.Value = !Settings.ShowDebugSnapshot.Value;

        if (Settings.ToggleOverlayHotkey.PressedOnce())
            Settings.ShowOverlay.Value = !Settings.ShowOverlay.Value;

        // Nothing to read outside the game.
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

        // Throttle: no more often than once per UpdateIntervalMs (setting, 50..2000 ms).
        var intervalMs = Math.Clamp(Settings.UpdateIntervalMs.Value, 50, 2000);
        var now = DateTime.UtcNow;
        if (_nextCaptureUtc > now)
            return null;

        _nextCaptureUtc = now.AddMilliseconds(intervalMs);

        try
        {
            // 1. Cheap inventory fingerprint (no item/gem reads).
            var counter = GemSnapshotReader.CaptureServerCounter(gameController);

            // 2. A full refresh is needed when there is no snapshot yet or the safety interval elapsed.
            var fullRefreshMs = Math.Clamp(Settings.FullRefreshIntervalMs.Value, 500, 10000);
            var needFullRefresh = _snapshot.IsEmpty
                                  || now - _lastFullRefreshUtc >= TimeSpan.FromMilliseconds(fullRefreshMs);

            // 3. The server reported no changes — do not touch item memory.
            if (_hasServerCounter && counter == _lastServerCounter && !needFullRefresh)
            {
                _skippedCount++;
                return null;
            }

            // 4. Actual snapshot read with timing.
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

            // 5. If the content did not change, keep the previous object (less GC pressure,
            //    and CapturedAtUtc still means "time of the last content change").
            if (!_snapshot.IsSameContent(fresh))
                _snapshot = fresh;
        }
        catch
        {
            // Capture/CaptureServerCounter handle their own errors; this is the last line of defense to keep Tick alive.
        }

        return null;
    }

    public override void Render()
    {
        // Safety net: the operation finished/reset but the overlay is still hidden — restore it on this frame.
        if (_swapStep == SwapStep.None && _unequipStep == UnequipStep.None)
            RestoreOverlay();

        // Local reference copy: render must not see partially updated state.
        var snapshot = _snapshot;

        // Debug window and overlay are independent: each is controlled by its own setting.
        if (Settings.ShowDebugSnapshot.Value)
            DrawDebugWindow(snapshot);

        // M4a: overlay on the inventory panel. Uses only the ready snapshot and UI geometry;
        // item memory is not read here.
        if (Settings.ShowOverlay.Value)
            GemOverlay.Draw(GameController, Settings, snapshot, Graphics);
    }

    /// <summary>Debug ImGui window: read statistics and the socket table.</summary>
    private void DrawDebugWindow(GemSnapshot snapshot)
    {
        ImGui.SetNextWindowSize(new Vector2(820, 460), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(420, 180), new Vector2(2000, 2000));

        // During Swap/Unequip the window keeps drawing (operation/status visible) but without input:
        // the overlay must not capture the cursor or cover sockets/inventory.
        var operationActive = _swapStep != SwapStep.None || _unequipStep != UnequipStep.None;
        var flags = operationActive ? ImGuiWindowFlags.NoInputs : ImGuiWindowFlags.None;
        if (operationActive)
            ImGui.SetNextWindowBgAlpha(0.35f);

        // Unique window ID: the title stays "GemHelper" but the ID is stable.
        if (!ImGui.Begin("GemHelper##GemHelperDebug", flags))
        {
            ImGui.End();
            return;
        }

        DrawStatistics(snapshot);
        ImGui.Separator();
        DrawSocketsTable(snapshot);

        ImGui.End();
    }

    /// <summary>Read statistics header (only fields updated in Tick).</summary>
    private void DrawStatistics(GemSnapshot snapshot)
    {
        var counterText = _hasServerCounter ? $"0x{_lastServerCounter:X8}" : "n/a";
        var lastCaptureText = _lastCaptureUtc == DateTime.MinValue
            ? "—"
            : _lastCaptureUtc.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

        ImGui.TextUnformatted(
            $"sockets={snapshot.SocketCount} gems={snapshot.GemCount} | " +
            $"capture={_lastCaptureMs.ToString("0.00", CultureInfo.InvariantCulture)} ms | " +
            $"reads={_captureCount} skip={_skippedCount} | " +
            $"counter={counterText} | " +
            $"hash=0x{snapshot.ContentHash:X8} | " +
            lastCaptureText);

        // Swap/Unequip status: visible right in the window even when the HUD log is not.
        ImGui.TextUnformatted($"op: {_operationStatus}");
        ImGui.TextUnformatted($"swap={_swapStep}  unequip={_unequipStep}");
        ImGui.TextUnformatted($"clicks: swap={_swapButtonClicks} unequip={_unequipButtonClicks}");
        ImGui.TextUnformatted($"build={BuildTag}");

        // Click environment state: whether the inventory is visible and the game window is focused.
        try
        {
            var gameController = GameController;
            var panel = gameController?.IngameState?.IngameUi?.InventoryPanel;
            var invVisible = panel is { IsValid: true, IsVisible: true };
            var foreground = gameController?.Window.IsForeground() ?? false;

            ImGui.TextUnformatted(
                $"diag: inv={invVisible} fg={foreground} overlayHidden={_overlayHidden} " +
                $"ws={_weaponSetToggled} retry={_weaponSetRetryWithoutToggle}");
        }
        catch
        {
            ImGui.TextUnformatted("diag: unavailable");
        }

        if (snapshot.IsEmpty)
            ImGui.TextUnformatted("Snapshot is empty (no equipped items with sockets).");
    }

    /// <summary>
    /// Socket table grouped by item: an item header row,
    /// then rows for its sockets. Sockets in the snapshot are already ordered by slot/socket,
    /// so groups are consecutive entries with the same (Slot, ItemAddress).
    /// </summary>
    private void DrawSocketsTable(GemSnapshot snapshot)
    {
        if (snapshot.IsEmpty)
            return;

        var compact = Settings.CompactMode.Value;
        var showExp = Settings.ShowExp.Value;
        var showEmpty = Settings.ShowEmptySockets.Value;

        // MAX color is taken from the overlay settings so the table and overlay match.
        var maxColor = ToVector4(Settings.OverlayMaxLevelColor.Value);

        // Compact mode hides "Color" and "Req"; ShowExp adds "Exp"; the last column is Swap.
        var columns = 6 + (compact ? 0 : 2) + (showExp ? 1 : 0) + 1;

        var available = ImGui.GetContentRegionAvail();
        if (available.Y < 32f)
            return;

        var flags = ImGuiTableFlags.Borders
                    | ImGuiTableFlags.RowBg
                    | ImGuiTableFlags.Resizable
                    | ImGuiTableFlags.SizingFixedFit
                    | ImGuiTableFlags.ScrollY;

        // ID depends on the column count: ImGui dislikes changing the column count of the same table.
        if (!ImGui.BeginTable($"gemhelper_table_{columns}", columns, flags, new Vector2(0, available.Y)))
            return;

        ImGui.TableSetupColumn("Slot");
        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 34);
        if (!compact)
            ImGui.TableSetupColumn("Color", ImGuiTableColumnFlags.WidthFixed, 46);
        ImGui.TableSetupColumn("Link", ImGuiTableColumnFlags.WidthFixed, 58);
        ImGui.TableSetupColumn("Gem");
        ImGui.TableSetupColumn("Lvl", ImGuiTableColumnFlags.WidthFixed, 40);
        ImGui.TableSetupColumn("Q", ImGuiTableColumnFlags.WidthFixed, 36);
        if (!compact)
            ImGui.TableSetupColumn("Req", ImGuiTableColumnFlags.WidthFixed, 44);
        if (showExp)
            ImGui.TableSetupColumn("Exp", ImGuiTableColumnFlags.WidthFixed, 84);
        ImGui.TableSetupColumn("Swap", ImGuiTableColumnFlags.WidthFixed, 54);

        ImGui.TableHeadersRow();

        var sockets = snapshot.Sockets;
        var start = 0;

        while (start < sockets.Count)
        {
            var first = sockets[start];

            // A group is a run of consecutive sockets of the same item.
            var end = start + 1;
            while (end < sockets.Count
                   && sockets[end].Slot == first.Slot
                   && sockets[end].ItemAddress == first.ItemAddress)
            {
                end++;
            }

            DrawItemHeaderRow(snapshot, start, end);

            for (var i = start; i < end; i++)
            {
                var socket = sockets[i];
                if (socket.IsEmpty && !showEmpty)
                    continue;

                DrawSocketRow(socket, compact, showExp, maxColor, snapshot);
            }

            start = end;
        }

        ImGui.EndTable();
    }

    /// <summary>
    /// Item header row: slot, name, class, socket count and the Unequip button.
    /// </summary>
    private void DrawItemHeaderRow(GemSnapshot snapshot, int start, int end)
    {
        var first = snapshot.Sockets[start];

        ImGui.TableNextRow();
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.ColorConvertFloat4ToU32(GroupHeaderBg));
        ImGui.TableSetColumnIndex(0);

        TextColoredSafe(GroupHeaderText, BuildItemHeader(first, end - start));

        // Item address goes into the tooltip to keep the row compact.
        if (first.ItemAddress != 0 && ImGui.IsItemHovered())
            ImGui.SetTooltip($"ItemAddress=0x{first.ItemAddress:X}");

        // Unequip-all-gems button next to the item name.
        ImGui.SameLine();

        if (_swapStep != SwapStep.None || _unequipStep != UnequipStep.None)
        {
            ImGui.TextDisabled("...");
            return;
        }

        var hasGems = false;
        for (var i = start; i < end; i++)
        {
            if (!snapshot.Sockets[i].IsEmpty)
            {
                hasGems = true;
                break;
            }
        }

        if (!hasGems)
        {
            ImGui.TextDisabled("—");
            return;
        }

        if (ImGui.SmallButton($"Unequip##{first.Slot}_{first.ItemAddress}"))
        {
            _unequipButtonClicks++;
            BeginUnequip(snapshot, start, end);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Unequip all gems from this item into the inventory");
    }

    private static string BuildItemHeader(GemSocketSnapshot first, int socketCount)
    {
        var name = string.IsNullOrEmpty(first.ItemName) ? "?" : first.ItemName;
        var className = string.IsNullOrEmpty(first.ItemClassName)
            ? string.Empty
            : $" [{first.ItemClassName}]";

        return $"{first.Slot} — {name}{className} — sockets={socketCount}";
    }

    /// <summary>Single socket row: color, link, gem, stats and the Swap button.</summary>
    private void DrawSocketRow(GemSocketSnapshot socket, bool compact, bool showExp, Vector4 maxColor, GemSnapshot snapshot)
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

        // Link: for the R1 fallback LinkGroup is unknown (-1) — show an orange marker.
        var isFallback = socket.LinkGroup < 0;
        var linkText = isFallback
            ? "?"
            : socket.LinkGroup.ToString(CultureInfo.InvariantCulture);

        if (compact)
            linkText = $"{socket.SocketColorCode}·{linkText}"; // keep the color visible when the column is hidden
        else if (isFallback)
            linkText = "? R1";

        ImGui.TableNextColumn();
        TextColoredSafe(isFallback ? TextFallback : plainColor, linkText);

        // Gem: an empty socket shows a dimmed "empty", otherwise the name in the socket color.
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
                if (socket.IsMaxLevel)
                    TextColoredSafe(maxColor, "MAX");
                else
                    TextColoredSafe(plainColor, socket.ExpPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%");

                // Raw values go into the tooltip so they do not take up a column.
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

        // Swap column: button to replace with an inventory gem (the action runs in Tick).
        ImGui.TableNextColumn();
        DrawSwapCell(socket, snapshot);
    }

    /// <summary>Text color by socket color (palette from the spec).</summary>
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

    /// <summary>SharpDX color (setting) → Vector4 for ImGui.</summary>
    private static Vector4 ToVector4(SharpDX.Color color)
        => new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

    /// <summary>
    /// The ImGui overload <c>TextColored(Vector4, string)</c> treats the string as a printf format,
    /// so "%" is escaped (gem names should not contain it, but this is a cheap safeguard).
    /// </summary>
    private static void TextColoredSafe(Vector4 color, string text)
        => ImGui.TextColored(color, text.Replace("%", "%%"));

    // ======================= Gem swap in a socket =======================

    /// <summary>Start a swap from the UI: only record the step; clicks happen in Tick.</summary>
    private void BeginSwap(GemSocketSnapshot socket, InventoryGemInfo target)
    {
        if (_swapStep != SwapStep.None || _unequipStep != UnequipStep.None)
        {
            SetOperationStatus("begin swap rejected (step/empty/max)");
            return;
        }

        if (socket.IsEmpty || target.IsMaxLevel)
        {
            SetOperationStatus("begin swap rejected (step/empty/max)");
            return;
        }

        // The native HUD overlay window (topmost) captures clicks — hide it during the operation.
        var controller = GameController;
        if (controller is not null)
            HideOverlayForInput(controller);

        // Try to activate the game once; on failure do not cancel the start:
        // the queue/step is set and the operation waits for focus in Tick.
        var focused = controller is not null && TryActivateGameWindow(controller);

        _swapSocket = socket;
        _swapTarget = target;
        _swapStartedUtc = DateTime.UtcNow;
        _swapStepAtUtc = DateTime.MinValue;

        _swapStep = SwapStep.RightClickSocket;

        SetOperationStatus(focused
            ? $"swap start {socket.Slot}[{socket.SocketIndex}] -> {target.Name} L{target.Level}"
            : "waiting for game focus");

        DebugWindow.LogMsg(
            $"[GemHelper] swap start: {socket.Slot}[{socket.SocketIndex}] -> {target.Name} L{target.Level}", 3f);
    }

    /// <summary>
    /// Swap state machine: right-click the socket → left-click the gem in the inventory → left-click the socket.
    /// Called from Tick only; any error or timeout resets the operation.
    /// </summary>
    private void ProcessSwap(GameController gameController)
    {
        if (_swapStep == SwapStep.None)
            return;

        var now = DateTime.UtcNow;

        if (now - _swapStartedUtc >= TimeSpan.FromMilliseconds(Settings.SwapTiming.TimeoutMs.Value))
        {
            FailSwap("timeout");
            return;
        }

        // Delay between steps so the game can process the previous click/cursor change.
        if (now - _swapStepAtUtc < TimeSpan.FromMilliseconds(Settings.SwapTiming.StepDelayMs.Value))
            return;

        SetOperationStatus($"swap step {_swapStep}");

        try
        {
            // Activate the game window before checking: after an ImGui click the foreground is the HUD window.
            // If focus is not obtained, do not reset the operation: wait until the user clicks into the game.
            TryActivateGameWindow(gameController);
            if (!gameController.Window.IsForeground())
            {
                SetOperationStatus("waiting for game focus");
                return;
            }

            // Clicks only while the inventory is open.
            var panel = gameController.IngameState?.IngameUi?.InventoryPanel;
            if (panel is null || !panel.IsValid || !panel.IsVisible)
            {
                FailSwap("inventory closed");
                return;
            }

            switch (_swapStep)
            {
                case SwapStep.RightClickSocket:
                {
                    var socket = _swapSocket;

                    // Second weapon set: sockets are not on screen until the set is active.
                    if (socket is not null && IsSecondWeaponSet(socket.Slot)
                        && !_weaponSetToggled && !_weaponSetRetryWithoutToggle)
                    {
                        if (!Settings.SwapTiming.SwapWeaponSetWithKey.Value || !PressWeaponSwapKey())
                        {
                            FailSwap("weapon swap key unavailable");
                            return;
                        }

                        _weaponSetToggled = true;
                        SetOperationStatus($"swap: weapon set switched ({socket.Slot})");
                        _swapStepAtUtc = now.AddMilliseconds(Settings.SwapTiming.WeaponSwapDelayMs.Value);
                        break;
                    }

                    if (socket is null
                        || !SocketHitTest.TryGetSocketCenter(gameController, socket, GetClickSlot(socket.Slot), out var center))
                    {
                        FailSwap("socket rect not found (right click)");
                        return;
                    }

                    DebugWindow.LogMsg($"[GemHelper] swap click socket at {center.X:0},{center.Y:0}", 3f);
                    _swapClickedCenter = center;

                    if (!ClickAt(gameController, center, MouseButtons.Right))
                    {
                        FailSwap("click failed (window focus)");
                        return;
                    }

                    Advance(SwapStep.WaitCursorOriginal);
                    break;
                }

                case SwapStep.WaitCursorOriginal:
                {
                    // The cursor must hold exactly the original gem from the socket.
                    var original = _swapSocket;
                    if (original is not null
                        && TryGetCursorGem(gameController, out var cursor)
                        && cursor is not null)
                    {
                        if (cursor.Level == original.GemLevel
                            && string.Equals(cursor.Name, original.GemName, StringComparison.Ordinal))
                        {
                            Advance(SwapStep.LeftClickInventory);
                        }
                        else if (_weaponSetToggled && !_weaponSetRetryWithoutToggle)
                        {
                            // The gem came from the other set: put it back into the socket and retry without switching.
                            ClickAt(gameController, _swapClickedCenter, MouseButtons.Left);
                            RestoreWeaponSetIfNeeded();
                            _weaponSetRetryWithoutToggle = true;
                            SetOperationStatus("swap: retry without weapon set swap");
                            Advance(SwapStep.RightClickSocket);
                        }
                        else
                        {
                            // Put the foreign gem back into the socket so it does not stay on the cursor.
                            ClickAt(gameController, _swapClickedCenter, MouseButtons.Left);
                            FailSwap($"unexpected cursor gem '{cursor.Name}' L{cursor.Level}");
                        }
                    }

                    break;
                }

                case SwapStep.LeftClickInventory:
                {
                    var target = _swapTarget;
                    if (target is null
                        || !SocketHitTest.TryGetInventoryItemCenter(gameController, target.ItemAddress, out var center))
                    {
                        FailSwap("target gem not found in inventory");
                        return;
                    }

                    DebugWindow.LogMsg($"[GemHelper] swap click inventory at {center.X:0},{center.Y:0}", 3f);

                    if (!ClickAt(gameController, center, MouseButtons.Left))
                    {
                        FailSwap("click failed (window focus)");
                        return;
                    }

                    Advance(SwapStep.WaitCursorTarget);
                    break;
                }

                case SwapStep.WaitCursorTarget:
                {
                    var target = _swapTarget;
                    if (target is null)
                    {
                        FailSwap("target lost");
                        return;
                    }

                    // The cursor must hold exactly the target gem.
                    if (TryGetCursorGem(gameController, out var cursor)
                        && cursor is not null
                        && cursor.Level == target.Level
                        && string.Equals(cursor.Name, target.Name, StringComparison.Ordinal))
                    {
                        Advance(SwapStep.LeftClickSocket);
                    }

                    break;
                }

                case SwapStep.LeftClickSocket:
                {
                    var socket = FindCurrentSocket(_swapSocket);
                    if (socket is null
                        || !SocketHitTest.TryGetSocketCenter(gameController, socket, GetClickSlot(socket.Slot), out var center))
                    {
                        FailSwap("socket not found (insert)");
                        return;
                    }

                    if (!ClickAt(gameController, center, MouseButtons.Left))
                    {
                        FailSwap("click failed (window focus)");
                        return;
                    }

                    Advance(SwapStep.WaitCursorEmpty);
                    break;
                }

                case SwapStep.WaitCursorEmpty:
                {
                    // Cursor is empty — the gem was inserted into the socket.
                    if (!TryGetCursorGem(gameController, out _))
                    {
                        SetOperationStatus("done");
                        ResetSwap();
                        break;
                    }

                    // The cursor may be stuck in memory — do not keep the overlay hidden until the global timeout.
                    if ((now - _swapStepAtUtc).TotalMilliseconds >= Settings.SwapTiming.CompletionTimeoutMs.Value)
                    {
                        SetOperationStatus("done (cursor check timeout)");
                        ResetSwap();
                    }

                    break;
                }
            }
        }
        catch (Exception ex)
        {
            FailSwap($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Advance to the next step with a timestamp (delay between steps).</summary>
    private void Advance(SwapStep next)
    {
        _swapStep = next;
        _swapStepAtUtc = DateTime.UtcNow;
    }

    private void ResetSwap()
    {
        RestoreWeaponSetIfNeeded();
        _weaponSetRetryWithoutToggle = false;

        _swapStep = SwapStep.None;
        _swapStepAtUtc = DateTime.MinValue;
        _swapStartedUtc = DateTime.MinValue;
        _swapSocket = null;
        _swapTarget = null;

        RestoreOverlay();
    }

    private void FailSwap(string reason)
    {
        SetOperationStatus($"FAIL: {reason}");

        try
        {
            DebugWindow.LogError($"[GemHelper] swap: {reason}");
        }
        catch
        {
            // Logging must not interfere with the reset.
        }

        ResetSwap();
    }

    /// <summary>
    /// Click at a point in game window coordinates (the calling step ensures game focus).
    /// The mouse moves smoothly (MouseMoveDurationMs/MouseMoveStepDelayMs settings), then clicks.
    /// </summary>
    private bool ClickAt(GameController gameController, Vector2 center, MouseButtons button)
    {
        var offset = gameController.Window.GetWindowRectangle().TopLeft;
        var targetX = (int)(center.X + offset.X);
        var targetY = (int)(center.Y + offset.Y);

        var duration = Math.Clamp(Settings.SwapTiming.MouseMoveDurationMs.Value, 0, 1000);
        if (duration > 0)
        {
            var stepDelay = Math.Clamp(Settings.SwapTiming.MouseMoveStepDelayMs.Value, 1, 50);
            var steps = Math.Max(1, duration / stepDelay);
            var start = System.Windows.Forms.Cursor.Position;

            for (var i = 1; i <= steps; i++)
            {
                var t = (float)i / steps;
                var x = start.X + (targetX - start.X) * t;
                var y = start.Y + (targetY - start.Y) * t;

                Input.SetCursorPos(new Vector2(x, y));
                Input.MouseMove();
                System.Threading.Thread.Sleep(stepDelay);
            }
        }
        else
        {
            // MouseMoveDurationMs = 0 — instant cursor jump.
            Input.SetCursorPos(new Vector2(targetX, targetY));
            Input.MouseMove();
        }

        // Pause so the game can process the hover (manual operation, 3-4 clicks).
        System.Threading.Thread.Sleep(35);

        var downUpDelay = Math.Clamp(Settings.SwapTiming.ClickDownUpDelayMs.Value, 0, 300);

        switch (button)
        {
            case MouseButtons.Right:
                Input.RightDown();
                System.Threading.Thread.Sleep(downUpDelay);
                Input.RightUp();
                break;

            default:
                Input.LeftDown();
                System.Threading.Thread.Sleep(downUpDelay);
                Input.LeftUp();
                break;
        }

        return true;
    }

    /// <summary>
    /// Makes the game window foreground: after an ImGui button click the HUD overlay becomes foreground,
    /// and <c>Input.mouse_event</c> sends clicks only to the foreground window.
    /// No aggressive tricks (showing/raising the window, Alt) — only SetForegroundWindow.
    /// </summary>
    private static bool TryActivateGameWindow(GameController gameController)
    {
        try
        {
            if (gameController.Window.IsForeground())
                return true;

            var handle = gameController.Window.Process?.MainWindowHandle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero)
                return false;

            SetForegroundWindow(handle);
            System.Threading.Thread.Sleep(80);
            return gameController.Window.IsForeground();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Gem on the cursor (InventorySlotE.Cursor1).</summary>
    private static bool TryGetCursorGem(GameController gameController, out InventoryGemInfo? gem)
    {
        gem = null;

        try
        {
            var inventories = gameController.IngameState?.ServerData?.PlayerInventories;
            if (inventories is null)
                return false;

            for (var i = 0; i < inventories.Count; i++)
            {
                var holder = inventories[i];
                if (holder?.TypeId != InventoryNameE.Cursor1)
                    continue;

                // The cursor may be exposed either as a flat Items list or as inventory slot items.
                var item = holder.Inventory?.Items?.FirstOrDefault()
                           ?? holder.Inventory?.InventorySlotItems?.FirstOrDefault()?.Item;

                if (item is null || item.Address == 0 || !item.IsValid)
                    return false;

                if (!item.TryGetComponent<SkillGem>(out var skillGem) || skillGem is null)
                    return false;

                if (!item.TryGetComponent<Base>(out var @base) || @base is null)
                    return false;

                var level = skillGem.Level;

                var maxLevel = 0;
                try
                {
                    maxLevel = skillGem.MaxLevel;
                }
                catch
                {
                    // Without MaxLevel fall back to the usual cap.
                }

                gem = new InventoryGemInfo(
                    @base.Name ?? string.Empty,
                    level,
                    maxLevel,
                    maxLevel > 0 ? level >= maxLevel : level >= 20,
                    item.Address,
                    0);

                return true;
            }
        }
        catch
        {
            // Cursor read failed — just "unknown".
        }

        return false;
    }

    /// <summary>
    /// Current socket entry from the latest snapshot (by slot and index).
    /// Socket geometry does not depend on the gem change, so the original is used when no entry is found.
    /// </summary>
    private GemSocketSnapshot? FindCurrentSocket(GemSocketSnapshot? original)
    {
        if (original is null)
            return null;

        var sockets = _snapshot.Sockets;

        for (var i = 0; i < sockets.Count; i++)
        {
            var socket = sockets[i];
            if (socket.Slot == original.Slot && socket.SocketIndex == original.SocketIndex)
                return socket;
        }

        return original;
    }

    /// <summary>
    /// Swap candidate: any inventory gem below max level; the highest-level one is chosen.
    /// Socket name and color no longer restrict the choice (the game validates the socket color).
    /// </summary>
    private static InventoryGemInfo? FindSwapCandidate(GemSnapshot snapshot, GemSocketSnapshot socket)
    {
        InventoryGemInfo? best = null;

        var gems = snapshot.InventoryGems;
        for (var i = 0; i < gems.Count; i++)
        {
            var gem = gems[i];

            if (gem.IsMaxLevel)
                continue;

            if (best is null || gem.Level > best.Level)
                best = gem;
        }

        return best;
    }

    /// <summary>Start unequipping all gems of an item: only fill the queue; clicks happen in Tick.</summary>
    private void BeginUnequip(GemSnapshot snapshot, int start, int end)
    {
        if (_swapStep != SwapStep.None || _unequipStep != UnequipStep.None)
        {
            SetOperationStatus("begin unequip rejected");
            return;
        }

        _unequipQueue.Clear();

        for (var i = start; i < end; i++)
        {
            var socket = snapshot.Sockets[i];
            if (!socket.IsEmpty)
                _unequipQueue.Add(socket);
        }

        if (_unequipQueue.Count == 0)
        {
            SetOperationStatus("begin unequip rejected");
            return;
        }

        // The native HUD overlay window (topmost) captures clicks — hide it during the operation.
        var controller = GameController;
        if (controller is not null)
            HideOverlayForInput(controller);

        // Try to activate the game once; on failure do not cancel the start:
        // the queue/step is set and the operation waits for focus in Tick.
        var focused = controller is not null && TryActivateGameWindow(controller);

        _unequipIndex = 0;
        _unequipStartedUtc = DateTime.UtcNow;
        _unequipStepAtUtc = DateTime.MinValue;
        _unequipStep = UnequipStep.RightClickSocket;

        SetOperationStatus(focused
            ? $"unequip start {_unequipQueue[0].Slot} ({_unequipQueue.Count})"
            : "waiting for game focus");

        DebugWindow.LogMsg(
            $"[GemHelper] unequip start: {_unequipQueue[0].Slot} ({_unequipQueue.Count} gems)", 3f);
    }

    /// <summary>
    /// Unequip state machine: right-click the socket → wait for the gem on the cursor → left-click an empty cell.
    /// One gem at a time, with a per-gem timeout. Swap and unequip never run at the same time.
    /// </summary>
    private void ProcessUnequip(GameController gameController)
    {
        if (_unequipStep == UnequipStep.None)
            return;

        var now = DateTime.UtcNow;

        // Per-gem timeout (reset when moving to the next socket).
        if (now - _unequipStartedUtc >= TimeSpan.FromMilliseconds(Settings.SwapTiming.TimeoutMs.Value))
        {
            FailUnequip("timeout");
            return;
        }

        if (now - _unequipStepAtUtc < TimeSpan.FromMilliseconds(Settings.SwapTiming.StepDelayMs.Value))
            return;

        SetOperationStatus($"unequip step {_unequipStep} [{_unequipIndex}/{_unequipQueue.Count}]");

        try
        {
            // Activate the game window before checking: after an ImGui click the foreground is the HUD window.
            // If focus is not obtained, do not reset the operation: wait until the user clicks into the game.
            TryActivateGameWindow(gameController);
            if (!gameController.Window.IsForeground())
            {
                SetOperationStatus("waiting for game focus");
                return;
            }

            var panel = gameController.IngameState?.IngameUi?.InventoryPanel;
            if (panel is null || !panel.IsValid || !panel.IsVisible)
            {
                FailUnequip("inventory closed");
                return;
            }

            if (_unequipIndex < 0 || _unequipIndex >= _unequipQueue.Count)
            {
                ResetUnequip();
                return;
            }

            var socket = _unequipQueue[_unequipIndex];

            switch (_unequipStep)
            {
                case UnequipStep.RightClickSocket:
                {
                    // Second weapon set: sockets are not on screen until the set is active.
                    if (IsSecondWeaponSet(socket.Slot) && !_weaponSetToggled && !_weaponSetRetryWithoutToggle)
                    {
                        if (!Settings.SwapTiming.SwapWeaponSetWithKey.Value || !PressWeaponSwapKey())
                        {
                            FailUnequip("weapon swap key unavailable");
                            return;
                        }

                        _weaponSetToggled = true;
                        SetOperationStatus($"unequip: weapon set switched ({socket.Slot})");
                        _unequipStepAtUtc = now.AddMilliseconds(Settings.SwapTiming.WeaponSwapDelayMs.Value);
                        break;
                    }

                    if (!SocketHitTest.TryGetSocketCenter(gameController, socket, GetClickSlot(socket.Slot), out var center))
                    {
                        FailUnequip("socket rect not found (right click)");
                        return;
                    }

                    _unequipClickedCenter = center;

                    DebugWindow.LogMsg($"[GemHelper] unequip click socket at {center.X:0},{center.Y:0}", 3f);

                    if (!ClickAt(gameController, center, MouseButtons.Right))
                    {
                        FailUnequip("click failed (window focus)");
                        return;
                    }

                    AdvanceUnequip(UnequipStep.WaitCursorGem);
                    break;
                }

                case UnequipStep.WaitCursorGem:
                {
                    // Wait until the cursor holds exactly the gem from this socket.
                    if (TryGetCursorGem(gameController, out var cursor) && cursor is not null)
                    {
                        var nameMatches = string.IsNullOrEmpty(socket.GemName)
                                          || string.Equals(cursor.Name, socket.GemName, StringComparison.Ordinal);

                        if (nameMatches && cursor.Level == socket.GemLevel)
                        {
                            AdvanceUnequip(UnequipStep.ClickEmptyCell);
                            break;
                        }

                        // Foreign gem on the cursor: the game was probably already on the second set and was switched needlessly.
                        // Put the gem back into the same socket and retry without switching the set.
                        if (_weaponSetToggled
                            && !_weaponSetRetryWithoutToggle
                            && (now - _unequipStepAtUtc).TotalMilliseconds >= Settings.SwapTiming.CompletionTimeoutMs.Value)
                        {
                            ClickAt(gameController, _unequipClickedCenter, MouseButtons.Left);
                            RestoreWeaponSetIfNeeded();
                            _weaponSetRetryWithoutToggle = true;
                            SetOperationStatus("unequip: retry without weapon set swap");
                            AdvanceUnequip(UnequipStep.RightClickSocket);
                            break;
                        }

                        // If the gem still does not match, do not hold the operation until the global timeout.
                        // Before failing, put the foreign gem back into the socket so it does not stay on the cursor.
                        if ((now - _unequipStepAtUtc).TotalMilliseconds >= Settings.SwapTiming.CompletionTimeoutMs.Value)
                        {
                            ClickAt(gameController, _unequipClickedCenter, MouseButtons.Left);
                            FailUnequip($"unexpected cursor gem '{cursor.Name}'");
                        }
                    }

                    break;
                }

                case UnequipStep.ClickEmptyCell:
                {
                    if (!InventoryHitTest.TryGetEmptyCellCenter(gameController, out var center))
                    {
                        FailUnequip("inventory full");
                        return;
                    }

                    DebugWindow.LogMsg($"[GemHelper] unequip click empty cell at {center.X:0},{center.Y:0}", 3f);

                    if (!ClickAt(gameController, center, MouseButtons.Left))
                    {
                        FailUnequip("click failed (window focus)");
                        return;
                    }

                    AdvanceUnequip(UnequipStep.WaitCursorEmpty);
                    break;
                }

                case UnequipStep.WaitCursorEmpty:
                {
                    if (TryGetCursorGem(gameController, out _))
                    {
                        // The cursor may be stuck in memory — do not wait for the global timeout.
                        if ((now - _unequipStepAtUtc).TotalMilliseconds >= Settings.SwapTiming.CompletionTimeoutMs.Value)
                            FailUnequip("cursor not empty");

                        break;
                    }

                    // Gem dropped — move to the next socket.
                    _unequipIndex++;

                    if (_unequipIndex >= _unequipQueue.Count)
                    {
                        SetOperationStatus("done");
                        ResetUnequip();
                        return;
                    }

                    _unequipStartedUtc = DateTime.UtcNow;
                    AdvanceUnequip(UnequipStep.RightClickSocket);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            FailUnequip($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Advance to the next unequip step with a timestamp.</summary>
    private void AdvanceUnequip(UnequipStep next)
    {
        _unequipStep = next;
        _unequipStepAtUtc = DateTime.UtcNow;
    }

    private void ResetUnequip()
    {
        RestoreWeaponSetIfNeeded();
        _weaponSetRetryWithoutToggle = false;

        _unequipStep = UnequipStep.None;
        _unequipStepAtUtc = DateTime.MinValue;
        _unequipStartedUtc = DateTime.MinValue;
        _unequipIndex = 0;
        _unequipQueue.Clear();

        RestoreOverlay();
    }

    private void FailUnequip(string reason)
    {
        SetOperationStatus($"FAIL: {reason}");

        try
        {
            DebugWindow.LogError($"[GemHelper] unequip: {reason}");
        }
        catch
        {
            // Logging must not interfere with the reset.
        }

        ResetUnequip();
    }

    /// <summary>Swap column cell: a button with a tooltip, or a dash.</summary>
    private void DrawSwapCell(GemSocketSnapshot socket, GemSnapshot snapshot)
    {
        if (socket.IsEmpty)
        {
            ImGui.TextDisabled("—");
            return;
        }

        // No button while a swap or unequip is running.
        if (_swapStep != SwapStep.None || _unequipStep != UnequipStep.None)
        {
            ImGui.TextDisabled("...");
            return;
        }

        var candidate = FindSwapCandidate(snapshot, socket);
        if (candidate is null)
        {
            ImGui.TextDisabled("—");
            return;
        }

        if (ImGui.SmallButton($"Swap##{socket.Slot}_{socket.SocketIndex}"))
        {
            _swapButtonClicks++;
            BeginSwap(socket, candidate);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Swap with {candidate.Name} L{candidate.Level}");
    }

    public override void EntityAdded(Entity entity)
    {
        // M3: no per-entity processing needed — the snapshot is read from PlayerInventories by counter/timer.
    }
}
