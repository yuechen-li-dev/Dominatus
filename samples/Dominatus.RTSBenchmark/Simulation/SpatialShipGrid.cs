namespace Dominatus.RTSBenchmark.Simulation;

internal readonly record struct GridCell(int X, int Y);

internal sealed class SpatialShipGrid
{
    private readonly Dictionary<GridCell, List<int>> _cells = new();
    private readonly Dictionary<int, ShipState> _shipsById;
    private readonly List<int> _candidates = new();

    public SpatialShipGrid(float cellSize, IReadOnlyList<ShipState> ships)
    {
        if (cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize), "Cell size must be greater than zero.");
        CellSize = cellSize;
        _shipsById = ships.ToDictionary(s => s.Id);
    }

    public float CellSize { get; }
    public int PopulatedCells => _cells.Count;

    public void Rebuild(IEnumerable<ShipState> ships)
    {
        _cells.Clear();
        foreach (var ship in ships)
        {
            if (!ship.Alive) continue;
            var cell = ToCell(ship.X, ship.Y);
            if (!_cells.TryGetValue(cell, out var ids))
            {
                ids = new List<int>();
                _cells.Add(cell, ids);
            }

            ids.Add(ship.Id);
        }

        foreach (var ids in _cells.Values)
            ids.Sort();
    }

    public IReadOnlyList<int> QueryCandidateIds(float x, float y, float range, out int cellsVisited)
    {
        _candidates.Clear();
        var center = ToCell(x, y);
        var radius = Math.Max(0, (int)MathF.Ceiling(range / CellSize));
        cellsVisited = 0;

        for (var cellX = center.X - radius; cellX <= center.X + radius; cellX++)
        {
            for (var cellY = center.Y - radius; cellY <= center.Y + radius; cellY++)
            {
                cellsVisited++;
                if (!_cells.TryGetValue(new GridCell(cellX, cellY), out var ids)) continue;
                _candidates.AddRange(ids);
            }
        }

        _candidates.Sort();
        return _candidates;
    }

    public ShipState ShipById(int id) => _shipsById[id];

    private GridCell ToCell(float x, float y) => new(FloorToCell(x), FloorToCell(y));

    private int FloorToCell(float coordinate) => (int)MathF.Floor(coordinate / CellSize);
}
