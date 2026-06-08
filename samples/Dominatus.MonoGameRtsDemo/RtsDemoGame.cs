using Dominatus.MonoGameConn;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Dominatus.MonoGameRtsDemo;

public sealed class RtsDemoGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly RtsDemoOptions _options;
    private RtsDemoSimulation _simulation = null!;
    private DominatusGameComponent _dominatus = null!;
    private SpriteBatch _spriteBatch = null!;
    private Texture2D _pixel = null!;
    private KeyboardState _previousKeyboard;
    private bool _showDebug;
    private float _speed = 1f;
    private double _titleTimer;

    public RtsDemoGame(RtsDemoOptions options)
    {
        _options = options;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = RtsDemoSimulation.WorldWidth,
            PreferredBackBufferHeight = RtsDemoSimulation.WorldHeight
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = "Dominatus RTS Demo";
    }

    protected override void Initialize()
    {
        _simulation = RtsDemoSimulation.Create(_options.Ships);
        _dominatus = new DominatusGameComponent(this, _simulation.World) { TimeScale = _speed };
        Components.Add(_dominatus);
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);
        base.LoadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        var keyboard = Keyboard.GetState();
        if (keyboard.IsKeyDown(Keys.Escape))
            Exit();

        if (WasPressed(keyboard, Keys.Space))
            _dominatus.IsPaused = !_dominatus.IsPaused;
        if (WasPressed(keyboard, Keys.R))
            ResetSimulation();
        if (WasPressed(keyboard, Keys.D))
            _showDebug = !_showDebug;
        if (WasPressed(keyboard, Keys.D1))
            SetSpeed(0.5f);
        if (WasPressed(keyboard, Keys.D2))
            SetSpeed(1f);
        if (WasPressed(keyboard, Keys.D3))
            SetSpeed(2f);

        _simulation.UpdatePerception();
        base.Update(gameTime);
        if (!_dominatus.IsPaused)
            _simulation.ResolveActions(_dominatus.LastDeltaSeconds);

        UpdateTitle(gameTime);
        _previousKeyboard = keyboard;
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(10, 14, 24));
        _spriteBatch.Begin(blendState: BlendState.AlphaBlend);

        DrawBattleLine();
        DrawLasers();
        foreach (var ship in _simulation.Ships)
            DrawShip(ship);

        if (_showDebug)
            DrawDebugMarkers();

        _spriteBatch.End();
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _pixel.Dispose();
        _spriteBatch.Dispose();
        base.UnloadContent();
    }

    private void DrawBattleLine()
    {
        DrawRect(new Rectangle(RtsDemoSimulation.WorldWidth / 2 - 1, 0, 2, RtsDemoSimulation.WorldHeight), new Color(60, 70, 90, 120));
    }

    private void DrawLasers()
    {
        foreach (var ship in _simulation.Ships)
        {
            if (!ship.Alive || !ship.FiredThisFrame || ship.LaserTargetPos is not { } target)
                continue;

            var color = ship.Faction == RtsFaction.Dominion ? Color.Cyan : new Color(255, 120, 40);
            DrawLine(ship.Position, target, color, ship.Class is ShipClass.Carrier or ShipClass.HiveArk or ShipClass.CommandCruiser ? 3f : 2f);
        }
    }

    private void DrawShip(ShipVisualState ship)
    {
        if (!ship.Alive)
            return;

        var action = ship.Agent.Bb.GetOrDefault(RtsDemoKeys.CurrentAction, "Idle");
        var baseColor = ship.Faction == RtsFaction.Dominion ? new Color(70, 150, 255) : new Color(255, 90, 80);
        var hullScale = MathHelper.Clamp(ship.HullFraction, 0.25f, 1f);
        var color = new Color(baseColor.ToVector3() * (0.45f + hullScale * 0.55f));
        var outline = ship.Faction == RtsFaction.Dominion ? new Color(150, 220, 255, 190) : new Color(255, 170, 100, 190);
        var x = (int)MathF.Round(ship.Position.X);
        var y = (int)MathF.Round(ship.Position.Y);
        var size = ClassSize(ship, hullScale);

        DrawShipShape(ship.Class, x, y, size, color, outline);

        if (action == "Attack" && ship.TargetId is not null)
            DrawRect(new Rectangle(x - 2, y - 2, 4, 4), Color.White);
        else if (action == "Retreat")
            DrawRect(new Rectangle(x - size / 2, y - size / 2 - 5, size, 2), Color.Yellow);
    }

    private void DrawShipShape(ShipClass shipClass, int x, int y, int size, Color color, Color outline)
    {
        switch (shipClass)
        {
            case ShipClass.ScoutFrigate:
                DrawCenteredRect(x, y, size + 8, Math.Max(3, size / 3), color);
                DrawCenteredRect(x + size / 3, y, 3, size / 2, outline);
                break;
            case ShipClass.MissileCorvette:
                DrawCenteredRect(x, y, size, size / 2, color);
                DrawCenteredRect(x, y, size / 2, size, color);
                break;
            case ShipClass.RailgunDestroyer:
                DrawCenteredRect(x, y, size + 10, Math.Max(5, size / 2), color);
                DrawCenteredRect(x + size / 2, y, 6, 3, outline);
                break;
            case ShipClass.Carrier:
                DrawCenteredRect(x, y, size + 12, size, color);
                DrawCenteredRect(x, y, size + 4, 4, outline);
                break;
            case ShipClass.RepairTender:
                DrawCenteredRect(x, y, size, size, color);
                DrawCenteredRect(x - size / 2, y - size / 2, 4, 4, outline);
                DrawCenteredRect(x + size / 2, y - size / 2, 4, 4, outline);
                DrawCenteredRect(x - size / 2, y + size / 2, 4, 4, outline);
                DrawCenteredRect(x + size / 2, y + size / 2, 4, 4, outline);
                break;
            case ShipClass.CommandCruiser:
                DrawCenteredRect(x, y, size + 8, size / 2, color);
                DrawCenteredRect(x, y, size / 2, size + 8, color);
                DrawCenteredRect(x, y, size / 2, size / 2, outline);
                break;
            case ShipClass.NeedleDrone:
                DrawCenteredRect(x, y, Math.Max(5, size / 2), Math.Max(5, size / 2), color);
                break;
            case ShipClass.SporeFrigate:
                DrawCenteredRect(x, y, size, size, color);
                DrawCenteredRect(x - size / 3, y + size / 4, size / 2, size / 2, color);
                DrawCenteredRect(x + size / 3, y - size / 4, size / 2, size / 2, outline);
                break;
            case ShipClass.SynapseCruiser:
                DrawCenteredRect(x, y, size + 4, size, color);
                DrawCenteredRect(x, y, size, size + 4, color);
                DrawCenteredRect(x, y, size / 2, size / 2, outline);
                break;
            case ShipClass.Regenerator:
                DrawCenteredRect(x, y, size / 2, size + 6, color);
                DrawCenteredRect(x, y, size + 6, size / 2, color);
                DrawCenteredRect(x, y, Math.Max(4, size / 3), Math.Max(4, size / 3), outline);
                break;
            case ShipClass.Harvester:
                DrawCenteredRect(x, y, size + 4, size, color);
                DrawCenteredRect(x - size / 2, y, size / 2, size / 2, outline);
                DrawCenteredRect(x + size / 2, y, size / 2, size / 2, outline);
                break;
            case ShipClass.HiveArk:
                DrawCenteredRect(x, y, size + 10, size + 4, color);
                DrawCenteredRect(x - size / 3, y - size / 3, size / 2, size / 2, outline);
                DrawCenteredRect(x + size / 3, y + size / 3, size / 2, size / 2, outline);
                DrawCenteredRect(x, y, size / 2, size / 2, color);
                break;
        }
    }

    private static int ClassSize(ShipVisualState ship, float hullScale)
    {
        var baseSize = ship.Class switch
        {
            ShipClass.NeedleDrone => 10,
            ShipClass.ScoutFrigate => 12,
            ShipClass.MissileCorvette or ShipClass.SporeFrigate => 14,
            ShipClass.RailgunDestroyer or ShipClass.RepairTender or ShipClass.Regenerator or ShipClass.Harvester => 17,
            ShipClass.Carrier or ShipClass.CommandCruiser or ShipClass.SynapseCruiser => 21,
            ShipClass.HiveArk => 25,
            _ => 14
        };

        return Math.Max(5, (int)MathF.Round(baseSize * (0.82f + hullScale * 0.18f)));
    }

    private void DrawDebugMarkers()
    {
        foreach (var label in DebugAgentOverlay.BuildLabels(_simulation.World.Agents))
        {
            var x = (int)label.Position.X;
            var y = (int)label.Position.Y;
            DrawRect(new Rectangle(x - 3, y - 3, 6, 6), Color.White);
        }
    }

    private void DrawRect(Rectangle rectangle, Color color) => _spriteBatch.Draw(_pixel, rectangle, color);

    private void DrawCenteredRect(int x, int y, int width, int height, Color color)
        => DrawRect(new Rectangle(x - width / 2, y - height / 2, Math.Max(1, width), Math.Max(1, height)), color);

    private void DrawLine(Vector2 start, Vector2 end, Color color, float thickness)
    {
        var delta = end - start;
        var length = delta.Length();
        if (length <= 0.01f)
            return;

        var angle = MathF.Atan2(delta.Y, delta.X);
        _spriteBatch.Draw(_pixel, start, null, color, angle, Vector2.Zero, new Vector2(length, thickness), SpriteEffects.None, 0f);
    }

    private void ResetSimulation()
    {
        Components.Remove(_dominatus);
        _simulation.Reset();
        _dominatus = new DominatusGameComponent(this, _simulation.World)
        {
            TimeScale = _speed
        };
        Components.Add(_dominatus);
    }

    private void SetSpeed(float speed)
    {
        _speed = speed;
        _dominatus.TimeScale = speed;
    }

    private void UpdateTitle(GameTime gameTime)
    {
        _titleTimer += gameTime.ElapsedGameTime.TotalSeconds;
        if (_titleTimer < 0.25)
            return;

        _titleTimer = 0;
        Window.Title = $"Dominatus RTS Demo - D:{_simulation.DominionAlive} C:{_simulation.CollectiveAlive} Ships:{_simulation.Ships.Count} Speed:{_speed:0.#}x Paused:{_dominatus.IsPaused} Debug:{_showDebug} | Space pause R reset 1/2/3 speed D debug Esc exit";
    }

    private bool WasPressed(KeyboardState keyboard, Keys key) => keyboard.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key);
}
