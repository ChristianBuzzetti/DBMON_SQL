using System.Collections;
using System.Globalization;

namespace AseIsql;

// Bounded retention in arrival order: preserve the beginning and the most recent diagnostics.
// Report synchronously so consumers can buffer without queuing one UI operation per server message.
public sealed class HeadTailBuffer<T>(Action<T>? onAdded = null) : IReadOnlyList<T>
{
    public const int HeadLimit = 250, TailLimit = 250;
    private readonly object gate = new();
    private readonly List<T> head = [];
    private readonly Queue<T> tail = new();
    private int omitted;

    public int Count { get { lock (gate) return head.Count + tail.Count; } }
    public int OmittedCount { get { lock (gate) return omitted; } }
    public T this[int index] { get { lock (gate) return index < head.Count ? head[index] : tail.ElementAt(index - head.Count); } }

    public void Add(T item)
    {
        lock (gate)
        {
            if (head.Count < HeadLimit) head.Add(item);
            else
            {
                if (tail.Count == TailLimit) { tail.Dequeue(); omitted++; }
                tail.Enqueue(item);
            }
            onAdded?.Invoke(item);
        }
    }

    // Append another buffer's retained rows, carrying its missing middle forward. Both buffers use the same limits.
    public void Append(IEnumerable<T> retained, int omittedCount = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(omittedCount);
        lock (gate)
        {
            foreach (var item in retained) Add(item);
            omitted += omittedCount;
        }
    }

    public IReadOnlyList<T> Snapshot(Func<int, T>? omissionMarker = null)
    {
        lock (gate)
        {
            var rows = new List<T>(head.Count + tail.Count + 1);
            rows.AddRange(head);
            if (omitted > 0 && omissionMarker != null) rows.Add(omissionMarker(omitted));
            rows.AddRange(tail);
            return rows;
        }
    }

    public static string OmissionText(int count) => "… " + count.ToString(CultureInfo.InvariantCulture) + " messages omitted …";

    public void Clear() { lock (gate) { head.Clear(); tail.Clear(); omitted = 0; } }
    public IEnumerator<T> GetEnumerator() => Snapshot().GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
