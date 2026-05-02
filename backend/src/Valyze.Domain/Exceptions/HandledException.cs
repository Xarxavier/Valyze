namespace Valyze.Domain.Exceptions;

public class HandledException : Exception
{
    public HandledException(string message) : base(message) { }
}
