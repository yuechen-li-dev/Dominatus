using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.OptFlow;

/// <summary>Persistence contract for an operation site. M4 supports pending-only durability.</summary>
public enum OperationPersistenceKind
{
    PendingOnly,
    /// <summary>Reserved for a future explicit run/generation protocol; not supported in M4.</summary>
    DurablePrimitiveResult
}

public enum OperationValidationCode
{
    BlankSiteId,
    SiteIdTooLong,
    InvalidSiteIdCharacter,
    ReservedPrefix,
    UnsupportedDurableResultType,
    InvalidPersistenceKind
}

public sealed record OperationValidationDiagnostic(OperationValidationCode Code, string Message);
public sealed record OperationValidationReport(IReadOnlyList<OperationValidationDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.Count == 0;
}

public sealed class OperationValidationException : ArgumentException
{
    public OperationValidationReport Report { get; }
    public OperationValidationException(OperationValidationReport report)
        : base(string.Join(" ", report.Diagnostics.Select(x => x.Message))) => Report = report;
}

/// <summary>An explicit, authored, ordinal and patch-stable durable operation-site identity.</summary>
public readonly record struct OperationSiteId
{
    public const int MaxLength = 128;
    public string Value { get; }

    public OperationSiteId(string value)
    {
        var report = Validate(value);
        if (!report.IsValid) throw new OperationValidationException(report);
        Value = value;
    }

    public override string ToString() => Value;
    public static implicit operator OperationSiteId(string value) => new(value);

    public static OperationValidationReport Validate(string? value)
    {
        var diagnostics = new List<OperationValidationDiagnostic>();
        if (string.IsNullOrWhiteSpace(value))
            diagnostics.Add(new(OperationValidationCode.BlankSiteId, "Operation site id must not be blank or whitespace."));
        else
        {
            if (value.Length > MaxLength)
                diagnostics.Add(new(OperationValidationCode.SiteIdTooLong, $"Operation site id must be at most {MaxLength} characters."));
            if (value.StartsWith("__op.", StringComparison.Ordinal))
                diagnostics.Add(new(OperationValidationCode.ReservedPrefix, "Operation site id must not begin with the reserved prefix '__op.'."));
            if (!char.IsAsciiLetterOrDigit(value[0]) || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '-')))
                diagnostics.Add(new(OperationValidationCode.InvalidSiteIdCharacter, "Operation site id must start with an ASCII letter or digit and may then contain only ASCII letters, digits, '.', '_', ':', and '-'."));
        }
        return new(diagnostics);
    }
}

public sealed record OperationGeneratedKeyInspection(string Purpose, string Name);

public sealed record OperationSiteInspection(
    OperationSiteId SiteId,
    OperationPersistenceKind PersistenceKind,
    Type? CommandType,
    Type? ResultType,
    IReadOnlyList<OperationGeneratedKeyInspection> GeneratedKeys,
    OperationValidationReport Validation)
{
    public bool IsPatchStable => Validation.IsValid;
    public bool ResultCachingSupported => false;
    public int GeneratedStateCount => 0;
    public bool ConcurrentUseSupported => false;
}

/// <summary>A reusable descriptor for one authored external-actuation obligation.</summary>
public class OperationSite
{
    internal OperationSite(OperationSiteId id, OperationPersistenceKind persistence)
    {
        Id = id;
        PersistenceKind = persistence;
        EnsurePendingOnly(persistence);
    }

    public OperationSiteId Id { get; }
    public OperationPersistenceKind PersistenceKind { get; }
    internal BbKey<bool> StartedKey => new($"__op.{Id.Value}.started");
    internal BbKey<long> PendingIdKey => new($"__op.{Id.Value}.pendingId");

    public OperationSiteInspection Inspect(Type? commandType = null) => Inspection(commandType, null);
    internal OperationSiteInspection Inspection(Type? commandType, Type? resultType) => new(
        Id, PersistenceKind, commandType, resultType,
        [new("started", StartedKey.Name), new("pendingId", PendingIdKey.Name)],
        OperationSiteId.Validate(Id.Value));

    internal static void EnsurePendingOnly(OperationPersistenceKind persistence)
    {
        if (persistence == OperationPersistenceKind.PendingOnly) return;
        var code = Enum.IsDefined(persistence) ? OperationValidationCode.UnsupportedDurableResultType : OperationValidationCode.InvalidPersistenceKind;
        throw new OperationValidationException(new([new(code, "M4 supports pending-only operation sites. Durable completed-result caching requires an explicit future run/generation protocol.")]));
    }
}

public sealed class OperationSite<TResult> : OperationSite
{
    internal OperationSite(OperationSiteId id, OperationPersistenceKind persistence) : base(id, persistence) { }
    public new OperationSiteInspection Inspect(Type? commandType = null) => Inspection(commandType, typeof(TResult));
}

