namespace GemHelper.Core;

/// <summary>
/// Неизменяемый снимок всех сокетов надетых предметов на момент <see cref="CapturedAtUtc"/>.
/// Строится в <c>Tick</c> (см. <see cref="GemSnapshotReader"/>), читается только в <c>Render</c>.
/// </summary>
public sealed class GemSnapshot
{
    /// <summary>Пустой снимок — используется до первого успешного чтения и вне игры.</summary>
    public static GemSnapshot Empty { get; } = new(Array.Empty<GemSocketSnapshot>(), DateTime.MinValue, 0);

    public GemSnapshot(IReadOnlyList<GemSocketSnapshot> sockets, DateTime capturedAtUtc)
        : this(sockets, capturedAtUtc, ComputeContentHash(sockets))
    {
    }

    private GemSnapshot(IReadOnlyList<GemSocketSnapshot> sockets, DateTime capturedAtUtc, int contentHash)
    {
        ArgumentNullException.ThrowIfNull(sockets);

        Sockets = sockets;
        CapturedAtUtc = capturedAtUtc;
        ContentHash = contentHash;
    }

    /// <summary>Сокеты всех надетых предметов в физическом порядке сокетов.</summary>
    public IReadOnlyList<GemSocketSnapshot> Sockets { get; }

    /// <summary>Время (UTC), когда снимок был прочитан из памяти игры.</summary>
    public DateTime CapturedAtUtc { get; }

    /// <summary>
    /// Хэш содержимого (адреса предметов/гемов, уровень, качество, пустота сокета).
    /// Нужен для будущего кэша: одинаковый хэш — одинаковое содержимое.
    /// </summary>
    public int ContentHash { get; }

    /// <summary>В снимке нет ни одного сокета (нет надетых предметов с сокетами).</summary>
    public bool IsEmpty => Sockets.Count == 0;

    /// <summary>Общее количество сокетов.</summary>
    public int SocketCount => Sockets.Count;

    /// <summary>Количество сокетов с гемом.</summary>
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

    /// <summary>Совпадает ли содержимое с другим снимком (быстрая проверка по хэшу).</summary>
    public bool IsSameContent(GemSnapshot? other) => other is not null && other.ContentHash == ContentHash;

    private static int ComputeContentHash(IReadOnlyList<GemSocketSnapshot> sockets)
    {
        // FNV-1a (32 бита): быстрый и без аллокаций, для кэша достаточно.
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
                // Прогресс опыта растёт без смены адресов — иначе таблица «залипнет» до full refresh.
                hash = Mix(hash, (int)Math.Round(socket.ExpPercent));
            }

            return (int)hash;
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
