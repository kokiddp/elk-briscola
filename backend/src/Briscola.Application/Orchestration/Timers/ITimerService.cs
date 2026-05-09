namespace Briscola.Application.Orchestration.Timers;

public interface ITimerService
{
    IDisposable ScheduleAt(DateTimeOffset at, Func<CancellationToken, ValueTask> callback);
}
