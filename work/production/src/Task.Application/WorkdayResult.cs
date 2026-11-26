namespace Task.Application;

public sealed record WorkdayResult(int Completed, int Planned);

public interface IWorkdayReadStore
{
    System.Threading.Tasks.Task<WorkdayResult?> ReadDayAsync(Guid organization, Guid actor, TaskTodayWindow day,
        DateTimeOffset cutoff, CancellationToken token);
}
