using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.Tests;

public sealed class TaskDisplayCategoryTests
{
    [Theory]
    [InlineData(CodexTaskStatus.Running, TaskDisplayCategory.InProgress)]
    [InlineData(CodexTaskStatus.NeedsInput, TaskDisplayCategory.NeedsAttention)]
    [InlineData(CodexTaskStatus.Failed, TaskDisplayCategory.NeedsAttention)]
    [InlineData(CodexTaskStatus.Stale, TaskDisplayCategory.Hidden)]
    [InlineData(CodexTaskStatus.Completed, TaskDisplayCategory.RecentlyCompleted)]
    [InlineData(CodexTaskStatus.Aborted, TaskDisplayCategory.Hidden)]
    [InlineData(CodexTaskStatus.Idle, TaskDisplayCategory.Hidden)]
    public void MapsInternalCodexStatusToSingleUserFacingCategory(
        CodexTaskStatus status,
        TaskDisplayCategory expected)
    {
        Assert.Equal(expected, status.ToDisplayCategory());
    }
}
