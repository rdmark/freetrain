// Behavioral extraction from FreeTrain at 1f755ca; see prototype/assets/PROVENANCE.md.
// Original: Copyright (c) 2002 Kohsuke Kawaguchi et al.; LGPL-2.1-or-later (COPYING.txt).
using System.Collections.Immutable;

namespace FreeTrain.Simulation;

public sealed record PendingAction(long DueTick, long Order, string Kind, string SubjectId);

// Value actions replace legacy delegates and singleton/UI callbacks.
public sealed class SimulationClock(DateTime start)
{
    private readonly PriorityQueue<PendingAction, (long DueTick, long Order)> queue = new();
    private long nextOrder;
    public long Tick { get; private set; }
    public DateTime Time => start.AddMinutes(Tick);
    public long NextOrder => nextOrder;
    public PendingAction Schedule(long dueTick, string kind, string subjectId)
    {
        if (dueTick <= Tick) throw new ArgumentOutOfRangeException(nameof(dueTick));
        var action = new PendingAction(dueTick, nextOrder++, kind, subjectId);
        queue.Enqueue(action, (action.DueTick, action.Order));
        return action;
    }
    public void Restore(long tick, long order, IEnumerable<PendingAction> pending)
    {
        queue.Clear(); Tick = tick; nextOrder = order;
        foreach (var action in pending) queue.Enqueue(action, (action.DueTick, action.Order));
    }
    public ImmutableArray<PendingAction> Advance()
    {
        Tick++;
        var actions = ImmutableArray.CreateBuilder<PendingAction>();
        while (queue.TryPeek(out _, out var priority) && priority.DueTick <= Tick)
            actions.Add(queue.Dequeue());
        return actions.ToImmutable();
    }
    public ImmutableArray<PendingAction> Pending => queue.UnorderedItems.Select(x => x.Element)
        .OrderBy(x => x.DueTick).ThenBy(x => x.Order).ToImmutableArray();
}
