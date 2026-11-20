using System.Runtime.InteropServices;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Interfaces;
using GameOffsets;

namespace GemHelper.Core;

/// <summary>
/// Reads gems from sockets of equipped items.
/// All game memory access is guarded: a failure of one slot/gem must never break Tick.
/// </summary>
public static class GemSnapshotReader
{
    /// <summary>Equipment slots whose sockets we care about (including the second weapon set).</summary>
    private static readonly InventorySlotE[] EquippedSlots =
    {
        InventorySlotE.Helm1,
        InventorySlotE.BodyArmour1,
        InventorySlotE.Gloves1,
        InventorySlotE.Boots1,
        InventorySlotE.Belt1,
        InventorySlotE.Amulet1,
        InventorySlotE.Ring1,
        InventorySlotE.Ring2,
        InventorySlotE.Weapon1,
        InventorySlotE.Offhand1,
        InventorySlotE.Weapon2,
        InventorySlotE.Offhand2,
    };

    // Message deduplication: memory is read every tick, so the log must not spam.
    private const int MaxLoggedMessages = 64;
    private static readonly HashSet<string> LoggedMessages = new(StringComparer.Ordinal);
    private static readonly object LogLock = new();

    // Cumulative experience of a normal gem (max level 20) per level: index = level - 1.
    private static readonly uint[] GemCumulativeExp =
    {
        0, 70, 308, 927, 2280, 4908, 9590, 17564, 30536, 51012,
        82746, 131368, 205048, 315864, 481484, 727896, 1094904, 1639248, 2447448, 3644748,
    };

    // Scan range for the (prev, max) pair during offset self-calibration.
    private const int ExpScanStart = 0x18;
    private const int ExpScanEnd = 0x60;
    private const int ExpScanStep = 4;

    // Current offsets of the exp / next-level-exp pair. Fields may have shifted in 330.5,
    // so they are not readonly: on the first suitable gem the pair is calibrated against the exp table.
    private static int _expPrevOffset = TryGetFieldOffset(nameof(SkillGemOffsets.ExperiencePrevLevel));
    private static int _expMaxOffset = TryGetFieldOffset(nameof(SkillGemOffsets.ExperienceMaxLevel));
    private static bool _expOffsetsCalibrated;

