namespace Valyze.Domain.Exceptions;

public static class AccountGuard
{
    public static T EnforceSingle<T>(T entity, Guid expectedAccountId, Func<T, Guid> accountSelector)
        where T : class
    {
        var actual = accountSelector(entity);
        if (actual != expectedAccountId)
            throw new BusinessException(
                "msnAccountIsolationViolation",
                $"Entity belongs to account {actual} but expected {expectedAccountId}.");
        return entity;
    }

    public static IEnumerable<T> EnforceMany<T>(
        IEnumerable<T> entities,
        Guid expectedAccountId,
        Func<T, Guid> accountSelector)
        where T : class
    {
        foreach (var entity in entities)
        {
            yield return EnforceSingle(entity, expectedAccountId, accountSelector);
        }
    }
}
