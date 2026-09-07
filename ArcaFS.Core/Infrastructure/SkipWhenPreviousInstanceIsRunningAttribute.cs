using Hangfire.Common;
using Hangfire.States;

namespace ArcaFS.Infrastructure;

/// <summary>
/// Drops an enqueued job when an identical one is already processing. Index scans are the main
/// consumer: a user hammering "rescan" would otherwise queue up duplicate walks of the same tree.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SkipWhenPreviousInstanceIsRunningAttribute : JobFilterAttribute, IElectStateFilter
{
    public void OnStateElection(ElectStateContext context)
    {
        if (context.CandidateState is not EnqueuedState)
        {
            return;
        }

        string fingerprint = context.BackgroundJob.Job.ToString();
        var monitoringApi = context.Storage.GetMonitoringApi();
        long processingCount = monitoringApi.ProcessingCount();
        if (processingCount == 0)
        {
            return;
        }

        var processing = monitoringApi.ProcessingJobs(0, (int)processingCount);
        bool isAlreadyRunning = processing.Any(pair => pair.Value?.Job?.ToString() == fingerprint);

        if (isAlreadyRunning)
        {
            context.CandidateState = new DeletedState
            {
                Reason = "Skipped because another instance is already running.",
            };
        }
    }
}
