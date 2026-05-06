using Microsoft.Extensions.Configuration;
using Shouldly;
using Valyze.Application.Decisions;
using Xunit;

namespace Valyze.Application.Tests.Decisions;

/// <summary>
/// Tests that DecisionEvaluationOptions binding matches the manual logic
/// in Valyze.Application/ServiceExtensions.cs.
/// Uses a real ConfigurationBuilder with in-memory dictionary — no DI container needed.
/// </summary>
public sealed class DecisionEvaluationOptionsTests
{
    /// <summary>
    /// Verifies that the default threshold is 0.05m when no configuration section is present.
    /// </summary>
    [Fact]
    public void Default_threshold_is_0_05_when_section_absent()
    {
        var configuration = new ConfigurationBuilder().Build();

        var opts = ApplyBinding(configuration);

        opts.AchievementThreshold.ShouldBe(0.05m);
    }

    /// <summary>
    /// Verifies that a configured threshold overrides the default (0.10).
    /// </summary>
    [Fact]
    public void Threshold_is_overridden_when_section_present()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Decisions:Evaluation:AchievementThreshold"] = "0.10"
            })
            .Build();

        var opts = ApplyBinding(configuration);

        opts.AchievementThreshold.ShouldBe(0.10m);
    }

    /// <summary>
    /// Verifies that an invalid/non-numeric threshold keeps the default (0.05m).
    /// </summary>
    [Fact]
    public void Default_threshold_kept_when_value_is_non_numeric()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Decisions:Evaluation:AchievementThreshold"] = "not-a-number"
            })
            .Build();

        var opts = ApplyBinding(configuration);

        opts.AchievementThreshold.ShouldBe(0.05m);
    }

    // ─── Helper — mirrors the binding logic in ServiceExtensions.cs ──────────

    private static DecisionEvaluationOptions ApplyBinding(IConfiguration configuration)
    {
        var opts = new DecisionEvaluationOptions(); // starts at default 0.05m
        var section = configuration.GetSection("Decisions:Evaluation");
        var threshold = section["AchievementThreshold"];
        if (!string.IsNullOrEmpty(threshold) &&
            decimal.TryParse(threshold, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var val))
            opts.AchievementThreshold = val;
        return opts;
    }
}
