namespace ApproveFlow.Application.Common.Exceptions;

/// <summary>Base type for application-layer failures that are mapped to user-facing messages / HTTP status codes.</summary>
public abstract class AppException : Exception
{
    protected AppException(string message) : base(message)
    {
    }

    protected AppException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>HTTP 404.</summary>
public sealed class NotFoundException : AppException
{
    public NotFoundException(string entityName, object key)
        : base($"{entityName} '{key}' was not found.")
    {
    }
}

/// <summary>HTTP 403 — authenticated but not permitted.</summary>
public sealed class ForbiddenAccessException : AppException
{
    public ForbiddenAccessException(string message) : base(message)
    {
    }
}

/// <summary>HTTP 409 — optimistic concurrency failure (RowVersion mismatch).</summary>
public sealed class ConcurrencyConflictException : AppException
{
    public const string DefaultMessage =
        "This record was modified by another user after you opened it. Reload the record to see the latest changes and try again.";

    public ConcurrencyConflictException() : base(DefaultMessage)
    {
    }

    public ConcurrencyConflictException(Exception innerException) : base(DefaultMessage, innerException)
    {
    }
}

/// <summary>HTTP 409 — request conflicts with existing data (e.g. duplicate reference, file already imported).</summary>
public sealed class ConflictException : AppException
{
    public ConflictException(string message) : base(message)
    {
    }
}

/// <summary>HTTP 422 — one or more input fields failed validation.</summary>
public sealed class ValidationException : AppException
{
    public ValidationException(IDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase);
    }

    public ValidationException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = new[] { message } })
    {
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
