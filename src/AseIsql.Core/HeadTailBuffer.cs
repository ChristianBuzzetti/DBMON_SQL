using System.Collections;
using System.Globalization;

namespace AseIsql;

// Bounded retention in arrival order: preserve the beginning and the most recent diagnostics.
// Report synchronously so consumers can buffer without queuing one UI operation per server message.
// The default limits suit a live pane; a run's own output passes larger count limits and a character budget
// (split evenly between head and tail) so long reports such as sp_sysmon survive whole.
public sealed class HeadTailBuffer<T>(Action<T>? onAdded = null, int headLimit = HeadTailBuffer<T>.HeadLimit,
    int tailLimit = HeadTailBuffer<T>.TailLimit, long maxCharacters = long.MaxValue, Func<T, long>? size = null) : IReadOnlyList<T>
{
    public const int HeadLimit = 250, TailLimit = 250;
    private readonly object gate = new();
    private readonly List<T> head = [];
    private readonly Queue<T> tail = new();
    private readonly long headBudget = maxCharacters / 2, tailBudget = maxCharacters - maxCharacters / 2;
    private long headCharacters, tailCharacters;
    private bool headClosed;
    private int omitted;
    private long Size(T item) => size == null ? 0 : Math.Max(0, size(item));

    public int Count { get { lock (gate) return head.Count + tail.Count; } }
    public int OmittedCount { get { lock (gate) return omitted; } }
    public T this[int index] { get { lock (gate) return index < head.Count ? head[index] : tail.ElementAt(index - head.Count); } }

    public void Add(T item)
    {
        lock (gate)
        {
            long length = Size(item);
            if (!headClosed && head.Count < headLimit && headCharacters + length <= headBudget) { head.Add(item); headCharacters += length; }
            else
            {
                headClosed = true;
                tail.Enqueue(item); tailCharacters += length;
                while (tail.Count > tailLimit || (tailCharacters > tailBudget && tail.Count > 0))
                { tailCharacters -= Size(tail.Dequeue()); omitted++; }
            }
            onAdded?.Invoke(item);
        }
    }

    // Append another buffer's retained rows, carrying its missing middle forward; this buffer's own limits apply.
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

    public void Clear() { lock (gate) { head.Clear(); tail.Clear(); omitted = 0; headCharacters = tailCharacters = 0; headClosed = false; } }
    public IEnumerator<T> GetEnumerator() => Snapshot().GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
