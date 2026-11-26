namespace Task.Application;

public sealed record WeeklyCompletion(string Title, DateTimeOffset CompletedAt);
public sealed record WorkweekResults(int Total, IReadOnlyList<WeeklyCompletion> Items);

public interface IWorkweekReadStore
{
    System.Threading.Tasks.Task<WorkweekResults> ReadWeekAsync(Guid organization, Guid actor,
        DateTimeOffset from, DateTimeOffset through, CancellationToken token);
}
