using GE360.Trading.LeanAdapter;
using NUnit.Framework;

namespace GE360.Trading.Tests;

[TestFixture]
public sealed class LeanRuntimeSubmissionPolicyTests
{
    [Test]
    public void BacktestSubmissionIsAllowedWithoutLiveFlags()
    {
        var decision = LeanRuntimeSubmissionPolicy.Evaluate(
            liveMode: false,
            liveModeBrokerage: null,
            new LeanExecutionOptions());

        Assert.That(decision.Allowed, Is.True);
        Assert.That(decision.Code, Is.EqualTo("BACKTEST_MODE"));
    }

    [Test]
    public void PaperBrokerageRequiresExplicitPaperFlag()
    {
        var blocked = LeanRuntimeSubmissionPolicy.Evaluate(
            liveMode: true,
            liveModeBrokerage: "PaperBrokerage",
            new LeanExecutionOptions(
                EnableLiveSubmission: false,
                AllowPaperBrokerageSubmission: false));

        var allowed = LeanRuntimeSubmissionPolicy.Evaluate(
            liveMode: true,
            liveModeBrokerage: "PaperBrokerage",
            new LeanExecutionOptions(
                EnableLiveSubmission: false,
                AllowPaperBrokerageSubmission: true));

        Assert.That(blocked.Allowed, Is.False);
        Assert.That(blocked.Code, Is.EqualTo("PAPER_SUBMISSION_DISABLED"));
        Assert.That(allowed.Allowed, Is.True);
        Assert.That(allowed.Code, Is.EqualTo("LEAN_PAPER_BROKERAGE"));
    }

    [Test]
    public void PaperFlagCannotAuthorizeRealBroker()
    {
        var decision = LeanRuntimeSubmissionPolicy.Evaluate(
            liveMode: true,
            liveModeBrokerage: "InteractiveBrokersBrokerage",
            new LeanExecutionOptions(
                EnableLiveSubmission: false,
                AllowPaperBrokerageSubmission: true));

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.Code, Is.EqualTo("LIVE_SUBMISSION_DISABLED"));
    }

    [Test]
    public void MissingBrokerageNameFailsClosedInLiveMode()
    {
        var decision = LeanRuntimeSubmissionPolicy.Evaluate(
            liveMode: true,
            liveModeBrokerage: null,
            new LeanExecutionOptions(
                EnableLiveSubmission: false,
                AllowPaperBrokerageSubmission: true));

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.Code, Is.EqualTo("LIVE_SUBMISSION_DISABLED"));
    }
}
