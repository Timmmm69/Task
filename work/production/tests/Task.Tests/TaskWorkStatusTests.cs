using Task.Domain;

namespace Task.Tests;

public sealed class TaskWorkStatusTests
{
    [Fact]
    public void ContainsExactlyTheFiveMvpValuesInOrder()
    {
        var names = Enum.GetNames<TaskWorkStatus>();
        var values = Enum.GetValues<TaskWorkStatus>();

        Assert.Equal(5, names.Length);
        Assert.Equal(new[] { "New", "InProgress", "Review", "Completed", "Cancelled" }, names);
        Assert.Equal(5, values.Length);
    }

    [Fact]
    public void Values_HaveTheFixedNumbers()
    {
        Assert.Equal(0, (int)TaskWorkStatus.New);
        Assert.Equal(1, (int)TaskWorkStatus.InProgress);
        Assert.Equal(2, (int)TaskWorkStatus.Review);
        Assert.Equal(3, (int)TaskWorkStatus.Completed);
        Assert.Equal(4, (int)TaskWorkStatus.Cancelled);
    }

    [Fact]
    public void DoesNotContainOverdueStatus()
    {
        Assert.DoesNotContain("Overdue", Enum.GetNames<TaskWorkStatus>());
        Assert.False(Enum.IsDefined(typeof(TaskWorkStatus), "Overdue"));
        Assert.False(Enum.IsDefined(typeof(TaskWorkStatus), 5));
        Assert.False(Enum.IsDefined(typeof(TaskWorkStatus), -1));
    }
}