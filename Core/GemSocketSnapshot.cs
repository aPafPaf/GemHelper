using ExileCore.Shared.Enums;
using SocketColorEnum = ExileCore.Shared.Enums.SocketColor;

namespace GemHelper.Core;

/// <summary>
/// Immutable record of a single socket of an equipped item.
/// Created in <see cref="GemSnapshotReader"/> and only read during rendering.
/// </summary>
public sealed record GemSocketSnapshot(
    InventorySlotE Slot,
    string ItemName,
    string ItemClassName,
    long ItemAddress,
    int SocketIndex,
    SocketColor SocketColor,
    int LinkGroup,
    bool IsEmpty,
    string GemName,
    int GemLevel,
    int GemQuality,
    int RequiredLevel,
    uint TotalExp,
    long GemAddress,
    float ExpPercent = 0f,
    bool IsMaxLevel = false,
    uint ExpPrev = 0,
    uint ExpMax = 0,
    int ItemSizeX = 0,
    int ItemSizeY = 0,
    int InventoryColumns = 0,
    int InventoryRows = 0,
    InventoryTypeE InventoryType = InventoryTypeE.MainInventory,
    int TotalSockets = 0,
    int MaxLevel = 0,
    uint ExpToNext = 0)
{
    /// <summary>Short socket color code for debug output: R/G/B/W/A/S.</summary>
    public string SocketColorCode => SocketColor switch
    {
        SocketColorEnum.Red => "R",
        SocketColorEnum.Green => "G",
        SocketColorEnum.Blue => "B",
        SocketColorEnum.White => "W",
        SocketColorEnum.Abyssal => "A",
        SocketColorEnum.Resonator => "S",
        _ => "?",
    };

    /// <summary>
    /// Ready-to-use string for the debug panel, e.g.
    /// <c>Helm1 [0] R link=0 — Fireball L20 Q20 (req 70)</c> or
    /// <c>Helm1 [1] G link=0 — empty</c>.
    /// </summary>
    public string DisplayName => IsEmpty
        ? $"{Slot} [{SocketIndex}] {SocketColorCode} link={LinkGroup} — empty"
        : $"{Slot} [{SocketIndex}] {SocketColorCode} link={LinkGroup} — {GemName} L{GemLevel} Q{GemQuality} (req {RequiredLevel})";
}
