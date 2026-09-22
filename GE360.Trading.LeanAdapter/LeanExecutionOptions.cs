namespace GE360.Trading.LeanAdapter;

public sealed record LeanExecutionOptions(
    bool EnableLiveSubmission = false,
    bool Asynchronous = true,
    bool BlockWhenMarketClosed = true);
