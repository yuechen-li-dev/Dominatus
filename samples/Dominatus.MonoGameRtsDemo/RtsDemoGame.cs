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

    private void DrawShip(ShipVisualState ship)
    {
        if (!ship.Alive)
            return;

        var action = ship.Agent.Bb.GetOrDefault(RtsDemoKeys.CurrentAction, "Idle");
        var baseColor = ship.Faction == RtsFaction.Dominion ? new Color(70, 150, 255) : new Color(255, 90, 80);
        var hullScale = MathHelper.Clamp(ship.HullFraction, 0.25f, 1f);
        var color = new Color(baseColor.ToVector3() * (0.45f + hullScale * 0.55f));
        var size = (int)MathF.Round(8f + hullScale * 10f);
        var rect = new Rectangle((int)ship.Position.X - size / 2, (int)ship.Position.Y - size / 2, size, size);
        DrawRect(rect, color);

        if (action == "Attack" && ship.TargetId is not null)
            DrawRect(new Rectangle((int)ship.Position.X - 2, (int)ship.Position.Y - 2, 4, 4), Color.White);
        else if (action == "Retreat")
            DrawRect(new Rectangle((int)ship.Position.X - size / 2, (int)ship.Position.Y - size / 2 - 5, size, 2), Color.Yellow);
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
