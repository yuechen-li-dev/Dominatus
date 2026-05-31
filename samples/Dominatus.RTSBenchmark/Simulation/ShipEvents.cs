namespace Dominatus.RTSBenchmark.Simulation;

public sealed record TargetSpotted(int Tick, int SourceShipId, int TargetShipId, Faction TargetFaction);
public sealed record AllyUnderFire(int Tick, int AllyShipId, int AttackerShipId);
public sealed record RepairRequested(int Tick, int SourceShipId, int AllyShipId);
public sealed record ShipDestroyed(int Tick, int ShipId, Faction Faction);
public sealed record CommandFocusOrder(int Tick, int CommanderShipId, int TargetShipId);
public sealed record SynapseLost(int Tick, int SynapseShipId);
