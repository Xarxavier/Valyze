namespace Valyze.Domain.Exceptions;

public class BusinessException : Exception
{
    public string? Detail { get; }

    public BusinessException(string message) : base(message) { }

    public BusinessException(string message, string detail) : base(message)
    {
        Detail = detail;
    }
}
