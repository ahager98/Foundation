using System.Text.RegularExpressions;

namespace Foundation.Exceptions;

/// <summary>
/// Base for client-facing domain exceptions. A client-visible error is described only by structured,
/// non-PII fields (entity type, optional id or index, reason code) and never by interpolated free text,
/// so the exception handler cannot echo user-supplied names or notes into a ProblemDetails response.
/// The free-text <see cref="Exception.Message"/> constructors remain for server-side logs only; the handler
/// never renders them.
/// </summary>
public abstract partial class DomainException : Exception
{
    protected DomainException()
    {
    }

    protected DomainException(string message)
        : base(message)
    {
    }

    protected DomainException(string message, Exception inner)
        : base(message, inner)
    {
    }

    protected DomainException(string entityType, string? entityId, string reasonCode)
        : base(BuildLogMessage(
            entityType ?? throw new ArgumentNullException(nameof(entityType)),
            entityId,
            reasonCode ?? throw new ArgumentNullException(nameof(reasonCode))))
    {
        EntityType = entityType;
        EntityId = entityId;
        ReasonCode = reasonCode;
    }

    /// <summary>Type of the entity concerned, e.g. "Person". Code identifier, never user text.</summary>
    public string? EntityType { get; }

    /// <summary>Identifier or index of the entity concerned. Never a name or other user text.</summary>
    public string? EntityId { get; }

    /// <summary>Stable machine-readable reason, e.g. "not_found".</summary>
    public string? ReasonCode { get; }

    /// <summary>
    /// Client-safe description built only from the structured fields. Null when no entity type or reason code was
    /// supplied, or when either is not in its strict shape. An <see cref="EntityId"/> that is not a database key (digits
    /// or a GUID) is omitted from the description. Unsafe fields are dropped, not escaped, because they may carry free text.
    /// </summary>
    public string? ClientDetail
    {
        get
        {
            if (!IsSafeType(EntityType) || !IsSafeReason(ReasonCode))
                return null;

            return IsSafeId(EntityId)
                ? $"{EntityType} {EntityId}: {ReasonCode}"
                : $"{EntityType}: {ReasonCode}";
        }
    }

    /// <summary>The <see cref="ReasonCode"/> when it is a lower_snake_case code, otherwise null. Safe to return to clients as a machine-readable reason.</summary>
    public string? ClientReasonCode => IsSafeReason(ReasonCode) ? ReasonCode : null;

    // Shapes, not content checks: a type is a code identifier, a reason is lower_snake_case, an id is a database key
    // (digits or a GUID). Dates, dashed numbers and dotted names do not fit any of them. A digits-only string still
    // fits an id, so callers must pass keys from their own database, never a value a person typed.
    private static bool IsSafeType(string? v) => v is not null && SafeType().IsMatch(v);

    private static bool IsSafeReason(string? v) => v is not null && SafeReason().IsMatch(v);

    private static bool IsSafeId(string? v) => v is not null && (NumericId().IsMatch(v) || GuidId().IsMatch(v));

    private static string BuildLogMessage(string entityType, string? entityId, string reasonCode) =>
        entityId is null ? $"{entityType}: {reasonCode}" : $"{entityType} {entityId}: {reasonCode}";

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]{0,63}\z")]
    private static partial Regex SafeType();

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,63}\z")]
    private static partial Regex SafeReason();

    [GeneratedRegex(@"^[0-9]{1,18}\z")]
    private static partial Regex NumericId();

    [GeneratedRegex(@"^[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\z")]
    private static partial Regex GuidId();
}
