using Dominatus.Core.Runtime;
using Dominatus.Fishtank;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace Dominatus.FishTank.Tests;

public sealed class FishTankDiagnosticsTests
{
    [Fact]
    public void FishTank_Diagnostic_Run_Writes_Log_Artifact()
    {
        var log = new StringBuilder();

        void Write(string s)
        {
            log.AppendLine(s);
        }

        Write("=== Dominatus FishTank Diagnostic ===");
        Write($"UTC: {DateTime.UtcNow:O}");
        Write("");

        // --------------------------------------------------------------------
        // Build host/world exactly like the game does
        // --------------------------------------------------------------------
        var host = new ActuatorHost();
        host.Register(new SetVelocityHandler());
        host.Register(new SteerTowardHandler());
        host.Register(new SteerAwayHandler());
        host.Register(new WanderHandler());

        var world = new AiWorld(host);

        // One prey only for a clean diagnosis
        var prey = FishFactory.CreatePrey(
            x: 100f,
            y: 100f,
            r: 8f,
            cr: 0.3f,
            cg: 0.6f,
            cb: 1.0f);

        world.Add(prey);

        // --------------------------------------------------------------------
        // Baseline snapshot
        // --------------------------------------------------------------------
        var initialVelX = prey.Bb.GetOrDefault(FishKeys.VelX, 0f);
        var initialVelY = prey.Bb.GetOrDefault(FishKeys.VelY, 0f);
        var initialDesiredVelX = prey.Bb.GetOrDefault(FishKeys.DesiredVelX, 0f);
        var initialDesiredVelY = prey.Bb.GetOrDefault(FishKeys.DesiredVelY, 0f);
        var initialWanderAngle = prey.Bb.GetOrDefault(FishKeys.WanderAngle, 0f);

        Write("Initial state:");
        Write($"  Vel           = ({initialVelX:F4}, {initialVelY:F4})");
        Write($"  DesiredVel    = ({initialDesiredVelX:F4}, {initialDesiredVelY:F4})");
        Write($"  WanderAngle   = {initialWanderAngle:F4}");
        Write("");

        // --------------------------------------------------------------------
        // Phase 1: no perception at all, force wander fallback conditions
        // If wander is truly running, DesiredVel and/or WanderAngle should drift.
        // --------------------------------------------------------------------
        Write("=== Phase 1: Wander fallback, no perception ===");

        prey.Bb.Set(FishKeys.FoodVisible, false);
        prey.Bb.Set(FishKeys.PredatorNearby, false);
        prey.Bb.Set(FishKeys.NearestFoodX, 0f);
        prey.Bb.Set(FishKeys.NearestFoodY, 0f);
        prey.Bb.Set(FishKeys.NearestPredX, 0f);
        prey.Bb.Set(FishKeys.NearestPredY, 0f);

        const float dt = 0.1f;

        float? firstChangedDesiredTick = null;
        float? firstChangedAngleTick = null;

        var baselineDesiredX = prey.Bb.GetOrDefault(FishKeys.DesiredVelX, 0f);
        var baselineDesiredY = prey.Bb.GetOrDefault(FishKeys.DesiredVelY, 0f);
        var baselineAngle = prey.Bb.GetOrDefault(FishKeys.WanderAngle, 0f);

        for (int i = 0; i < 30; i++)
        {
            world.Tick(dt);

            var dvx = prey.Bb.GetOrDefault(FishKeys.DesiredVelX, 0f);
            var dvy = prey.Bb.GetOrDefault(FishKeys.DesiredVelY, 0f);
            var ang = prey.Bb.GetOrDefault(FishKeys.WanderAngle, 0f);

            var desiredChanged =
                MathF.Abs(dvx - baselineDesiredX) > 0.0001f ||
                MathF.Abs(dvy - baselineDesiredY) > 0.0001f;

            var angleChanged =
                MathF.Abs(ang - baselineAngle) > 0.0001f;

            if (desiredChanged && firstChangedDesiredTick is null)
                firstChangedDesiredTick = i * dt;

            if (angleChanged && firstChangedAngleTick is null)
                firstChangedAngleTick = i * dt;

            Write(
                $"Tick {i:00} | " +
                $"DesiredVel=({dvx:F4},{dvy:F4}) | " +
                $"WanderAngle={ang:F4} | " +
                $"FoodVisible={prey.Bb.GetOrDefault(FishKeys.FoodVisible, false)} | " +
                $"PredatorNearby={prey.Bb.GetOrDefault(FishKeys.PredatorNearby, false)}");
        }

        Write("");
        Write("Phase 1 summary:");
        Write($"  First DesiredVel change observed at t = {(firstChangedDesiredTick?.ToString("F2") ?? "NEVER")}");
        Write($"  First WanderAngle change observed at t = {(firstChangedAngleTick?.ToString("F2") ?? "NEVER")}");
        Write("");

        // --------------------------------------------------------------------
        // Phase 2: simulate SeekFood conditions
        // If decision switching works, DesiredVel should steer toward +X direction.
        // --------------------------------------------------------------------
        Write("=== Phase 2: Force SeekFood ===");

        prey.Bb.Set(FishKeys.FoodVisible, true);
        prey.Bb.Set(FishKeys.PredatorNearby, false);
        prey.Bb.Set(FishKeys.NearestFoodX, 500f);
        prey.Bb.Set(FishKeys.NearestFoodY, 100f); // straight right from current-ish area

        var beforeSeekX = prey.Bb.GetOrDefault(FishKeys.DesiredVelX, 0f);
        var beforeSeekY = prey.Bb.GetOrDefault(FishKeys.DesiredVelY, 0f);

        for (int i = 0; i < 10; i++)
        {
            world.Tick(dt);

            var dvx = prey.Bb.GetOrDefault(FishKeys.DesiredVelX, 0f);
            var dvy = prey.Bb.GetOrDefault(FishKeys.DesiredVelY, 0f);

            Write(
                $"Seek Tick {i:00} | " +
                $"DesiredVel=({dvx:F4},{dvy:F4}) | " +
                $"NearestFood=(500.0000,100.0000)");
        }

        var afterSeekX = prey.Bb.GetOrDefault(FishKeys.DesiredVelX, 0f);
        var afterSeekY = prey.Bb.GetOrDefault(FishKeys.DesiredVelY, 0f);

        Write("");
        Write("Phase 2 summary:");
        Write($"  Before Seek DesiredVel = ({beforeSeekX:F4}, {beforeSeekY:F4})");
        Write($"  After  Seek DesiredVel = ({afterSeekX:F4}, {afterSeekY:F4})");
        Write("");

        // --------------------------------------------------------------------
        // Phase 3: simulate Flee conditions
        // If decision switching works, DesiredVel should steer away from predator.
        // --------------------------------------------------------------------
        Write("=== Phase 3: Force Flee ===");

        prey.Bb.Set(FishKeys.FoodVisible, true);          // both on, but Flee should win
        prey.Bb.Set(FishKeys.PredatorNearby, true);
        prey.Bb.Set(FishKeys.NearestPredX, 120f);
        prey.Bb.Set(FishKeys.NearestPredY, 100f);

        var beforeFleeX = prey.Bb.GetOrDefault(FishKeys.DesiredVelX, 0f);
        var beforeFleeY = prey.Bb.GetOrDefault(FishKeys.DesiredVelY, 0f);

        for (int i = 0; i < 10; i++)
        {
            world.Tick(dt);

            var dvx = prey.Bb.GetOrDefault(FishKeys.DesiredVelX, 0f);
            var dvy = prey.Bb.GetOrDefault(FishKeys.DesiredVelY, 0f);

            Write(
                $"Flee Tick {i:00} | " +
                $"DesiredVel=({dvx:F4},{dvy:F4}) | " +
                $"NearestPred=(120.0000,100.0000)");
        }

        var afterFleeX = prey.Bb.GetOrDefault(FishKeys.DesiredVelX, 0f);
        var afterFleeY = prey.Bb.GetOrDefault(FishKeys.DesiredVelY, 0f);

        Write("");
        Write("Phase 3 summary:");
        Write($"  Before Flee DesiredVel = ({beforeFleeX:F4}, {beforeFleeY:F4})");
        Write($"  After  Flee DesiredVel = ({afterFleeX:F4}, {afterFleeY:F4})");
        Write("");

        // --------------------------------------------------------------------
        // Coarse interpretation block
        // --------------------------------------------------------------------
        Write("=== Interpretation ===");

        var wanderAppearsDead =
            firstChangedDesiredTick is null &&
            firstChangedAngleTick is null;

        if (wanderAppearsDead)
        {
            Write("Wander appears NOT to be executing.");
            Write("This strongly suggests a problem in HFSM activation, Ai.Decide transition, or actuation dispatch.");
        }
        else
        {
            Write("Wander appears to be executing.");
            Write("That makes a pure 'perception never fired' explanation more plausible for gameplay issues.");
        }

        var seekDidSomething =
            MathF.Abs(afterSeekX - beforeSeekX) > 0.0001f ||
            MathF.Abs(afterSeekY - beforeSeekY) > 0.0001f;

        var fleeDidSomething =
            MathF.Abs(afterFleeX - beforeFleeX) > 0.0001f ||
            MathF.Abs(afterFleeY - beforeFleeY) > 0.0001f;

        Write($"Seek phase changed DesiredVel: {seekDidSomething}");
        Write($"Flee phase changed DesiredVel: {fleeDidSomething}");

        if (!seekDidSomething && !fleeDidSomething)
        {
            Write("Decision switching also appears dead.");
            Write("That points even harder at Root/Decide/HFSM semantics rather than perception math.");
        }

        // --------------------------------------------------------------------
        // Write artifact
        // --------------------------------------------------------------------
        var outDir = Path.Combine(AppContext.BaseDirectory, "diagnostics");
        Directory.CreateDirectory(outDir);

        var outPath = Path.Combine(outDir, "fishtank-diagnostic-log.txt");
        File.WriteAllText(outPath, log.ToString());

        // Keep test green if the file was produced.
        Assert.True(File.Exists(outPath), $"Expected diagnostic log at: {outPath}");
    }
}