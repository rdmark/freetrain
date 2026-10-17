using FreeTrain.Content;
using FreeTrain.Plugins.ManualController;
using FreeTrain.Session;
using FreeTrain.Simulation;

namespace FreeTrain.Server;

public static class SessionComposition
{
    // Explicit contribution registration; no assembly scanning or arbitrary DLL loading.
    public static SessionHost CreateReferenceHost() => new(ReferenceScenario.Create(), "operator-1",
        [() => new AutomaticController(), () => new ManualTrainController()]);
}
