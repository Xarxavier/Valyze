namespace Valyze.Domain.Enum;

public enum DecisionSource : short
{
    AiRecommendation = 1,
    UserOwnAnalysis = 2,
    ExternalNews = 3,
    ThirdPartyTip = 4,
    Other = 5,
}
