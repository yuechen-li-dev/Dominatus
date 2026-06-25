using Dominatus.Core.Runtime;
using Godot;

namespace Dominatus.GodotConn.Actuation;

/// <summary>
/// Shared navigation-aware 2D movement handler for worlds hosting multiple agents.
/// Dominatus provides target intent while Godot NavigationAgent2D handles path following.
/// </summary>
public sealed class RegisteredNavigationMove2DActuationHandler : GodotActuationHandler<NavigationMove2DCommand>
{
    private const float DefaultResponsiveness = 10f;
    private const float TargetEpsilon = 0.25f;
    private readonly Dictionary<AgentId, Registration> _registrations = new();

    public RegisteredNavigationMove2DActuationHandler(Node owner)
        : base(owner)
    {
    }

    public void Bind(AgentId agentId, CharacterBody2D body, NavigationAgent2D navigationAgent, float responsiveness = DefaultResponsiveness)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(navigationAgent);

        _registrations[agentId] = new Registration(body, navigationAgent, MathF.Max(1f, responsiveness));
    }

    public bool Unbind(AgentId agentId) => _registrations.Remove(agentId);

    public bool TryGetStateSnapshot(AgentId agentId, out NavigationMove2DStateSnapshot snapshot)
    {
        if (_registrations.TryGetValue(agentId, out var registration))
        {
            snapshot = registration.CreateSnapshot();
            return true;
        }

        snapshot = default;
        return false;
    }

    public void Advance(float deltaSeconds)
    {
        if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds))
            return;

        foreach (var registration in _registrations.Values)
            Advance(registration, deltaSeconds);
    }

    public override ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, NavigationMove2DCommand cmd)
    {
        if (!_registrations.TryGetValue(ctx.Agent.Id, out var registration))
            return ActuatorHost.HandlerResult.CompletedFailure($"No navigation target registered for agent {ctx.Agent.Id.Value}.");

        registration.TargetPosition = cmd.TargetPosition;
        registration.Speed = MathF.Max(0f, cmd.Speed);
        registration.ArrivalRadius = MathF.Max(1f, cmd.ArrivalRadius);
        registration.SlowdownRadius = MathF.Max(registration.ArrivalRadius, cmd.SlowdownRadius);
        registration.StopOnArrival = cmd.StopOnArrival;
        registration.HasCommand = true;
        registration.NavigationFinished = false;
        registration.NavigationActive = true;
        registration.ObservedNavigationActive |= registration.Speed > 0f;

        registration.NavigationAgent.TargetDesiredDistance = registration.ArrivalRadius;
        registration.NavigationAgent.PathDesiredDistance = MathF.Max(4f, registration.ArrivalRadius * 0.6f);
        registration.NavigationAgent.MaxSpeed = MathF.Max(registration.Speed, 1f);

        if (registration.NavigationAgent.TargetPosition.DistanceSquaredTo(cmd.TargetPosition) > TargetEpsilon)
            registration.NavigationAgent.TargetPosition = cmd.TargetPosition;

        return ActuatorHost.HandlerResult.CompletedOk();
    }

    private static void Advance(Registration registration, float deltaSeconds)
    {
        var body = registration.Body;
        var navigationAgent = registration.NavigationAgent;

        if (!registration.HasCommand)
        {
            DecayVelocity(registration, deltaSeconds);
            body.MoveAndSlide();
            registration.NavigationActive = false;
            registration.NavigationFinished = true;
            registration.LastNextPathPosition = body.GlobalPosition;
            return;
        }

        var distanceToTarget = body.GlobalPosition.DistanceTo(registration.TargetPosition);
        registration.DistanceToTarget = distanceToTarget;

        if (navigationAgent.IsNavigationFinished() || distanceToTarget <= registration.ArrivalRadius)
        {
            registration.NavigationActive = false;
            registration.NavigationFinished = true;
            registration.HasCommand = false;
            registration.LastNextPathPosition = registration.TargetPosition;
            DecayVelocity(registration, deltaSeconds);
            body.MoveAndSlide();
            return;
        }

        var nextPathPosition = navigationAgent.GetNextPathPosition();
        registration.LastNextPathPosition = nextPathPosition;

        var desiredVelocity = ComputeDesiredVelocity(registration, nextPathPosition, distanceToTarget);
        var smoothingT = 1f - MathF.Exp(-registration.Responsiveness * deltaSeconds);
        registration.Velocity = registration.Velocity.Lerp(desiredVelocity, smoothingT);

        if (registration.Speed > 0f && registration.Velocity.Length() > registration.Speed)
            registration.Velocity = registration.Velocity.Normalized() * registration.Speed;

        body.Velocity = registration.Velocity;
        body.MoveAndSlide();
        registration.NavigationActive = registration.Velocity.LengthSquared() > 0.0025f;
        registration.NavigationFinished = false;
        registration.ObservedNavigationActive |= registration.NavigationActive;
        registration.DistanceToTarget = body.GlobalPosition.DistanceTo(registration.TargetPosition);
    }

    private static Vector2 ComputeDesiredVelocity(Registration registration, Vector2 nextPathPosition, float distanceToTarget)
    {
        var toNext = nextPathPosition - registration.Body.GlobalPosition;
        if (toNext.LengthSquared() <= 0.0001f || registration.Speed <= 0f)
            return Vector2.Zero;

        var desiredSpeed = registration.Speed;
        if (distanceToTarget < registration.SlowdownRadius)
        {
            var denominator = MathF.Max(registration.SlowdownRadius - registration.ArrivalRadius, 0.001f);
            var slowdown = Math.Clamp((distanceToTarget - registration.ArrivalRadius) / denominator, 0f, 1f);
            desiredSpeed *= MathF.Max(0.25f, slowdown);
        }

        return toNext.Normalized() * desiredSpeed;
    }

    private static void DecayVelocity(Registration registration, float deltaSeconds)
    {
        var dampingT = 1f - MathF.Exp(-registration.Responsiveness * deltaSeconds);
        registration.Velocity = registration.Velocity.Lerp(Vector2.Zero, dampingT);
        if (registration.Velocity.LengthSquared() < 0.01f)
            registration.Velocity = Vector2.Zero;

        registration.Body.Velocity = registration.Velocity;
    }

    private sealed class Registration
    {
        public Registration(CharacterBody2D body, NavigationAgent2D navigationAgent, float responsiveness)
        {
            Body = body;
            NavigationAgent = navigationAgent;
            Responsiveness = responsiveness;
            ArrivalRadius = 16f;
            SlowdownRadius = 48f;
            TargetPosition = body.GlobalPosition;
            LastNextPathPosition = body.GlobalPosition;
            NavigationAgent.AvoidanceEnabled = false;
            NavigationAgent.MaxSpeed = 120f;
            NavigationAgent.TargetDesiredDistance = ArrivalRadius;
            NavigationAgent.PathDesiredDistance = MathF.Max(4f, ArrivalRadius * 0.6f);
        }

        public CharacterBody2D Body { get; }

        public NavigationAgent2D NavigationAgent { get; }

        public float Responsiveness { get; }

        public Vector2 TargetPosition { get; set; }

        public Vector2 LastNextPathPosition { get; set; }

        public Vector2 Velocity { get; set; }

        public float Speed { get; set; }

        public float ArrivalRadius { get; set; }

        public float SlowdownRadius { get; set; }

        public float DistanceToTarget { get; set; }

        public bool StopOnArrival { get; set; } = true;

        public bool HasCommand { get; set; }

        public bool NavigationActive { get; set; }

        public bool NavigationFinished { get; set; } = true;

        public bool ObservedNavigationActive { get; set; }

        public NavigationMove2DStateSnapshot CreateSnapshot()
            => new(
                TargetPosition,
                LastNextPathPosition,
                Velocity,
                Speed,
                ArrivalRadius,
                SlowdownRadius,
                DistanceToTarget,
                NavigationActive,
                NavigationFinished,
                ObservedNavigationActive);
    }
}
