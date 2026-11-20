using ExileCore.Shared.Enums;
using SocketColorEnum = ExileCore.Shared.Enums.SocketColor;

namespace GemHelper.Core;

/// <summary>
/// Неизменяемая запись об одном сокете надетого предмета.
/// Создаётся в <see cref="GemSnapshotReader"/> и только читается в рендере.
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
    /// <summary>Короткий код цвета сокета для отладочного вывода: R/G/B/W/A/S.</summary>
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
    /// Готовая строка для отладочной панели, например
    /// <c>Helm1 [0] R link=0 — Fireball L20 Q20 (req 70)</c> или
    /// <c>Helm1 [1] G link=0 — empty</c>.
    /// </summary>
    public string DisplayName => IsEmpty
        ? $"{Slot} [{SocketIndex}] {SocketColorCode} link={LinkGroup} — empty"
        : $"{Slot} [{SocketIndex}] {SocketColorCode} link={LinkGroup} — {GemName} L{GemLevel} Q{GemQuality} (req {RequiredLevel})";
}
