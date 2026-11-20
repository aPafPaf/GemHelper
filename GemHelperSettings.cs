using ExileCore.Shared.Attributes;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using System.Windows.Forms; // Keys — for HotkeyNode

namespace GemHelper;

public class GemHelperSettings : ISettings
{
    /// <summary>
    /// Plugin master switch. When false the HUD does not call Tick/Render.
    /// Disabled by default so the plugin never runs accidentally.
    /// </summary>
    public ToggleNode Enable { get; set; } = new ToggleNode(false);

    /// <summary>
    /// "GemHelper" debug ImGui panel: socket table of equipped items plus read statistics.
    /// </summary>
    public ToggleNode ShowDebugSnapshot { get; set; } = new ToggleNode(true);

    /// <summary>
    /// Show empty sockets as table rows. The item header is always drawn,
    /// even when all of its sockets are empty and hidden.
    /// </summary>
    public ToggleNode ShowEmptySockets { get; set; } = new ToggleNode(true);

    /// <summary>Show the Exp column (gem progress to the next level, % / MAX).</summary>
    public ToggleNode ShowExp { get; set; } = new ToggleNode(true);

    /// <summary>
    /// Compact mode: hide the color and Req columns. The color code is still shown
    /// in the Link column (e.g. "R·0"), so no color information is lost.
    /// </summary>
    public ToggleNode CompactMode { get; set; } = new ToggleNode(false);

    /// <summary>
    /// Inventory panel overlay: frame around each equipped item and a gem list next to it.
    /// Disabled by default.
    /// </summary>
    public ToggleNode ShowOverlay { get; set; } = new ToggleNode(false);

    /// <summary>Show gem names with level/quality in the overlay.</summary>
    public ToggleNode OverlayShowGemNames { get; set; } = new ToggleNode(true);

    /// <summary>
    /// Draw only items that contain gems. Disabled by default: frames and sockets are drawn
    /// for every detected item, even empty ones.
    /// </summary>
    public ToggleNode OverlayOnlyWithGems { get; set; } = new ToggleNode(false);

    /// <summary>Append gem progress (%/MAX) to the gem line in the overlay.</summary>
    public ToggleNode OverlayShowExp { get; set; } = new ToggleNode(true);

    /// <summary>Draw frames around individual sockets of an equipped item (M4b).</summary>
    public ToggleNode OverlaySocketFrames { get; set; } = new ToggleNode(true);

    /// <summary>Draw link lines between linked sockets (M4b).</summary>
    public ToggleNode OverlayLinkLines { get; set; } = new ToggleNode(true);

    /// <summary>
    /// Diagnostics: label detected slots with their Slot name at the top-left corner of the frame.
    /// Helps to see in game which slots are recognized.
    /// </summary>
    public ToggleNode OverlayDebugSlots { get; set; } = new ToggleNode(false);

    /// <summary>X offset of the gem label block, pixels (-900..900). Helps separate overlapping text.</summary>
    public RangeNode<int> OverlayTextOffsetX { get; set; } = new(0, -900, 900);

    /// <summary>Y offset of the gem label block, pixels (-900..900).</summary>
    public RangeNode<int> OverlayTextOffsetY { get; set; } = new(0, -900, 900);

    /// <summary>Draw thin lines from each gem text row to its socket.</summary>
    public ToggleNode OverlayTextToSocketLines { get; set; } = new(true);

    /// <summary>Highlight sockets whose gem reached max level (outer frame).</summary>
    public ToggleNode OverlayHighlightMaxLevelGems { get; set; } = new(true);

    /// <summary>Highlight color for max level gems (gold by default).</summary>
    public ColorNode OverlayMaxLevelColor { get; set; } = new(new SharpDX.Color(255, 215, 0));

    /// <summary>Thickness of the outer MAX socket frame, pixels (1..10).</summary>
    public RangeNode<int> OverlayMaxLevelThickness { get; set; } = new(2, 1, 10);

    /// <summary>
    /// Recolor the whole MAX gem text with <see cref="OverlayMaxLevelColor"/>.
    /// false — normal text color, only the MAX word is colored.
    /// </summary>
    public ToggleNode OverlayMaxLevelRecolorWholeText { get; set; } = new(false);

    /// <summary>Use the socket color for gem text (otherwise <see cref="OverlayGemTextColor"/>).</summary>
    public ToggleNode OverlayUseSocketColorForText { get; set; } = new(true);

    /// <summary>Gem text color used when <see cref="OverlayUseSocketColorForText"/> is off.</summary>
    public ColorNode OverlayGemTextColor { get; set; } = new(new SharpDX.Color(255, 255, 255));

    /// <summary>
    /// Color of the "text → socket" lines. Transparent (alpha = 0) = use the socket color;
    /// any opaque value is applied as is.
    /// </summary>
    public ColorNode OverlayGemLineColor { get; set; } = new(new SharpDX.Color(0, 0, 0, 0));

    /// <summary>Thickness of the "text line → socket" line, pixels (1..10).</summary>
    public RangeNode<int> OverlayGemLineThickness { get; set; } = new(1, 1, 10);

    /// <summary>Color of link lines between sockets (not the lines to the text).</summary>
    public ColorNode OverlayLinkLineColor { get; set; } = new(new SharpDX.Color(255, 255, 0));

    /// <summary>Thickness of link lines between sockets, pixels (1..10).</summary>
    public RangeNode<int> OverlayLinkLineThickness { get; set; } = new(2, 1, 10);

