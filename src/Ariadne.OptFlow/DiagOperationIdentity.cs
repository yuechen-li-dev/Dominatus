using Dominatus.Core.Blackboard;

namespace Ariadne.OptFlow;

/// <summary>Kind of authored dialogue operation.</summary>
public enum DiagOperationKind { Line, Ask, Choose }

/// <summary>Whether an operation has a patch-stable authored identity.</summary>
public enum DiagOperationIdentityKind { ExplicitStable, LegacySourceDerived }

public enum DiagOperationValidationCode { BlankId, IdTooLong, InvalidCharacter, ReservedPrefix, InvalidKind }

public sealed record DiagOperationValidationDiagnostic(DiagOperationValidationCode Code, string Message);

public sealed record DiagOperationValidationReport(IReadOnlyList<DiagOperationValidationDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.Count == 0;
}

public sealed class DiagOperationValidationException : ArgumentException
{
    public DiagOperationValidationReport Report { get; }

    public DiagOperationValidationException(DiagOperationValidationReport report)
        : base(string.Join(" ", report.Diagnostics.Select(d => d.Message))) => Report = report;
}

/// <summary>A validated, authored, ordinal dialogue-operation identity.</summary>
public readonly record struct DiagOperationId
{
    public const int MaxLength = 128;
    public string Value { get; }

    public DiagOperationId(string value)
    {
        var report = Validate(value);
        if (!report.IsValid) throw new DiagOperationValidationException(report);
        Value = value;
    }

    public override string ToString() => Value;
    public static implicit operator DiagOperationId(string value) => new(value);

    public static DiagOperationValidationReport Validate(string? value)
    {
        var diagnostics = new List<DiagOperationValidationDiagnostic>();
        if (string.IsNullOrWhiteSpace(value))
            diagnostics.Add(new(DiagOperationValidationCode.BlankId, "Dialogue operation id must not be blank or whitespace."));
        else
        {
            if (value.Length > MaxLength)
                diagnostics.Add(new(DiagOperationValidationCode.IdTooLong, $"Dialogue operation id must be at most {MaxLength} characters."));
            if (value.StartsWith("__diag.", StringComparison.Ordinal))
                diagnostics.Add(new(DiagOperationValidationCode.ReservedPrefix, "Dialogue operation id must not begin with the reserved prefix '__diag.'."));
            if (!char.IsAsciiLetterOrDigit(value[0]) || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '-')))
                diagnostics.Add(new(DiagOperationValidationCode.InvalidCharacter, "Dialogue operation id must start with an ASCII letter or digit and may then contain only ASCII letters, digits, '.', '_', ':', and '-'."));
        }
        return new(diagnostics);
    }
}

/// <summary>Pull-based, deterministic description of an operation's durable BB bookkeeping.</summary>
public sealed record DiagOperationInspection(
    string OperationId,
    DiagOperationKind Kind,
    DiagOperationIdentityKind IdentityKind,
    BbKey<bool> StartedKey,
    BbKey<long> PendingIdKey,
    BbKey<string>? StoreKey,
    DiagOperationValidationReport Validation)
{
    public bool IsPatchStable => IdentityKind == DiagOperationIdentityKind.ExplicitStable;
}
