namespace GemHelper.Core;

/// <summary>
/// Gem stored in the main inventory (candidate for swapping into an equipped socket).
/// The snapshot is built in Tick, the UI only reads it.
/// </summary>
public sealed record InventoryGemInfo(
    string Name,
    int Level,
    int MaxLevel,
    bool IsMaxLevel,
    long ItemAddress,
    int Quality);
