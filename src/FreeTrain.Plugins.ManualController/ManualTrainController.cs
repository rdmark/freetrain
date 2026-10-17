// Behavioral port of FreeTrain's manual controller; see prototype/assets/PROVENANCE.md.
// Original: Copyright (c) 2002 Kohsuke Kawaguchi et al.; LGPL-2.1-or-later (COPYING.txt).
using System.Collections.Immutable;
using FreeTrain.Simulation;

namespace FreeTrain.Plugins.ManualController;

public sealed class ManualTrainController : ITrainController
{
    public const string ContributionId = "{5567FE11-FC2C-4a1d-836F-722EF9DC957C}";
    public string Id => "manual";
    public StopAction AtStation(ManualInput input, bool firstCall)
    {
        if (firstCall) return input.Pass ? StopAction.Depart : StopAction.Wait;
        if (input.Depart) return StopAction.Depart;
        return input.Reverse ? StopAction.Reverse : StopAction.Wait;
    }
    public Direction AtJunction(ManualInput input, Direction heading, ImmutableArray<Direction> exits)
    {
        if (input.Left && exits.Contains(heading.Left())) return heading.Left();
        if (input.Right && exits.Contains(heading.Right())) return heading.Right();
        return exits.Contains(heading) ? heading : exits[0];
    }
}