public static class Operation
{
    public static OperationSite Site(string id, OperationPersistenceKind persistence = OperationPersistenceKind.PendingOnly)
        => new(new OperationSiteId(id), persistence);
    public static OperationSite<TResult> Site<TResult>(string id, OperationPersistenceKind persistence = OperationPersistenceKind.PendingOnly)
        => new(new OperationSiteId(id), persistence);
}

public enum OperationPhase { Dispatch, Completion, Payload, Conflict }

public abstract class OperationException : InvalidOperationException
{
    protected OperationException(OperationSiteId siteId, Type commandType, long? actuationId, OperationPhase phase, string message)
        : base(message)
    { SiteId = siteId; CommandType = commandType; ActuationId = actuationId; Phase = phase; }
    public OperationSiteId SiteId { get; }
    public Type CommandType { get; }
    public long? ActuationId { get; }
    public OperationPhase Phase { get; }
}
public sealed class OperationDispatchException : OperationException
{ internal OperationDispatchException(OperationSiteId s, Type c, long id, string? e) : base(s, c, id, OperationPhase.Dispatch, $"Operation '{s}' dispatch was rejected: {Bound(e)}") { } internal static string Bound(string? value) => string.IsNullOrWhiteSpace(value) ? "no error supplied" : value.Length <= 256 ? value : value[..256]; }
public sealed class OperationCompletionException : OperationException
{ internal OperationCompletionException(OperationSiteId s, Type c, long id, string? e) : base(s, c, id, OperationPhase.Completion, $"Operation '{s}' completed unsuccessfully: {OperationDispatchException.Bound(e)}") { } }
public sealed class OperationPayloadException : OperationException
{ internal OperationPayloadException(OperationSiteId s, Type c, long id, Type resultType) : base(s, c, id, OperationPhase.Payload, $"Operation '{s}' completed without the expected {resultType.Name} payload.") { } }

internal sealed record OperationStep(OperationSite Site, IActuationCommand Command) : AiStep, IWaitEvent
{
    public bool TryConsume(AiCtx ctx, ref EventCursor cursor)
    {
        var started = ctx.Bb.GetOrDefault(Site.StartedKey, false);
        if (!started)
        {
            var dispatch = ctx.Act.Dispatch(ctx, Command);
            ctx.Bb.Set(Site.PendingIdKey, dispatch.Id.Value);
            ctx.Bb.Set(Site.StartedKey, true);
            if (!dispatch.Accepted) { Clear(ctx); throw new OperationDispatchException(Site.Id, Command.GetType(), dispatch.Id.Value, dispatch.Error); }
        }
        var id = ctx.Bb.GetOrDefault(Site.PendingIdKey, 0L);
        if (!ctx.Events.TryConsume(ref cursor, (ActuationCompleted e) => e.Id.Value == id, out var completed)) return false;
        Clear(ctx);
        if (!completed.Ok) throw new OperationCompletionException(Site.Id, Command.GetType(), id, completed.Error);
        return true;
    }
    private void Clear(AiCtx ctx) { ctx.Bb.Set(Site.StartedKey, false); ctx.Bb.Set(Site.PendingIdKey, 0L); }
}

internal sealed record OperationResultStep<TResult>(OperationSite<TResult> Site, IActuationCommand Command, BbKey<TResult> StoreAs) : AiStep, IWaitEvent
{
    public bool TryConsume(AiCtx ctx, ref EventCursor cursor)
    {
        if (!ctx.Bb.GetOrDefault(Site.StartedKey, false))
        {
            var dispatch = ctx.Act.Dispatch(ctx, Command);
            ctx.Bb.Set(Site.PendingIdKey, dispatch.Id.Value);
            ctx.Bb.Set(Site.StartedKey, true);
            if (!dispatch.Accepted) { Clear(ctx); throw new OperationDispatchException(Site.Id, Command.GetType(), dispatch.Id.Value, dispatch.Error); }
        }
        var id = ctx.Bb.GetOrDefault(Site.PendingIdKey, 0L);
        if (!ctx.Events.TryConsume(ref cursor, (ActuationCompleted<TResult> e) => e.Id.Value == id, out var completed)) return false;
        Clear(ctx);
        if (!completed.Ok) throw new OperationCompletionException(Site.Id, Command.GetType(), id, completed.Error);
        if (completed.Payload is not TResult payload) throw new OperationPayloadException(Site.Id, Command.GetType(), id, typeof(TResult));
        ctx.Bb.Set(StoreAs, payload);
        return true;
    }
    private void Clear(AiCtx ctx) { ctx.Bb.Set(Site.StartedKey, false); ctx.Bb.Set(Site.PendingIdKey, 0L); }
}
