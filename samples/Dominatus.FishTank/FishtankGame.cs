using Dominatus.Core.Runtime;
using Dominatus.UtilityLite;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Dominatus.Fishtank;

public sealed class FishtankGame : Game
{
    // ------- config -------
    private const int ScreenW = 1280;
    private const int ScreenH = 720;
    private const int PreyCount = 15;
    private const int PredCount = 2;
    private const float FoodRadius = 6f;
    private const float PreyDetectPredDist = 120f;
    private const float PredDetectPreyDist = 180f;
    private const float PreyDetectFoodDist = 150f;

    // ------- MonoGame -------
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _sb = null!;
    private CircleRenderer _circles = null!;
    private SpriteFont? _font;

    // ------- Dominatus -------
    private AiWorld _world = null!;
    private readonly List<AiAgent> _prey = new();
    private readonly List<AiAgent> _predators = new();

    // ------- food pellets -------
    private readonly List<Vector2> _food = new();
    private readonly Random _rng = new();

    public FishtankGame()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = ScreenW,
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
                r: 8f, cr: c.r, cg: c.g, cb: c.b);
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
        _sb = new SpriteBatch(GraphicsDevice);
        _circles = new CircleRenderer(GraphicsDevice);
        // Font is optional — comment out if you don't have a Content folder set up
        // _font = Content.Load<SpriteFont>("Arial");
    }

    protected override void Update(GameTime gt)
    {
        var kb = Keyboard.GetState();
        if (kb.IsKeyDown(Keys.Escape)) Exit();

        // Click to add food, but only if the click is inside the playable area.
        var ms = Mouse.GetState();
        if (ms.LeftButton == ButtonState.Pressed && IsInsidePlayableBounds(ms.X, ms.Y))
            _food.Add(new Vector2(
                ClampFoodX(ms.X),
                ClampFoodY(ms.Y)));

        // Utility-controlled ambient spawning.
        // Few pellets => spawn more aggressively.
        // Many pellets => slow down or stop.
        var spawnChance = GetAmbientFoodSpawnChance();
        if (_rng.NextSingle() < spawnChance)
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
            var x = a.Bb.GetOrDefault(FishKeys.PosX, 0f);
            var y = a.Bb.GetOrDefault(FishKeys.PosY, 0f);
            var r = a.Bb.GetOrDefault(FishKeys.Radius, 8f);
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
            ClampFoodX(_rng.NextSingle() * ScreenW),
            ClampFoodY(_rng.NextSingle() * ScreenH)));
    }

    private void UpdatePerception()
    {
        const float SeparationRadius = 28f;
        const float FoodOffsetRadius = 6f;

        // --- Prey perception: nearest food, nearest predator, and local separation ---
        foreach (var prey in _prey)
        {
            var px = prey.Bb.GetOrDefault(FishKeys.PosX, 0f);
            var py = prey.Bb.GetOrDefault(FishKeys.PosY, 0f);
            var pr = prey.Bb.GetOrDefault(FishKeys.Radius, 8f);

            // Local separation from nearby prey
            float sepX = 0f;
            float sepY = 0f;

            foreach (var other in _prey)
            {
                if (ReferenceEquals(prey, other))
                    continue;

                var ox = other.Bb.GetOrDefault(FishKeys.PosX, 0f);
                var oy = other.Bb.GetOrDefault(FishKeys.PosY, 0f);
                var oradius = other.Bb.GetOrDefault(FishKeys.Radius, 8f);

                var dx = px - ox;
                var dy = py - oy;
                var d = MathF.Sqrt(dx * dx + dy * dy);

                var desiredSpace = pr + oradius + SeparationRadius;

                if (d > 0.001f && d < desiredSpace)
                {
                    // Stronger repulsion when closer; fades out with distance.
                    var strength = (desiredSpace - d) / desiredSpace;
                    sepX += (dx / d) * strength;
                    sepY += (dy / d) * strength;
                }
            }

            prey.Bb.Set(FishKeys.SeparationX, sepX);
            prey.Bb.Set(FishKeys.SeparationY, sepY);

            // Nearest food, but with a stable per-fish offset around the pellet
            var bestFoodDist = float.MaxValue;
            var bestFoodX = 0f;
            var bestFoodY = 0f;

            foreach (var f in _food)
            {
                var d = Dist(px, py, f.X, f.Y);
                if (d < bestFoodDist)
                {
                    bestFoodDist = d;

                    var offsetAngle = prey.Bb.GetOrDefault(FishKeys.FoodOffsetAngle, 0f);
                    bestFoodX = f.X + MathF.Cos(offsetAngle) * FoodOffsetRadius;
                    bestFoodY = f.Y + MathF.Sin(offsetAngle) * FoodOffsetRadius;
                }
            }

            prey.Bb.Set(FishKeys.FoodVisible, bestFoodDist < PreyDetectFoodDist);
            prey.Bb.Set(FishKeys.NearestFoodX, bestFoodX);
            prey.Bb.Set(FishKeys.NearestFoodY, bestFoodY);

            // Nearest predator
            var bestPredDist = float.MaxValue;
            var bestPredX = 0f;
            var bestPredY = 0f;
            foreach (var pred in _predators)
            {
                var predX = pred.Bb.GetOrDefault(FishKeys.PosX, 0f);
                var predY = pred.Bb.GetOrDefault(FishKeys.PosY, 0f);
                var d = Dist(px, py, predX, predY);
                if (d < bestPredDist)
                {
                    bestPredDist = d;
                    bestPredX = predX;
                    bestPredY = predY;
                }
            }

            prey.Bb.Set(FishKeys.PredatorNearby, bestPredDist < PreyDetectPredDist);
            prey.Bb.Set(FishKeys.NearestPredX, bestPredX);
            prey.Bb.Set(FishKeys.NearestPredY, bestPredY);
        }

        // --- Predator perception: find nearest prey ---
        foreach (var pred in _predators)
        {
            var px = pred.Bb.GetOrDefault(FishKeys.PosX, 0f);
            var py = pred.Bb.GetOrDefault(FishKeys.PosY, 0f);

            var bestDist = float.MaxValue;
            var bestX = 0f;
            var bestY = 0f;
            foreach (var prey in _prey)
            {
                var preyX = prey.Bb.GetOrDefault(FishKeys.PosX, 0f);
                var preyY = prey.Bb.GetOrDefault(FishKeys.PosY, 0f);
                var d = Dist(px, py, preyX, preyY);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestX = preyX;
                    bestY = preyY;
                }
            }

            pred.Bb.Set(FishKeys.FoodVisible, bestDist < PredDetectPreyDist);
            pred.Bb.Set(FishKeys.NearestFoodX, bestX);
            pred.Bb.Set(FishKeys.NearestFoodY, bestY);
        }
    }

    private void IntegratePositions(float dt)
    {
        // Steering responsiveness: how quickly actual velocity tracks desired velocity.
        // Lower = more sluggish/organic, higher = snappier.
        const float SteerLerp = 6f;

        foreach (var agent in _world.Agents)
        {
            var vx = agent.Bb.GetOrDefault(FishKeys.VelX, 0f);
            var vy = agent.Bb.GetOrDefault(FishKeys.VelY, 0f);
            var dvx = agent.Bb.GetOrDefault(FishKeys.DesiredVelX, vx);
            var dvy = agent.Bb.GetOrDefault(FishKeys.DesiredVelY, vy);

            // Lerp actual velocity toward desired — gives smooth organic turning
            var t = Math.Clamp(SteerLerp * dt, 0f, 1f);
            vx = vx + (dvx - vx) * t;
            vy = vy + (dvy - vy) * t;

            agent.Bb.Set(FishKeys.VelX, vx);
            agent.Bb.Set(FishKeys.VelY, vy);

            var x = agent.Bb.GetOrDefault(FishKeys.PosX, 0f) + vx * dt;
            var y = agent.Bb.GetOrDefault(FishKeys.PosY, 0f) + vy * dt;

            // Wrap around screen edges
            if (x < 0) x += ScreenW;
            if (x > ScreenW) x -= ScreenW;
            if (y < 0) y += ScreenH;
            if (y > ScreenH) y -= ScreenH;

            agent.Bb.Set(FishKeys.PosX, x);
            agent.Bb.Set(FishKeys.PosY, y);
        }

        // Hard prey-prey overlap resolution pass.
        // This is presentation-preserving hygiene: even if steering wants the same spot,
        // the circles should never remain fused together.
        const float VelocityDamp = 0.90f;

        for (int i = 0; i < _prey.Count; i++)
        {
            var a = _prey[i];
            var ax = a.Bb.GetOrDefault(FishKeys.PosX, 0f);
            var ay = a.Bb.GetOrDefault(FishKeys.PosY, 0f);
            var ar = a.Bb.GetOrDefault(FishKeys.Radius, 8f);

            for (int j = i + 1; j < _prey.Count; j++)
            {
                var b = _prey[j];
                var bx = b.Bb.GetOrDefault(FishKeys.PosX, 0f);
                var by = b.Bb.GetOrDefault(FishKeys.PosY, 0f);
                var br = b.Bb.GetOrDefault(FishKeys.Radius, 8f);

                var dx = bx - ax;
                var dy = by - ay;
                var distSq = dx * dx + dy * dy;
                var minDist = ar + br;

                if (distSq >= minDist * minDist)
                    continue;

                float dist;
                float nx;
                float ny;

                if (distSq > 0.0001f)
                {
                    dist = MathF.Sqrt(distSq);
                    nx = dx / dist;
                    ny = dy / dist;
                }
                else
                {
                    // Perfect or near-perfect overlap: choose a deterministic fallback axis.
                    dist = 0f;
                    nx = 1f;
                    ny = 0f;
                }

                var penetration = minDist - dist;
                var push = penetration * 0.5f;

                ax -= nx * push;
                ay -= ny * push;
                bx += nx * push;
                by += ny * push;

                // Wrap corrected positions too
                if (ax < 0) ax += ScreenW;
                if (ax > ScreenW) ax -= ScreenW;
                if (ay < 0) ay += ScreenH;
                if (ay > ScreenH) ay -= ScreenH;

                if (bx < 0) bx += ScreenW;
                if (bx > ScreenW) bx -= ScreenW;
                if (by < 0) by += ScreenH;
                if (by > ScreenH) by -= ScreenH;

                a.Bb.Set(FishKeys.PosX, ax);
                a.Bb.Set(FishKeys.PosY, ay);
                b.Bb.Set(FishKeys.PosX, bx);
                b.Bb.Set(FishKeys.PosY, by);

                // Slightly damp velocities so they don't sit there buzzing in place.
                var avx = a.Bb.GetOrDefault(FishKeys.VelX, 0f) * VelocityDamp;
                var avy = a.Bb.GetOrDefault(FishKeys.VelY, 0f) * VelocityDamp;
                var bvx = b.Bb.GetOrDefault(FishKeys.VelX, 0f) * VelocityDamp;
                var bvy = b.Bb.GetOrDefault(FishKeys.VelY, 0f) * VelocityDamp;

                a.Bb.Set(FishKeys.VelX, avx);
                a.Bb.Set(FishKeys.VelY, avy);
                b.Bb.Set(FishKeys.VelX, bvx);
                b.Bb.Set(FishKeys.VelY, bvy);
            }
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

    private static bool IsInsidePlayableBounds(int x, int y)
    {
        return x >= 0 && x < ScreenW && y >= 0 && y < ScreenH;
    }

    private static float ClampFoodX(float x)
    {
        return Math.Clamp(x, FoodRadius, ScreenW - FoodRadius);
    }

    private static float ClampFoodY(float y)
    {
        return Math.Clamp(y, FoodRadius, ScreenH - FoodRadius);
    }

    private float GetAmbientFoodSpawnChance()
    {
        // Hard safety cap so the screen can never spiral into a yellow apocalypse.
        const int HardCap = 80;
        if (_food.Count >= HardCap)
            return 0f;

        // We use UtilityLite as a tiny discrete controller:
        // pick the highest-utility spawn band from current pellet count.
        //
        // Bands:
        // - Starving  : very few pellets, spawn aggressively
        // - Low       : somewhat low, spawn moderately
        // - Balanced  : healthy amount, spawn lightly
        // - Flooded   : too many, stop spawning
        //
        // These utility surfaces are intentionally simple and readable.
        var starving = When.Score((_, _) =>
        {
            if (_food.Count <= 8) return 1.0f;
            if (_food.Count <= 14) return 0.6f;
            return 0.0f;
        }).Eval(_world, _prey.Count > 0 ? _prey[0] : _predators[0]);

        var low = When.Score((_, _) =>
        {
            if (_food.Count >= 6 && _food.Count <= 18) return 0.85f;
            if (_food.Count <= 24) return 0.35f;
            return 0.0f;
        }).Eval(_world, _prey.Count > 0 ? _prey[0] : _predators[0]);

        var balanced = When.Score((_, _) =>
        {
            if (_food.Count >= 16 && _food.Count <= 36) return 0.75f;
            if (_food.Count <= 44) return 0.25f;
            return 0.0f;
        }).Eval(_world, _prey.Count > 0 ? _prey[0] : _predators[0]);

        var flooded = When.Score((_, _) =>
        {
            if (_food.Count >= 45) return 1.0f;
            if (_food.Count >= 36) return 0.5f;
            return 0.0f;
        }).Eval(_world, _prey.Count > 0 ? _prey[0] : _predators[0]);

        // Winner-takes-band. This is a deliberately simple utility controller.
        if (starving >= low && starving >= balanced && starving >= flooded)
            return 0.08f;

        if (low >= balanced && low >= flooded)
            return 0.025f;

        if (balanced >= flooded)
            return 0.006f;

        return 0.0f;
    }
}