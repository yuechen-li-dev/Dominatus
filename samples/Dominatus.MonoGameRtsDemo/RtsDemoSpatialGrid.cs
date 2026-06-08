using Dominatus.Core.Runtime;
using Microsoft.Xna.Framework;

namespace Dominatus.MonoGameRtsDemo;

internal readonly record struct RtsDemoGridCell(int X, int Y);

public sealed class RtsDemoSpatialGrid
{
    private readonly Dictionary<RtsDemoGridCell, List<AgentId>> _cells = new();
    private readonly List<AgentId> _candidates = new();

    public RtsDemoSpatialGrid(float cellSize)
    {
        if (cellSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(cellSize), "Cell size must be greater than zero.");

        CellSize = cellSize;
    }

    public float CellSize { get; }
    public int PopulatedCells => _cells.Count;

    public void Rebuild(IEnumerable<ShipVisualState> ships)
    {
        _cells.Clear();
        foreach (var ship in ships)
        {
            if (!ship.Alive)
                continue;

            var cell = ToCell(ship.Position);
            if (!_cells.TryGetValue(cell, out var ids))
            {
                ids = new List<AgentId>();
                _cells.Add(cell, ids);
            }

            ids.Add(ship.AgentId);
        }

        foreach (var ids in _cells.Values)
            ids.Sort((a, b) => a.Value.CompareTo(b.Value));
    }

    public IReadOnlyList<AgentId> QueryCandidateIds(Vector2 position, float range)
    {
        _candidates.Clear();
        var center = ToCell(position);
        var radius = Math.Max(0, (int)MathF.Ceiling(range / CellSize));

        for (var cellX = center.X - radius; cellX <= center.X + radius; cellX++)
        {
            for (var cellY = center.Y - radius; cellY <= center.Y + radius; cellY++)
            {
                if (_cells.TryGetValue(new RtsDemoGridCell(cellX, cellY), out var ids))
                    _candidates.AddRange(ids);
            }
        }

        _candidates.Sort((a, b) => a.Value.CompareTo(b.Value));
        return _candidates;
    }

    private RtsDemoGridCell ToCell(Vector2 position) => new(FloorToCell(position.X), FloorToCell(position.Y));

    private int FloorToCell(float coordinate) => (int)MathF.Floor(coordinate / CellSize);
}
