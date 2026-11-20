using System.Runtime.InteropServices;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Interfaces;
using GameOffsets;

namespace GemHelper.Core;

/// <summary>
/// Чтение гемов из сокетов надетых предметов.
/// Все обращения к памяти игры — защитные: ошибка одного слота/гема не должна валить Tick.
/// </summary>
public static class GemSnapshotReader
{
    /// <summary>Слоты экипировки, в которых нас интересуют сокеты (включая второй набор оружия).</summary>
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

    // Дедупликация сообщений: читаем память каждый тик, поэтому лог не должен спамить.
    private const int MaxLoggedMessages = 64;
    private static readonly HashSet<string> LoggedMessages = new(StringComparer.Ordinal);
    private static readonly object LogLock = new();

    // Кумулятивный опыт обычного гема (max level 20) по уровням: индекс = level - 1.
    private static readonly uint[] GemCumulativeExp =
    {
        0, 70, 308, 927, 2280, 4908, 9590, 17564, 30536, 51012,
        82746, 131368, 205048, 315864, 481484, 727896, 1094904, 1639248, 2447448, 3644748,
    };

    // Диапазон сканирования пары (prev, max) при самокалибровке оффсетов.
    private const int ExpScanStart = 0x18;
    private const int ExpScanEnd = 0x60;
    private const int ExpScanStep = 4;

    // Текущие оффсеты пары опыт/опыт-следующего-уровня. В 330.5 поля могли сдвинуться,
    // поэтому они не readonly: при первом удобном геме пара калибруется по таблице опыта.
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

    /// <summary>Пара (prev, max) согласуется с суммарным опытом гема.</summary>
    private static bool IsValidExpPair(uint prev, uint max, uint totalExp)
        => max > prev && prev <= totalExp && totalExp <= max;

    /// <summary>Процент прогресса до следующего уровня; вне диапазона — 0..100.</summary>
    private static float CalcExpPercent(uint totalExp, uint prev, uint max)
    {
        if (max <= prev)
            return 0f;

        var current = totalExp < prev ? prev : totalExp;
        return (float)Math.Clamp((current - prev) * 100.0 / (max - prev), 0d, 100d);
    }

    /// <summary>Отклонение значения от табличного не больше ~1%.</summary>
    private static bool IsCloseToTable(uint value, uint expected)
    {
        var diff = value >= expected ? (long)value - expected : (long)expected - value;
        return diff <= expected / 100 + 1;
    }

    /// <summary>
    /// Однократная самокалибровка пары (prev, max): ищем в SkillGem два подряд идущих uint,
    /// совпадающих с табличными границами текущего уровня. Сначала точное совпадение, затем ±1%.
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

        // Калибруемся только по обычным гемам и «серединным» уровням (у 1 и 20 границы тривиальны).
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

                // Точное совпадение с таблицей — лучший вариант.
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
            // Скан не должен ломать Capture.
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

