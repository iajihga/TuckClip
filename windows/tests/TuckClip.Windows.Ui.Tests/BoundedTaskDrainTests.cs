using TuckClip.Windows.Services;

namespace TuckClip.Windows.Ui.Tests;

[TestClass]
public sealed class BoundedTaskDrainTests
{
    [TestMethod]
    public async Task WaitUntilIdleAsyncReturnsTrueAfterPendingWorkCompletes()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new List<Task> { completion.Task };
        _ = Task.Run(async () =>
        {
            await Task.Delay(10);
            completion.TrySetResult();
        });

        var drained = await BoundedTaskDrain.WaitUntilIdleAsync(
            () => tasks,
            TimeSpan.FromSeconds(1));

        Assert.IsTrue(drained);
    }

    [TestMethod]
    public async Task WaitUntilIdleAsyncReturnsFalseAtDeadline()
    {
        var neverCompletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var drained = await BoundedTaskDrain.WaitUntilIdleAsync(
            () => [neverCompletes.Task],
            TimeSpan.FromMilliseconds(25));

        Assert.IsFalse(drained);
    }

    [TestMethod]
    public async Task WaitUntilIdleAsyncContinuesAfterFaultedWork()
    {
        var tasks = new List<Task> { Task.FromException(new IOException("expected test failure")) };

        var drained = await BoundedTaskDrain.WaitUntilIdleAsync(
            () => tasks,
            TimeSpan.FromSeconds(1));

        Assert.IsTrue(drained);
    }
}
