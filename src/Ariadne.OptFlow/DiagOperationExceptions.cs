using Dominatus.Core.Runtime;

namespace Ariadne.OptFlow;

public abstract class DiagOperationException : InvalidOperationException
{
    protected DiagOperationException(
        string message,
        string callsiteId,
        ActuationId actuationId,
        string? actuatorError = null)
        : base(message)
    {
        CallsiteId = callsiteId;
        ActuationId = actuationId;
        ActuatorError = actuatorError;
    }

    public string CallsiteId { get; }

    public ActuationId ActuationId { get; }

    public string? ActuatorError { get; }
}

public sealed class DiagDispatchException : DiagOperationException
{
    public DiagDispatchException(string callsiteId, ActuationId actuationId, string? actuatorError)
        : base(
            $"Dialogue operation '{callsiteId}' was rejected by the actuator: {actuatorError ?? "no error supplied"}.",
            callsiteId,
            actuationId,
            actuatorError)
    {
    }
}

public sealed class DiagCompletionException : DiagOperationException
{
    public DiagCompletionException(string callsiteId, ActuationId actuationId, string? actuatorError)
        : base(
            $"Dialogue operation '{callsiteId}' failed: {actuatorError ?? "no error supplied"}.",
            callsiteId,
            actuationId,
            actuatorError)
    {
    }
}

public sealed class DiagPayloadException : DiagOperationException
{
    public DiagPayloadException(string callsiteId, ActuationId actuationId, string expectedPayload)
        : base(
            $"Dialogue operation '{callsiteId}' completed without the required {expectedPayload} payload.",
            callsiteId,
            actuationId)
    {
        ExpectedPayload = expectedPayload;
    }

    public string ExpectedPayload { get; }
}
