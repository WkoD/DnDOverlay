namespace DnDOverlay.Control;

/// <summary>
/// What this process's memory did over one frame window, read beside the frame times so the two
/// lines can be laid side by side (<c>ControlLog.StageMemory</c>).
/// <para>
/// <b>It exists to tell three causes apart that 4013 cannot.</b> The fourth hand-run of M4 found
/// the window stuttering on every change of WIDTH - never of height - and the frame line read
/// 1335.6 ms of garbage collection in 173 full collections over 30 seconds, at a CPU share under one
/// percent. A restart cleared it. That is a state that builds up over a session, and 4013 only
/// counts full collections and their pause, which every one of these causes produces alike:
/// </para>
/// <list type="bullet">
/// <item><b>churn</b> - a great deal allocated per window and nothing kept: the allocation figure is
/// high, heap and process stay flat;</item>
/// <item><b>a leak</b> - something kept per redraw or per layout: the heap climbs from one line to
/// the next and does not come back;</item>
/// <item><b>bitmap pressure</b> - WPF reports the unmanaged memory behind a bitmap to the collector
/// and forces full collections when many arrive quickly: few managed bytes, full collections far
/// above the gen0 and gen1 counts, and the PROCESS growing where the heap does not.</item>
/// </list>
/// <para>
/// The cache counts are the fourth witness: a cache that grows with every width change rather than
/// with every asset names the culprit directly.
/// </para>
/// </summary>
internal sealed class StageMemory
{
    private long _allocated = GC.GetTotalAllocatedBytes();
    private int _gen0 = GC.CollectionCount(0);
    private int _gen1 = GC.CollectionCount(1);
    private int _gen2 = GC.CollectionCount(2);

    /// <summary>One reading, as differences since the last one where a difference is the point.</summary>
    internal sealed record Reading(
        double AllocatedMb,
        double HeapMb,
        double LargeMb,
        double ProcessMb,
        int Gen0,
        int Gen1,
        int Gen2);

    /// <summary>
    /// Reads and moves the marks on. Allocation and collections are counted since the previous
    /// reading; heap, large-object heap and process are how things stand now, because a trend is
    /// read across lines and a difference would hide the level.
    /// </summary>
    internal Reading Read()
    {
        var allocated = GC.GetTotalAllocatedBytes();
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        var info = GC.GetGCMemoryInfo();

        // Index 3 is the large-object heap: gen0, gen1, gen2, LOH, pinned. Objects of 85 KB and
        // more live there, and allocating them is one of the two things that start a full
        // collection on its own.
        var large = info.GenerationInfo.Length > 3 ? info.GenerationInfo[3].SizeAfterBytes : 0;

        var reading = new Reading(
            AllocatedMb: Mb(allocated - _allocated),
            HeapMb: Mb(info.HeapSizeBytes),
            LargeMb: Mb(large),
            ProcessMb: Mb(Environment.WorkingSet),
            Gen0: gen0 - _gen0,
            Gen1: gen1 - _gen1,
            Gen2: gen2 - _gen2);

        _allocated = allocated;
        _gen0 = gen0;
        _gen1 = gen1;
        _gen2 = gen2;

        return reading;
    }

    private static double Mb(long bytes) => Math.Round(bytes / (1024d * 1024d), 1);
}