        // Один раз за процесс: полезно для диагностики, не спамит.
        LogOnce($"exp offsets calibrated: prev=0x{offset:X}, max=0x{offset + 4:X}");
    }

    /// <summary>
    /// Fallback по <c>SkillGem.ExperienceToNextLevel</c>: свойство может означать как «осталось»,
    /// так и «требуется на уровень», а может быть основано на багованном ExperiencePrevLevel.
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

        // Сколько опыта уже набрано внутри текущего уровня (0..required).
        var inLevel = totalExp > tablePrev ? Math.Min(totalExp - tablePrev, required) : 0u;

        // Кандидат «осталось опыта»: если свойство действительно означает остаток, оно должно
        // быть близко к этому значению.
        var remainingCandidate = required - inLevel;

        // Расстояния до двух гипотез — выбираем ту, что ближе. Так значение, равное required
        // (исправленное свойство «сколько нужно на уровень»), больше не даёт 0 %.
        var distFixed = expToNext >= required
            ? (long)expToNext - required
            : (long)required - expToNext;

        var distRemaining = expToNext >= remainingCandidate
            ? (long)expToNext - remainingCandidate
            : (long)remainingCandidate - expToNext;

        if (distRemaining < distFixed)
        {
            // Свойство = «осталось опыта» (не больше required, иначе ветка недостижима).
            var remaining = expToNext < required ? required - expToNext : 0u;

            percent = (float)Math.Clamp(remaining * 100.0 / required, 0d, 100d);
            return true;
        }

        // Свойство = «нужно опыта на уровень» (или неоднозначно при 0 % прогресса).
        percent = (float)Math.Clamp(inLevel * 100.0 / required, 0d, 100d);
        return true;
    }

    /// <summary>Надетый предмет вместе с геометрией инвентаря (нужна M4b для посокетных рамок).</summary>
    private readonly record struct EquippedItemInfo(
        Entity Item,
        int SizeX,
        int SizeY,
        int Columns,
        int Rows,
        InventoryTypeE InventoryType);

    /// <summary>
    /// Строит снимок сокетов всех надетых предметов. Никогда не бросает исключений наружу.
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

        // Память нужна для чтения ExperiencePrevLevel/ExperienceMaxLevel из SkillGemOffsets.
        IMemory? memory = null;
        try
        {
            memory = gameController.Memory;
        }
        catch
        {
            // Без памяти прогресс опыта будет 0 — это не критично.
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

        return new GemSnapshot(sockets, DateTime.UtcNow);
    }

    /// <summary>
    /// Дешёвый «отпечаток» инвентарей игрока: хэш пар (InventSlot, ServerRequestCounter),
    /// отсортированных по слоту. Позволяет не читать предметы/гемы, если сервер ничего не менял.
    /// При любой ошибке возвращает 0.
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

            // Сортировка по слоту — чтобы порядок не зависел от порядка списка в памяти.
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

    /// <summary>Ищем инвентарь по <c>InventSlot</c>, а не по индексу в списке.</summary>
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
    /// Надетый предмет и его геометрия: первый слот инвентаря, при пустом списке — первый из <c>Items</c>.
    /// Геометрия нужна M4b для посокетных рамок.
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
            // В 330.5 ServerInventory.Item — это индексатор this[x, y], а не «предмет целиком»,
            // поэтому fallback берём из плоского списка Items.
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

        // У инвентарей экипировки Columns/Rows могут приходить нулевыми — подстраховываемся размерами предмета.
        var columns = inventory.Columns > 0 ? inventory.Columns : sizeX;
        var rows = inventory.Rows > 0 ? inventory.Rows : sizeY;

        // И наоборот: размеры предмета могут быть 0 — берём размеры инвентаря.
        if (sizeX <= 0)
            sizeX = inventory.Columns;
        if (sizeY <= 0)
            sizeY = inventory.Rows;

        info = new EquippedItemInfo(item, sizeX, sizeY, columns, rows, inventory.InventType);
        return true;
    }

    /// <summary>Добавляет по записи на каждый сокет предмета (обычный путь или R1-fallback).</summary>
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

    /// <summary>Обычный путь: <c>Sockets.SocketInfo</c> содержит цвет, линк и сущность гема.</summary>
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
    /// R1: <c>SocketInfo</c> пуст (мусор в памяти при больших линках), но гемы/цвета известны.
    /// Цвета берём из <c>SocketList</c>, гемы — из <c>SocketedGems</c>; LinkGroup неизвестен (-1).
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

    /// <summary>Ищет гем, привязанный к сокету с указанным физическим индексом.</summary>
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

    /// <summary>Имя и класс предмета (защитно: у некоторых предметов может не быть <c>Base</c>).</summary>
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

    /// <summary>Один сокет: цвет, линк и (если есть) данные гема.</summary>
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

                        // Подсказка на случай, если оффсеты пары не сойдутся (может быть багованной),
                        // поэтому используем только как fallback для процента.
                        try
                        {
                            expToNext = skillGem.ExperienceToNextLevel;
                        }
                        catch
                        {
                            // Свойство может кидать — считаем, что подсказки нет.
                        }

                        // MaxLevel — «потолок» гема (20/40 и т.п.); на нём показываем MAX.
                        try
                        {
                            gemMaxLevel = skillGem.MaxLevel;
                        }
                        catch
                        {
                            // Без MaxLevel считаем, что уровень не максимальный.
                        }

                        isMaxLevel = gemMaxLevel > 0 && gemLevel >= gemMaxLevel;

                        if (isMaxLevel)
                        {
                            expPercent = 100f;
                        }
                        else
                        {
                            // 1. Пара prev/max по текущим оффсетам — если согласуется с TotalExp.
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

                            // 2. Пара невалидна — однократная самокалибровка оффсетов по таблице опыта.
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
                                // 3. Оффсеты не сошлись — считаем по известной таблице.
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

                            // 4. Совсем нет валидных данных (или получился 0) — пробуем ExperienceToNextLevel.
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

                        // RequiredLevel внутри ExileCore лениво лезет в GrantedEffect и может кидать.
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

    /// <summary>Пишет сообщение в лог HUD только один раз (и не больше <see cref="MaxLoggedMessages"/>).</summary>
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
            // Логирование не должно ронять чтение снимка.
        }
    }
}
