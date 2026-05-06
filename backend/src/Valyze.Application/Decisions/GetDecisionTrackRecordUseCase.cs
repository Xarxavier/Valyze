using Microsoft.Extensions.Options;
using Valyze.Domain.Application.Decisions;
using Valyze.Domain.Decisions;
using Valyze.Domain.Enum;
using Valyze.Domain.Exceptions;
using Valyze.Domain.QueryService;

namespace Valyze.Application.Decisions;

public class GetDecisionTrackRecordUseCase : IGetDecisionTrackRecordUseCase
{
    private readonly IInvestmentDecisionQueryService _queryService;
    private readonly DecisionEvaluationOptions _options;

    public GetDecisionTrackRecordUseCase(
        IInvestmentDecisionQueryService queryService,
        IOptions<DecisionEvaluationOptions> options)
    {
        _queryService = queryService;
        _options = options.Value;
    }

    public async Task<DecisionTrackRecord> ExecuteAsync(
        Guid accountId,
        DecisionSource? sourceFilter,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
            throw new BusinessException("msnDecisionAccountIdRequired");

        var rows = await _queryService.GetTrackRecordAsync(
            accountId,
            sourceFilter,
            _options.AchievementThreshold,
            cancellationToken);

        return new DecisionTrackRecord(rows);
    }
}
