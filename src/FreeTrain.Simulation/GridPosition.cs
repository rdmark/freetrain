namespace FreeTrain.Simulation;

// Domain coordinates only. Projection and camera state belong to the browser.
public readonly record struct GridPosition(int X, int Y, int Z);

public readonly record struct ManualInput(bool Pass, bool Depart, bool Reverse, bool Left, bool Right);
