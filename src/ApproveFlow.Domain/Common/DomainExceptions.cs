namespace ApproveFlow.Domain.Common;

/// <summary>Base type for violations of business rules enforced by the domain model.</summary>
public abstract class DomainException : Exception
{
    protected DomainException(string code, string message) : base(message)
    {
        Code = code;
    }

    /// <summary>Stable, machine-readable error code (returned in API ProblemDetails).</summary>
    public string Code { get; }
}

/// <summary>A business rule was not satisfied (e.g. remarks too short, amount outside product range). HTTP 422.</summary>
public sealed class BusinessRuleViolationException : DomainException
{
    public BusinessRuleViolationException(string code, string message) : base(code, message)
    {
    }
}

/// <summary>The requested action is not valid for the record's current status (e.g. editing an approved application). HTTP 409.</summary>
public sealed class InvalidStatusTransitionException : DomainException
{
    public InvalidStatusTransitionException(string code, string message) : base(code, message)
    {
    }
}

/// <summary>Segregation-of-duties / four-eyes / ownership violation. HTTP 403.</summary>
public sealed class SegregationOfDutiesException : DomainException
{
    public SegregationOfDutiesException(string code, string message) : base(code, message)
    {
    }
}
