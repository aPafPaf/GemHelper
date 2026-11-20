namespace GemHelper.Core;

/// <summary>
/// Immutable snapshot of all equipped item sockets at <see cref="CapturedAtUtc"/>.
/// Built in <c>Tick</c> (see <see cref="GemSnapshotReader"/>), read only in <c>Render</c>.
/// </summary>
public sealed class GemSnapshot
{
    /// <summary>Empty snapshot — used before the first successful read and outside the game.</summary>
    public static GemSnapshot Empty { get; } =
        new(Array.Empty<GemSocketSnapshot>(), DateTime.MinValue, Array.Empty<InventoryGemInfo>(), 0);

    public GemSnapshot(IReadOnlyList<GemSocketSnapshot> sockets, DateTime capturedAtUtc)
        : this(sockets, capturedAtUtc, Array.Empty<InventoryGemInfo>())
    {
    }

    public GemSnapshot(
        IReadOnlyList<GemSocketSnapshot> sockets,
        DateTime capturedAtUtc,
        IReadOnlyList<InventoryGemInfo>? inventoryGems)
        : this(sockets, capturedAtUtc, inventoryGems, ComputeContentHash(sockets, inventoryGems))
    {
    }

    private GemSnapshot(
        IReadOnlyList<GemSocketSnapshot> sockets,
        DateTime capturedAtUtc,
        IReadOnlyList<InventoryGemInfo>? inventoryGems,
        int contentHash)
    {
        ArgumentNullException.ThrowIfNull(sockets);

        Sockets = sockets;
        InventoryGems = inventoryGems ?? Array.Empty<InventoryGemInfo>();
        CapturedAtUtc = capturedAtUtc;
        ContentHash = contentHash;
    }

    /// <summary>Sockets of all equipped items in physical socket order.</summary>
    public IReadOnlyList<GemSocketSnapshot> Sockets { get; }

    /// <summary>Gems in the main inventory (Swap candidates).</summary>
    public IReadOnlyList<InventoryGemInfo> InventoryGems { get; }

    /// <summary>Time (UTC) when the snapshot was read from game memory.</summary>
    public DateTime CapturedAtUtc { get; }

    /// <summary>
    /// Content hash (item/gem addresses, level, quality, empty socket flag).
    /// Intended for a future cache: same hash — same content.
    /// </summary>
    public int ContentHash { get; }

    /// <summary>The snapshot has no sockets at all (no equipped items with sockets).</summary>
    public bool IsEmpty => Sockets.Count == 0;

    /// <summary>Total number of sockets.</summary>
    public int SocketCount => Sockets.Count;

    /// <summary>Number of sockets that contain a gem.</summary>
    public int GemCount
    {
        get
        {
            var count = 0;
            for (var i = 0; i < Sockets.Count; i++)
            {
                if (!Sockets[i].IsEmpty)
                    count++;
            }

            return count;
        }
    }

    /// <summary>Whether the content matches another snapshot (fast hash check).</summary>
    public bool IsSameContent(GemSnapshot? other) => other is not null && other.ContentHash == ContentHash;

    private static int ComputeContentHash(
        IReadOnlyList<GemSocketSnapshot> sockets,
        IReadOnlyList<InventoryGemInfo>? inventoryGems)
    {
        // FNV-1a (32 bit): fast and allocation free, good enough for a cache.
        unchecked
        {
            const uint offsetBasis = 2166136261;

            var hash = offsetBasis;
            hash = Mix(hash, sockets.Count);

            for (var i = 0; i < sockets.Count; i++)
            {
                var socket = sockets[i];
                hash = Mix(hash, (int)socket.Slot);
                hash = Mix(hash, socket.ItemAddress);
                hash = Mix(hash, socket.SocketIndex);
                hash = Mix(hash, socket.GemAddress);
                hash = Mix(hash, socket.GemLevel);
                hash = Mix(hash, socket.GemQuality);
                hash = Mix(hash, socket.IsEmpty ? 1 : 0);
                // Exp progress grows without address changes, otherwise the table would stay stale until full refresh.
                hash = Mix(hash, (int)Math.Round(socket.ExpPercent));
            }

            // Inventory gems: addresses/levels change after a swap, so the snapshot must be rebuilt.
            var gems = inventoryGems ?? Array.Empty<InventoryGemInfo>();
            hash = Mix(hash, gems.Count);

            for (var i = 0; i < gems.Count; i++)
            {
                var gem = gems[i];
                hash = Mix(hash, gem.ItemAddress);
                hash = Mix(hash, gem.Level);
                hash = Mix(hash, gem.Quality);
                hash = Mix(hash, gem.Name);
            }

            return (int)hash;
        }
    }

    /// <summary>FNV-1a over the characters of a string (inventory gem names).</summary>
    private static uint Mix(uint hash, string value)
    {
        unchecked
        {
            for (var i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619;
            }

            return Mix(hash, value.Length);
        }
    }

    private static uint Mix(uint hash, long value)
    {
        unchecked
        {
            hash ^= (uint)value;
            hash *= 16777619;
            hash ^= (uint)(value >> 32);
            hash *= 16777619;
            return hash;
        }
    }

    private static uint Mix(uint hash, int value) => Mix(hash, (long)value);
}
