using Task.Domain;

namespace Task.Tests;

public sealed class TaskPriorityTests
{
    [Fact]
    public void ContainsExactlyTheFourPriorityValuesInOrder()
    {
        var names = Enum.GetNames<TaskPriority>();
        var values = Enum.GetValues<TaskPriority>();

        Assert.Equal(4, names.Length);
        Assert.Equal(new[] { "Low", "Normal", "High", "Critical" }, names);
        Assert.Equal(4, values.Length);
    }

    [Fact]
    public void Values_HaveTheFixedNumbers()
    {
        Assert.Equal(0, (int)TaskPriority.Low);
        Assert.Equal(1, (int)TaskPriority.Normal);
        Assert.Equal(2, (int)TaskPriority.High);
        Assert.Equal(3, (int)TaskPriority.Critical);
    }
}

public sealed class TaskScheduleTests
{
    private static readonly DateTimeOffset UtcNoon =
        new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset UtcAfterNoon =
        new(2026, 8, 15, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_AcceptsNullStartAndNullDeadline()
    {
        var schedule = TaskSchedule.Create(startsAtUtc: null, deadlineUtc: null);

        Assert.Null(schedule.StartsAtUtc);
        Assert.Null(schedule.DeadlineUtc);
    }

    [Fact]
    public void Create_AcceptsNullStartWithDeadline()
    {
        var schedule = TaskSchedule.Create(startsAtUtc: null, deadlineUtc: UtcAfterNoon);

        Assert.Null(schedule.StartsAtUtc);
        Assert.Equal(UtcAfterNoon, schedule.DeadlineUtc);
    }

    [Fact]
    public void Create_AcceptsStartWithNullDeadline()
    {
        var schedule = TaskSchedule.Create(startsAtUtc: UtcNoon, deadlineUtc: null);

        Assert.Equal(UtcNoon, schedule.StartsAtUtc);
        Assert.Null(schedule.DeadlineUtc);
    }

    [Fact]
    public void Create_AcceptsDeadlineAfterStart()
    {
        var schedule = TaskSchedule.Create(UtcNoon, UtcAfterNoon);

        Assert.Equal(UtcNoon, schedule.StartsAtUtc);
        Assert.Equal(UtcAfterNoon, schedule.DeadlineUtc);
    }

    [Fact]
    public void Create_AcceptsDeadlineEqualToStart()
    {
        var schedule = TaskSchedule.Create(UtcNoon, UtcNoon);

        Assert.Equal(UtcNoon, schedule.StartsAtUtc);
        Assert.Equal(UtcNoon, schedule.DeadlineUtc);
    }

    [Fact]
    public void Create_RejectsDeadlineBeforeStart()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => TaskSchedule.Create(UtcAfterNoon, UtcNoon));

        Assert.Equal("deadlineUtc", exception.ParamName);
    }

    [Fact]
    public void Create_RejectsNonUtcStart()
    {
        var nonUtc = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(3));

        Assert.Throws<ArgumentException>(
            () => TaskSchedule.Create(startsAtUtc: nonUtc, deadlineUtc: null));
    }

    [Fact]
    public void Create_RejectsNonUtcDeadline()
    {
        var nonUtc = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(3));

        Assert.Throws<ArgumentException>(
            () => TaskSchedule.Create(startsAtUtc: null, deadlineUtc: nonUtc));
    }
}

public sealed class TaskOverduePolicyTests
{
    private static readonly DateTimeOffset UtcNoon =
        new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(TaskWorkStatus.New)]
    [InlineData(TaskWorkStatus.InProgress)]
    [InlineData(TaskWorkStatus.Review)]
    public void IsOverdue_NonTerminalStatuses_ReturnsTrue_WhenDeadlinePassed(TaskWorkStatus status)
    {
        var deadline = UtcNoon.AddHours(-1);

        Assert.True(TaskOverduePolicy.IsOverdue(status, deadline, UtcNoon));
    }

    [Theory]
    [InlineData(TaskWorkStatus.New)]
    [InlineData(TaskWorkStatus.InProgress)]
    [InlineData(TaskWorkStatus.Review)]
    public void IsOverdue_NonTerminalStatuses_ReturnsFalse_WhenDeadlineIsInTheFuture(TaskWorkStatus status)
    {
        var deadline = UtcNoon.AddHours(1);

        Assert.False(TaskOverduePolicy.IsOverdue(status, deadline, UtcNoon));
    }

    [Theory]
    [InlineData(TaskWorkStatus.Completed)]
    [InlineData(TaskWorkStatus.Cancelled)]
    public void IsOverdue_TerminalStatuses_AlwaysReturnsFalse_EvenAfterDeadline(TaskWorkStatus status)
    {
        var deadline = UtcNoon.AddHours(-1);

        Assert.False(TaskOverduePolicy.IsOverdue(status, deadline, UtcNoon));
    }

    [Theory]
    [InlineData(TaskWorkStatus.New)]
    [InlineData(TaskWorkStatus.InProgress)]
    [InlineData(TaskWorkStatus.Review)]
    public void IsOverdue_ReturnsFalse_WhenDeadlineEqualsNow(TaskWorkStatus status)
    {
        Assert.False(TaskOverduePolicy.IsOverdue(status, UtcNoon, UtcNoon));
    }

    [Theory]
    [InlineData(TaskWorkStatus.New)]
    [InlineData(TaskWorkStatus.InProgress)]
    [InlineData(TaskWorkStatus.Review)]
    [InlineData(TaskWorkStatus.Completed)]
    [InlineData(TaskWorkStatus.Cancelled)]
    public void IsOverdue_ReturnsFalse_WhenDeadlineIsNull(TaskWorkStatus status)
    {
        Assert.False(TaskOverduePolicy.IsOverdue(status, deadlineUtc: null, UtcNoon));
    }

    [Fact]
    public void IsOverdue_RejectsNonUtcNow()
    {
        var nowNonUtc = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(3));

        Assert.Throws<ArgumentException>(
            () => TaskOverduePolicy.IsOverdue(TaskWorkStatus.New, UtcNoon, nowNonUtc));
    }

    [Fact]
    public void IsOverdue_RejectsNonUtcDeadline()
    {
        var deadlineNonUtc = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(3));

        Assert.Throws<ArgumentException>(
            () => TaskOverduePolicy.IsOverdue(TaskWorkStatus.New, deadlineNonUtc, UtcNoon));
    }

    [Fact]
    public void IsOverdue_RejectsUnknownStatus()
    {
        var unknown = (TaskWorkStatus)99;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => TaskOverduePolicy.IsOverdue(unknown, UtcNoon, UtcNoon));
    }
}