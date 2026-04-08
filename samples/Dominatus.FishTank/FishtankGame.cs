using Dominatus.Core.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Dominatus.Fishtank;

public sealed class FishtankGame : Game
{
    // ------- config -------
    private const int ScreenW      = 1280;
    private const int ScreenH      = 720;
    private const int PreyCount    = 15;
    private const int PredCount    = 2;
    private const float FoodRadius = 6f;
    private const float PreyDetectPredDist  = 120f;
    private const float PredDetectPreyDist  = 180f;
    private const float PreyDetectFoodDist  = 150f;

    // ------- MonoGame -------
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _sb = null!;
    private CircleRenderer _circles = null!;
    private SpriteFont? _font;

    // ------- Dominatus -------
    private AiWorld _world = null!;
    private readonly List<AiAgent> _prey    = new();
    private readonly List<AiAgent> _predators = new();

    // ------- food pellets -------
    private readonly List<Vector2> _food = new();
    private readonly Random _rng = new();

    public FishtankGame()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth  = ScreenW,
            PreferredBackBufferHeight = ScreenH
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = "Dominatus Fishtank";
    }

    protected override void Initialize()
    {
        // --- Build actuator host ---
        var host = new ActuatorHost();
        host.Register(new SetVelocityHandler());
        host.Register(new SteerTowardHandler());
        host.Register(new SteerAwayHandler());
        host.Register(new WanderHandler());

        _world = new AiWorld(host);

        // --- Spawn prey ---
        var preyColors = new (float r, float g, float b)[]
        {
            (0.3f, 0.6f, 1.0f),
            (0.3f, 1.0f, 0.6f),
            (0.8f, 0.8f, 0.3f),
            (0.7f, 0.4f, 1.0f),
        };

        for (int i = 0; i < PreyCount; i++)
        {
            var c = preyColors[i % preyColors.Length];
            var agent = FishFactory.CreatePrey(
                _rng.NextSingle() * ScreenW,
                _rng.NextSingle() * ScreenH,
                r:  8f, cr: c.r, cg: c.g, cb: c.b);
            _world.Add(agent);
            _prey.Add(agent);
        }

        // --- Spawn predators ---
        for (int i = 0; i < PredCount; i++)
        {
            var agent = FishFactory.CreatePredator(
                _rng.NextSingle() * ScreenW,
                _rng.NextSingle() * ScreenH);
            _world.Add(agent);
            _predators.Add(agent);
        }

        // --- Seed food ---
        for (int i = 0; i < 8; i++)
            SpawnFood();

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _sb      = new SpriteBatch(GraphicsDevice);
        _circles = new CircleRenderer(GraphicsDevice);
        // Font is optional — comment out if you don't have a Content folder set up
        // _font = Content.Load<SpriteFont>("Arial");
    }

    protected override void Update(GameTime gt)
    {
        var kb = Keyboard.GetState();
        if (kb.IsKeyDown(Keys.Escape)) Exit();

        // Click to add food
        var ms = Mouse.GetState();
        if (ms.LeftButton == ButtonState.Pressed)
            _food.Add(new Vector2(ms.X, ms.Y));

        // Randomly spawn food occasionally
        if (_rng.NextSingle() < 0.01f)
            SpawnFood();

        float dt = (float)gt.ElapsedGameTime.TotalSeconds;

        // --- Update perception for all agents ---
        UpdatePerception();

        // --- Tick the Dominatus world ---
        _world.Tick(dt);

        // --- Integrate positions from velocity ---
        IntegratePositions(dt);

        // --- Food collection ---
        CollectFood();

        base.Update(gt);
    }

    protected override void Draw(GameTime gt)
    {
        GraphicsDevice.Clear(new Color(0.04f, 0.08f, 0.15f));

        _sb.Begin(blendState: BlendState.AlphaBlend);

        // Draw food
        foreach (var f in _food)
            _circles.Draw(_sb, f.X, f.Y, FoodRadius, new Color(0.9f, 0.9f, 0.3f));

        // Draw prey
        foreach (var a in _prey)
        {
            var x  = a.Bb.GetOrDefault(FishKeys.PosX, 0f);
            var y  = a.Bb.GetOrDefault(FishKeys.PosY, 0f);
            var r  = a.Bb.GetOrDefault(FishKeys.Radius, 8f);
            var cr = a.Bb.GetOrDefault(FishKeys.ColorR, 0.3f);
            var cg = a.Bb.GetOrDefault(FishKeys.ColorG, 0.6f);
            var cb = a.Bb.GetOrDefault(FishKeys.ColorB, 1.0f);
            _circles.Draw(_sb, x, y, r, new Color(cr, cg, cb));
        }

        // Draw predators
        foreach (var a in _predators)
        {
            var x = a.Bb.GetOrDefault(FishKeys.PosX, 0f);
            var y = a.Bb.GetOrDefault(FishKeys.PosY, 0f);
            var r = a.Bb.GetOrDefault(FishKeys.Radius, 14f);
            _circles.Draw(_sb, x, y, r, new Color(0.9f, 0.1f, 0.1f));

            // Draw detection radius (faint)
            _circles.Draw(_sb, x, y, PredDetectPreyDist,
                new Color(0.9f, 0.1f, 0.1f, 0.04f));
        }

        _sb.End();

        base.Draw(gt);
    }

    protected override void UnloadContent()
    {
        _circles.Dispose();
        base.UnloadContent();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void SpawnFood()
    {
        _food.Add(new Vector2(
            _rng.NextSingle() * ScreenW,
            _rng.NextSingle() * ScreenH));
    }

    private void UpdatePerception()
    {
        // --- Prey perception: find nearest food and nearest predator ---
        foreach (var prey in _prey)
        {
            var px = prey.Bb.GetOrDefault(FishKeys.PosX, 0f);
            var py = prey.Bb.GetOrDefault(FishKeys.PosY, 0f);

            // Nearest food
            var bestFoodDist = float.MaxValue;
            var bestFoodX    = 0f;
            var bestFoodY    = 0f;
            foreach (var f in _food)
            {
                var d = Dist(px, py, f.X, f.Y);
                if (d < bestFoodDist) { bestFoodDist = d; bestFoodX = f.X; bestFoodY = f.Y; }
            }

            prey.Bb.Set(FishKeys.FoodVisible,   bestFoodDist < PreyDetectFoodDist);
            prey.Bb.Set(FishKeys.NearestFoodX,  bestFoodX);
            prey.Bb.Set(FishKeys.NearestFoodY,  bestFoodY);

            // Nearest predator
            var bestPredDist = float.MaxValue;
            var bestPredX    = 0f;
            var bestPredY    = 0f;
            foreach (var pred in _predators)
            {
                var predX = pred.Bb.GetOrDefault(FishKeys.PosX, 0f);
                var predY = pred.Bb.GetOrDefault(FishKeys.PosY, 0f);
                var d     = Dist(px, py, predX, predY);
                if (d < bestPredDist) { bestPredDist = d; bestPredX = predX; bestPredY = predY; }
            }

            prey.Bb.Set(FishKeys.PredatorNearby, bestPredDist < PreyDetectPredDist);
            prey.Bb.Set(FishKeys.NearestPredX,   bestPredX);
            prey.Bb.Set(FishKeys.NearestPredY,   bestPredY);
        }

        // --- Predator perception: find nearest prey ---
        foreach (var pred in _predators)
        {
            var px = pred.Bb.GetOrDefault(FishKeys.PosX, 0f);
            var py = pred.Bb.GetOrDefault(FishKeys.PosY, 0f);

            var bestDist = float.MaxValue;
            var bestX    = 0f;
            var bestY    = 0f;
            foreach (var prey in _prey)
            {
                var preyX = prey.Bb.GetOrDefault(FishKeys.PosX, 0f);
                var preyY = prey.Bb.GetOrDefault(FishKeys.PosY, 0f);
                var d     = Dist(px, py, preyX, preyY);
                if (d < bestDist) { bestDist = d; bestX = preyX; bestY = preyY; }
            }

            pred.Bb.Set(FishKeys.FoodVisible,  bestDist < PredDetectPreyDist);
            pred.Bb.Set(FishKeys.NearestFoodX, bestX);
            pred.Bb.Set(FishKeys.NearestFoodY, bestY);
        }
    }

    private void IntegratePositions(float dt)
    {
        foreach (var agent in _world.Agents)
        {
            var x  = agent.Bb.GetOrDefault(FishKeys.PosX, 0f);
            var y  = agent.Bb.GetOrDefault(FishKeys.PosY, 0f);
            var vx = agent.Bb.GetOrDefault(FishKeys.VelX, 0f);
            var vy = agent.Bb.GetOrDefault(FishKeys.VelY, 0f);

            x += vx * dt;
            y += vy * dt;

            // Wrap around screen edges
            if (x < 0)      { x += ScreenW; }
            if (x > ScreenW){ x -= ScreenW; }
            if (y < 0)      { y += ScreenH; }
            if (y > ScreenH){ y -= ScreenH; }

            agent.Bb.Set(FishKeys.PosX, x);
            agent.Bb.Set(FishKeys.PosY, y);
        }
    }

    private void CollectFood()
    {
        for (int i = _food.Count - 1; i >= 0; i--)
        {
            foreach (var prey in _prey)
            {
                var px = prey.Bb.GetOrDefault(FishKeys.PosX, 0f);
                var py = prey.Bb.GetOrDefault(FishKeys.PosY, 0f);
                if (Dist(px, py, _food[i].X, _food[i].Y) < 12f)
                {
                    _food.RemoveAt(i);
                    // Respawn somewhere else
                    SpawnFood();
                    break;
                }
            }
        }
    }

    private static float Dist(float ax, float ay, float bx, float by)
    {
        var dx = ax - bx;
        var dy = ay - by;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