    private static int TryGetFieldOffset(string fieldName)
    {
        try
        {
            return (int)Marshal.OffsetOf(typeof(SkillGemOffsets), fieldName);
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>The (prev, max) pair is consistent with the gem total experience.</summary>
    private static bool IsValidExpPair(uint prev, uint max, uint totalExp)
        => max > prev && prev <= totalExp && totalExp <= max;

    /// <summary>Progress percentage to the next level; clamped to 0..100.</summary>
    private static float CalcExpPercent(uint totalExp, uint prev, uint max)
    {
        if (max <= prev)
            return 0f;

        var current = totalExp < prev ? prev : totalExp;
        return (float)Math.Clamp((current - prev) * 100.0 / (max - prev), 0d, 100d);
    }

    /// <summary>Value deviates from the table value by no more than ~1%.</summary>
    private static bool IsCloseToTable(uint value, uint expected)
    {
        var diff = value >= expected ? (long)value - expected : (long)expected - value;
        return diff <= expected / 100 + 1;
    }

    /// <summary>
    /// One-time self-calibration of the (prev, max) pair: look for two consecutive uints in SkillGem
    /// matching the table boundaries of the current level. Exact match first, then ±1%.
    /// </summary>
    private static bool TryCalibrateExpOffsets(
        IMemory memory,
        long address,
        int gemLevel,
        int maxLevel,
        out uint prev,
        out uint max)
    {
        prev = 0;
        max = 0;

        if (_expOffsetsCalibrated)
            return false;

        // Calibrate only on normal gems and "middle" levels (boundaries at 1 and 20 are trivial).
        if (gemLevel < 2 || gemLevel > 19 || maxLevel != 20)
            return false;

        var expectedPrev = GemCumulativeExp[gemLevel - 1];
        var expectedMax = GemCumulativeExp[gemLevel];

        try
        {
            var tolerantOffset = -1;
            uint tolerantPrev = 0;
            uint tolerantMax = 0;

            for (var offset = ExpScanStart; offset <= ExpScanEnd; offset += ExpScanStep)
            {
                uint first;
                uint second;

                try
                {
                    first = memory.Read<uint>(address + offset);
                    second = memory.Read<uint>(address + offset + 4);
                }
                catch
                {
                    continue;
                }

                // Exact match with the table is the best case.
                if (first == expectedPrev && second == expectedMax)
                {
                    CommitExpOffsets(offset, first, second, out prev, out max);
                    return true;
                }

                if (tolerantOffset < 0 && IsCloseToTable(first, expectedPrev) && IsCloseToTable(second, expectedMax))
                {
                    tolerantOffset = offset;
                    tolerantPrev = first;
                    tolerantMax = second;
                }
            }

            if (tolerantOffset >= 0)
            {
                CommitExpOffsets(tolerantOffset, tolerantPrev, tolerantMax, out prev, out max);
                return true;
            }
        }
        catch
        {
            // The scan must not break Capture.
        }

        return false;
    }

    private static void CommitExpOffsets(int offset, uint prev, uint max, out uint resultPrev, out uint resultMax)
    {
        _expPrevOffset = offset;
        _expMaxOffset = offset + 4;
        _expOffsetsCalibrated = true;

        resultPrev = prev;
        resultMax = max;

        // Once per process: useful for diagnostics, does not spam.
        LogOnce($"exp offsets calibrated: prev=0x{offset:X}, max=0x{offset + 4:X}");
    }

    /// <summary>
    /// Fallback via <c>SkillGem.ExperienceToNextLevel</c>: the property may mean either "remaining"
    /// or "required for the level", and may be based on a bugged ExperiencePrevLevel.
    /// </summary>
    private static bool TryCalcPercentFromExpToNext(
        int gemLevel,
        int maxLevel,
        uint totalExp,
        uint expToNext,
        out float percent)
    {
        percent = 0f;

        if (expToNext == 0)
            return false;

        if (gemLevel < 1 || gemLevel > 19)
            return false;

        if (maxLevel != 20 && maxLevel != 0)
            return false;

        var tablePrev = GemCumulativeExp[gemLevel - 1];
        var tableMax = GemCumulativeExp[gemLevel];
        var required = tableMax - tablePrev;

        if (required == 0)
            return false;

        // Experience already gained within the current level (0..required).
        var inLevel = totalExp > tablePrev ? Math.Min(totalExp - tablePrev, required) : 0u;

        // "Remaining experience" candidate: if the property really means the remainder, it should
        // be close to this value.
        var remainingCandidate = required - inLevel;

        // Distances to the two hypotheses — pick the closer one. This way a value equal to required
        // (the fixed "how much is needed for the level" property) no longer yields 0%.
        var distFixed = expToNext >= required
            ? (long)expToNext - required
            : (long)required - expToNext;

        var distRemaining = expToNext >= remainingCandidate
            ? (long)expToNext - remainingCandidate
            : (long)remainingCandidate - expToNext;

        if (distRemaining < distFixed)
        {
            // Property = "remaining experience" (not greater than required, otherwise unreachable).
            var remaining = expToNext < required ? required - expToNext : 0u;

            percent = (float)Math.Clamp(remaining * 100.0 / required, 0d, 100d);
            return true;
        }

        // Property = "experience needed for the level" (or ambiguous at 0% progress).
        percent = (float)Math.Clamp(inLevel * 100.0 / required, 0d, 100d);
        return true;
    }

    /// <summary>Equipped item together with inventory geometry (needed by M4b for per-socket frames).</summary>
    private readonly record struct EquippedItemInfo(
        Entity Item,
        int SizeX,
        int SizeY,
        int Columns,
        int Rows,
        InventoryTypeE InventoryType);

    /// <summary>
    /// Builds a socket snapshot of all equipped items. Never throws outwards.
    /// </summary>
    public static GemSnapshot Capture(GameController gameController)
    {
        if (gameController is null)
            return GemSnapshot.Empty;

        IList<InventoryHolder>? inventories;
        try
        {
            inventories = gameController.IngameState?.ServerData?.PlayerInventories;
        }
        catch (Exception ex)
        {
            LogOnce($"PlayerInventories: {ex.GetType().Name}: {ex.Message}");
            return GemSnapshot.Empty;
        }

        if (inventories is null)
            return GemSnapshot.Empty;

        // Memory is needed to read ExperiencePrevLevel/ExperienceMaxLevel from SkillGemOffsets.
        IMemory? memory = null;
        try
        {
            memory = gameController.Memory;
        }
        catch
        {
            // Without memory the exp progress is 0 — not critical.
        }

        var sockets = new List<GemSocketSnapshot>(24);

        foreach (var slot in EquippedSlots)
        {
            try
            {
                var holder = FindInventory(inventories, slot);
                if (holder is null)
                    continue;

                if (!GetEquippedItem(holder.Inventory, out var info))
                    continue;

                AppendSockets(info, slot, sockets, memory);
            }
            catch (Exception ex)
            {
                LogOnce($"slot {slot}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        var inventoryGems = CaptureInventoryGems(inventories);

        return new GemSnapshot(sockets, DateTime.UtcNow, inventoryGems);
    }

    /// <summary>
    /// Gems from the main inventory — Swap candidates. Active/support gems only
    /// (requires the <c>SkillGem</c> component). Never throws outwards.
    /// </summary>
    private static List<InventoryGemInfo> CaptureInventoryGems(IList<InventoryHolder> inventories)
    {
        var result = new List<InventoryGemInfo>(64);

        try
        {
            InventoryHolder? holder = null;

            for (var i = 0; i < inventories.Count; i++)
            {
                if (inventories[i]?.TypeId == InventoryNameE.MainInventory1)
                {
                    holder = inventories[i];
                    break;
                }
            }

            var items = holder?.Inventory?.InventorySlotItems;
            if (items is null)
                return result;

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i]?.Item;
                if (item is null || item.Address == 0)
                    continue;

                try
                {
                    if (!item.IsValid)
                        continue;

                    if (!item.TryGetComponent<SkillGem>(out var skillGem) || skillGem is null)
                        continue;

                    if (!item.TryGetComponent<Base>(out var @base) || @base is null)
                        continue;

                    var name = @base.Name;
                    if (string.IsNullOrEmpty(name))
                        continue;

                    var level = skillGem.Level;

                    var maxLevel = 0;
                    try
                    {
                        maxLevel = skillGem.MaxLevel;
                    }
                    catch
                    {
                        // Without MaxLevel fall back to the usual cap of 20.
                    }

                    var isMaxLevel = maxLevel > 0 ? level >= maxLevel : level >= 20;

                    var quality = 0;
                    if (item.TryGetComponent<Quality>(out var qualityComponent) && qualityComponent is not null)
                        quality = qualityComponent.ItemQuality;

                    result.Add(new InventoryGemInfo(name, level, maxLevel, isMaxLevel, item.Address, quality));
                }
                catch (Exception ex)
                {
                    LogOnce($"inventory gem: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            LogOnce($"MainInventory1: {ex.GetType().Name}: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Cheap "fingerprint" of the player inventories: hash of (InventSlot, ServerRequestCounter) pairs
    /// sorted by slot. Lets us skip reading items/gems when the server changed nothing.
    /// Returns 0 on any error.
    /// </summary>
    public static int CaptureServerCounter(GameController gameController)
    {
        if (gameController is null)
            return 0;

        try
        {
            var inventories = gameController.IngameState?.ServerData?.PlayerInventories;
            if (inventories is null)
                return 0;

            var pairs = new List<(int Slot, int Counter)>(inventories.Count);

            for (var i = 0; i < inventories.Count; i++)
            {
                var inventory = inventories[i]?.Inventory;
                if (inventory is null)
                    continue;

                pairs.Add(((int)inventory.InventSlot, inventory.ServerRequestCounter));
            }

            // Sort by slot so the result does not depend on the memory list order.
            pairs.Sort(static (left, right) => left.Slot.CompareTo(right.Slot));

            var hash = new HashCode();
            hash.Add(pairs.Count);

            for (var i = 0; i < pairs.Count; i++)
            {
                hash.Add(pairs[i].Slot);
                hash.Add(pairs[i].Counter);
            }

            return hash.ToHashCode();
        }
        catch (Exception ex)
        {
            LogOnce($"ServerRequestCounter: {ex.GetType().Name}: {ex.Message}");
            return 0;
        }
    }

    /// <summary>Find an inventory by <c>InventSlot</c>, not by index in the list.</summary>
    private static InventoryHolder? FindInventory(IList<InventoryHolder> inventories, InventorySlotE slot)
    {
        for (var i = 0; i < inventories.Count; i++)
        {
            var holder = inventories[i];
            var inventory = holder?.Inventory;
            if (inventory is null)
                continue;

            if (inventory.InventSlot == slot)
                return holder;
        }

        return null;
    }

    /// <summary>
    /// Equipped item and its geometry: first inventory slot, or the first entry of <c>Items</c>
    /// when the slot list is empty. The geometry is needed by M4b for per-socket frames.
    /// </summary>
    private static bool GetEquippedItem(ServerInventory? inventory, out EquippedItemInfo info)
    {
        info = default;

        if (inventory is null)
            return false;

        var slotItems = inventory.InventorySlotItems;
        var slotItem = slotItems is { Count: > 0 } ? slotItems[0] : null;
        var item = slotItem?.Item;

        if (item is null)
        {
            // In 330.5 ServerInventory.Item is the this[x, y] indexer, not the "whole item",
            // so the fallback comes from the flat Items list.
            var items = inventory.Items;
            if (items is { Count: > 0 })
                item = items[0];
        }

        if (item is null || item.Address == 0)
            return false;

        if (!item.IsValid)
            return false;

        var sizeX = slotItem?.SizeX ?? 0;
        var sizeY = slotItem?.SizeY ?? 0;

        // Equipment inventories may report zero Columns/Rows — fall back to the item size.
        var columns = inventory.Columns > 0 ? inventory.Columns : sizeX;
        var rows = inventory.Rows > 0 ? inventory.Rows : sizeY;

        // And vice versa: the item size may be 0 — take the inventory size.
        if (sizeX <= 0)
            sizeX = inventory.Columns;
        if (sizeY <= 0)
            sizeY = inventory.Rows;

        info = new EquippedItemInfo(item, sizeX, sizeY, columns, rows, inventory.InventType);
        return true;
    }

    /// <summary>Adds one entry per item socket (normal path or R1 fallback).</summary>
    private static void AppendSockets(
        EquippedItemInfo info,
        InventorySlotE slot,
        List<GemSocketSnapshot> target,
        IMemory? memory)
    {
        var item = info.Item;

        if (!item.TryGetComponent<Sockets>(out var sockets) || sockets is null)
            return;

        var totalSockets = sockets.NumberOfSockets;
        var (itemName, itemClassName) = ReadItemBase(item);

        var socketInfo = sockets.SocketInfo;
        if (socketInfo is { Count: > 0 })
        {
            AppendFromSocketInfo(socketInfo, slot, info, totalSockets, itemName, itemClassName, memory, target);
            return;
        }

        AppendFromFallback(sockets, slot, info, totalSockets, itemName, itemClassName, memory, target);
    }

    /// <summary>Normal path: <c>Sockets.SocketInfo</c> contains color, link and the gem entity.</summary>
    private static void AppendFromSocketInfo(
        List<Sockets.Socket> socketInfo,
        InventorySlotE slot,
        EquippedItemInfo info,
        int totalSockets,
        string itemName,
        string itemClassName,
        IMemory? memory,
        List<GemSocketSnapshot> target)
    {
        for (var i = 0; i < socketInfo.Count; i++)
        {
            var socket = socketInfo[i];
            if (socket is null)
                continue;

            try
            {
                target.Add(BuildSocket(
                    slot,
                    i,
                    socket.SocketColor,
                    socket.LinkGroup,
                    socket.SocketedGemEntity,
                    itemName,
                    itemClassName,
                    info.Item.Address,
                    info,
                    totalSockets,
                    memory));
            }
            catch (Exception ex)
            {
                LogOnce($"socket {slot}[{i}]: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// R1: <c>SocketInfo</c> is empty (memory garbage with large links), but gems/colors are known.
    /// Colors come from <c>SocketList</c>, gems from <c>SocketedGems</c>; LinkGroup is unknown (-1).
    /// </summary>
    private static void AppendFromFallback(
        Sockets sockets,
        InventorySlotE slot,
        EquippedItemInfo info,
        int totalSockets,
        string itemName,
        string itemClassName,
        IMemory? memory,
        List<GemSocketSnapshot> target)
    {
        var socketCount = sockets.NumberOfSockets;
        if (socketCount <= 0)
            return;

        var socketList = sockets.SocketList;
        if (socketList is null || socketList.Count == 0)
            return;

        LogOnce($"R1 fallback: {slot} sockets={socketCount}, socketInfo=0");

        var socketedGems = sockets.SocketedGems;
        var limit = Math.Min(socketCount, socketList.Count);

        for (var i = 0; i < limit; i++)
        {
            try
            {
                var gemEntity = FindSocketedGem(socketedGems, i);

                target.Add(BuildSocket(
                    slot,
                    i,
                    (SocketColor)socketList[i],
                    -1,
                    gemEntity,
                    itemName,
                    itemClassName,
                    info.Item.Address,
                    info,
                    totalSockets,
                    memory));
            }
            catch (Exception ex)
            {
                LogOnce($"socket (R1) {slot}[{i}]: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Finds the gem bound to the socket with the given physical index.</summary>
    private static Entity? FindSocketedGem(List<Sockets.SocketedGem>? socketedGems, int socketIndex)
    {
        if (socketedGems is null)
            return null;

        for (var i = 0; i < socketedGems.Count; i++)
        {
            var socketedGem = socketedGems[i];
            if (socketedGem is null)
                continue;

            if (socketedGem.SocketIndex == socketIndex)
                return socketedGem.GemEntity;
        }

        return null;
    }

    /// <summary>Item name and class (guarded: some items may have no <c>Base</c>).</summary>
    private static (string Name, string ClassName) ReadItemBase(Entity item)
    {
        try
        {
            if (item.TryGetComponent<Base>(out var @base) && @base is not null)
            {
                var name = @base.Name ?? string.Empty;
                var className = @base.Info?.BaseItemTypeDat?.ClassName ?? string.Empty;
                return (name, className);
            }
        }
        catch (Exception ex)
        {
            LogOnce($"item base: {ex.GetType().Name}: {ex.Message}");
        }

        return (string.Empty, string.Empty);
    }

    /// <summary>Single socket: color, link and (if present) gem data.</summary>
    private static GemSocketSnapshot BuildSocket(
        InventorySlotE slot,
        int socketIndex,
        SocketColor socketColor,
        int linkGroup,
        Entity? gemEntity,
        string itemName,
        string itemClassName,
        long itemAddress,
        EquippedItemInfo info,
        int totalSockets,
        IMemory? memory)
    {
        var isEmpty = gemEntity is null || gemEntity.Address == 0;

        var gemName = string.Empty;
        var gemLevel = 0;
        var gemQuality = 0;
        var requiredLevel = 0;
        uint totalExp = 0;
        var gemAddress = 0L;
        var expPercent = 0f;
        var isMaxLevel = false;
        var gemMaxLevel = 0;
        uint expPrev = 0;
        uint expMax = 0;
        uint expToNext = 0;

        if (!isEmpty)
        {
            gemAddress = gemEntity!.Address;

            try
            {
                if (gemEntity.IsValid)
                {
                    if (gemEntity.TryGetComponent<Base>(out var gemBase) && gemBase is not null)
                        gemName = gemBase.Name ?? string.Empty;

                    if (gemEntity.TryGetComponent<SkillGem>(out var skillGem) && skillGem is not null)
                    {
                        gemLevel = skillGem.Level;
                        totalExp = skillGem.TotalExpGained;

                        // Hint in case the pair offsets do not line up (may be bugged),
                        // so it is used only as a fallback for the percentage.
                        try
                        {
                            expToNext = skillGem.ExperienceToNextLevel;
                        }
                        catch
                        {
                            // The property may throw — treat it as no hint.
                        }

                        // MaxLevel is the gem "cap" (20/40 etc.); MAX is shown at that level.
                        try
                        {
                            gemMaxLevel = skillGem.MaxLevel;
                        }
                        catch
                        {
                            // Without MaxLevel assume the level is not max.
                        }

                        // If MaxLevel was not read (0), treat the usual level 20 as max.
                        isMaxLevel = (gemMaxLevel > 0 && gemLevel >= gemMaxLevel)
                                     || (gemMaxLevel == 0 && gemLevel >= 20);

                        if (isMaxLevel)
                        {
                            expPercent = 100f;
                        }
                        else
                        {
                            // 1. prev/max pair at the current offsets — if consistent with TotalExp.
                            var hasExpSource = false;

                            if (memory is not null && _expPrevOffset >= 0 && _expMaxOffset >= 0)
                            {
                                try
                                {
                                    var address = skillGem.Address;
                                    expPrev = memory.Read<uint>(address + _expPrevOffset);
                                    expMax = memory.Read<uint>(address + _expMaxOffset);
                                    hasExpSource = IsValidExpPair(expPrev, expMax, totalExp);
                                }
                                catch
                                {
                                    expPrev = 0;
                                    expMax = 0;
                                }
                            }

                            // 2. The pair is invalid — one-time self-calibration of offsets against the exp table.
                            if (!hasExpSource && memory is not null)
                            {
                                hasExpSource = TryCalibrateExpOffsets(
                                    memory,
                                    skillGem.Address,
                                    gemLevel,
                                    gemMaxLevel,
                                    out expPrev,
                                    out expMax);
                            }

                            if (hasExpSource)
                            {
                                expPercent = CalcExpPercent(totalExp, expPrev, expMax);
                            }
                            else if (gemLevel is >= 1 and <= 19 && (gemMaxLevel == 20 || gemMaxLevel == 0))
                            {
                                // 3. Offsets did not match — compute from the known table.
                                expPrev = GemCumulativeExp[gemLevel - 1];
                                expMax = GemCumulativeExp[gemLevel];
                                expPercent = CalcExpPercent(totalExp, expPrev, expMax);
                                hasExpSource = true;
                            }
                            else
                            {
                                expPrev = 0;
                                expMax = 0;
                            }

                            // 4. No valid data at all (or the result is 0) — try ExperienceToNextLevel.
                            if (!hasExpSource || expPercent <= 0f)
                            {
                                if (TryCalcPercentFromExpToNext(
                                        gemLevel,
                                        gemMaxLevel,
                                        totalExp,
                                        expToNext,
                                        out var percentFromNext))
                                {
                                    expPercent = percentFromNext;
                                }
                            }
                        }

                        // Inside ExileCore RequiredLevel lazily touches GrantedEffect and may throw.
                        try
                        {
                            requiredLevel = skillGem.RequiredLevel;
                        }
                        catch (Exception ex)
                        {
                            LogOnce($"SkillGem.RequiredLevel: {ex.GetType().Name}: {ex.Message}");
                        }
                    }

                    if (gemEntity.TryGetComponent<Quality>(out var quality) && quality is not null)
                        gemQuality = quality.ItemQuality;
                }
            }
            catch (Exception ex)
            {
                LogOnce($"gem {slot}[{socketIndex}]: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return new GemSocketSnapshot(
            Slot: slot,
            ItemName: itemName,
            ItemClassName: itemClassName,
            ItemAddress: itemAddress,
            SocketIndex: socketIndex,
            SocketColor: socketColor,
            LinkGroup: linkGroup,
            IsEmpty: isEmpty,
            GemName: gemName,
            GemLevel: gemLevel,
            GemQuality: gemQuality,
            RequiredLevel: requiredLevel,
            TotalExp: totalExp,
            GemAddress: gemAddress,
            ExpPercent: expPercent,
            IsMaxLevel: isMaxLevel,
            ExpPrev: expPrev,
            ExpMax: expMax,
            ItemSizeX: info.SizeX,
            ItemSizeY: info.SizeY,
            InventoryColumns: info.Columns,
            InventoryRows: info.Rows,
            InventoryType: info.InventoryType,
            TotalSockets: totalSockets,
            MaxLevel: gemMaxLevel,
            ExpToNext: expToNext);
    }

    /// <summary>Writes a message to the HUD log only once (and no more than <see cref="MaxLoggedMessages"/>).</summary>
    private static void LogOnce(string message)
    {
        lock (LogLock)
        {
            if (LoggedMessages.Count >= MaxLoggedMessages)
                return;

            if (!LoggedMessages.Add(message))
                return;
        }

        try
        {
            DebugWindow.LogError($"[GemHelper] {message}");
        }
        catch
        {
            // Logging must never break snapshot reading.
        }
    }
}