    /// <summary>
    /// Per equipment slot offsets of the label block.
    /// Added to the global <see cref="OverlayTextOffsetX"/> / <see cref="OverlayTextOffsetY"/>.
    /// </summary>
    public SlotTextOffsets TextOffsets { get; set; } = new();

    /// <summary>Text offsets per equipment slot (pixels, -300..300).</summary>
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

        /// <summary>Offset for a specific slot (unknown slot — same as Helm1).</summary>
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

    /// <summary>X/Y offset pair in pixels for a single slot.</summary>
    [Submenu(CollapsedByDefault = true)]
    public class OffsetXY
    {
        /// <summary>Show gem text for this slot (otherwise frames/sockets only).</summary>
        public ToggleNode Show { get; set; } = new(true);

        public RangeNode<int> X { get; set; } = new(0, -900, 900);
        public RangeNode<int> Y { get; set; } = new(0, -900, 900);
    }

    /// <summary>
    /// Snapshot rebuild interval in milliseconds (50..2000, 250 by default).
    /// The snapshot is built in Tick only, so this value affects only the memory read rate.
    /// </summary>
    public RangeNode<int> UpdateIntervalMs { get; set; } = new RangeNode<int>(250, 50, 2000);

    /// <summary>
    /// Forced full snapshot read interval, ms (500..10000, 2000 by default).
    /// Between full refreshes the snapshot is rebuilt only when ServerRequestCounter changes,
    /// which prevents stale data when changes do not bump the counter.
    /// </summary>
    public RangeNode<int> FullRefreshIntervalMs { get; set; } = new RangeNode<int>(2000, 500, 10000);

    // ==================== Swap / Unequip timings ====================

    /// <summary>
    /// Timings and mouse behavior for Swap/Unequip (step delays, timeouts, hover speed, HUD hiding).
    /// </summary>
    public SwapTimingSettings SwapTiming { get; set; } = new();

    /// <summary>
    /// Hotkey to show/hide the socket table window.
    /// Unassigned by default (Keys.None) so nothing is intercepted.
    /// </summary>
#pragma warning disable CS0618 // HotkeyNode is [Obsolete] in 330.5, but it works and other HUD plugins use it
    public HotkeyNode ToggleWindowHotkey { get; set; } = new(Keys.None);

    /// <summary>
    /// Hotkey to toggle the inventory panel overlay.
    /// Unassigned by default (Keys.None).
    /// </summary>
    public HotkeyNode ToggleOverlayHotkey { get; set; } = new(Keys.None);
#pragma warning restore CS0618

    /// <summary>
    /// "Dump snapshot" button: opens the current GemSnapshot in the HUD inspector
    /// (GameController.InspectObject) for diagnostics.
    /// </summary>
    public ButtonNode DumpSnapshotButton { get; set; } = new();
}

/// <summary>
/// Timings and mouse behavior for Swap/Unequip operations.
/// Kept in a submenu so the settings root stays clean.
/// </summary>
[Submenu(CollapsedByDefault = false)]
public class SwapTimingSettings
{
    /// <summary>
    /// Delay between Swap/Unequip steps, ms (1..100, 10 by default).
    /// Gives the game time to process the previous click and cursor change.
    /// </summary>
    public RangeNode<int> StepDelayMs { get; set; } = new(10, 1, 100);

    /// <summary>
    /// Total timeout of one Swap/Unequip operation, ms (1000..15000, 2000 by default).
    /// Includes waiting for game focus, so do not set it too low.
    /// </summary>
    public RangeNode<int> TimeoutMs { get; set; } = new(2_000, 1_000, 15_000);

    /// <summary>
    /// Completion confirmation timeout (cursor must become empty), ms (100..2000, 400 by default).
    /// The cursor inventory in memory sometimes sticks; without this limit the operation hangs until the total timeout.
    /// </summary>
    public RangeNode<int> CompletionTimeoutMs { get; set; } = new(400, 100, 2_000);

    /// <summary>
    /// Duration of the smooth mouse movement to the target, ms (0..300, 50 by default).
    /// 0 — instant cursor jump.
    /// </summary>
    public RangeNode<int> MouseMoveDurationMs { get; set; } = new(50, 0, 300);

    /// <summary>
    /// Delay between steps of the smooth mouse movement, ms (1..50, 10 by default).
    /// Lower value — more steps and smoother movement (for the same duration).
    /// </summary>
    public RangeNode<int> MouseMoveStepDelayMs { get; set; } = new(10, 1, 50);

    /// <summary>
    /// Delay between mouse button down and up, ms (0..300, 35 by default).
    /// If too small, the game may not process the click.
    /// </summary>
    public RangeNode<int> ClickDownUpDelayMs { get; set; } = new(35, 0, 300);

    /// <summary>
    /// Hide the native HUD overlay window ("ExileApi") during Swap/Unequip
    /// so Input clicks reach the game. The window is restored after the operation.
    /// </summary>
    public ToggleNode HideOverlayDuringOperations { get; set; } = new(true);

    /// <summary>
    /// The second weapon set (Weapon2/Offhand2) cannot be removed by clicks while the set is inactive:
    /// the plugin presses the weapon set swap key itself and switches it back after the operation.
    /// </summary>
    public ToggleNode SwapWeaponSetWithKey { get; set; } = new(true);

    /// <summary>In-game weapon set swap key (X by default).</summary>
    public HotkeyNodeV2 WeaponSwapKey { get; set; } = Keys.X;

    /// <summary>Delay after pressing the weapon set swap key, ms (50..2000, 250 by default).</summary>
    public RangeNode<int> WeaponSwapDelayMs { get; set; } = new(250, 50, 2000);
}
