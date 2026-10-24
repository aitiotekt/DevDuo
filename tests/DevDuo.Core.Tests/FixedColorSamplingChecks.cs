using DevDuo;

internal static class FixedColorSamplingChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var schedule = new FixedColorSampleSchedule();
        check(schedule.TryBegin(7, out var early), "Initial document colors are requested.");
        schedule.Complete(early, false);
        check(schedule.TryBegin(7, out var retry), "Unavailable colors retry without an extra paint.");
        schedule.Complete(retry, true);
        check(!schedule.TryBegin(7, out _), "Accepted static colors do not sustain idle IPC.");

        schedule.Wake();
        check(schedule.TryBegin(7, out var ready), "Load completion wakes colors with the same render count.");
        schedule.Wake();
        schedule.Complete(ready, true);
        check(schedule.TryBegin(7, out var newer), "Readiness arriving during IPC is not consumed by its reply.");
        schedule.Complete(ready, false);
        schedule.Complete(newer, true);
        check(!schedule.TryBegin(7, out _), "An old reply cannot invalidate the current successful sample.");

        check(schedule.TryBegin(8, out var changed), "A new native paint refreshes accepted colors.");
        schedule.Complete(changed, true);
        schedule.Reset();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            check(schedule.TryBegin(8, out var unavailable), "A failed request has a bounded retry opportunity.");
            schedule.Complete(unavailable, false);
        }
        check(!schedule.TryBegin(8, out _), "Persistent unavailability does not create an idle polling loop.");
        schedule.Wake();
        check(schedule.TryBegin(8, out var afterWake), "A later readiness event reopens an exhausted retry budget.");
        schedule.Complete(afterWake, true);
        schedule.Reset();
        schedule.Complete(afterWake, true);
        check(schedule.TryBegin(8, out _), "Navigation reset rejects the prior document's cached result.");
    }
}
