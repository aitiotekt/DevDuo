using DevDuo;

internal static class ScrollIndicatorChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var state = new ScrollIndicatorState();
        state.Observe(0, 400, 1200, false, 0);
        check(state.Opacity(0) == 0, "Idle nonzero scroll position does not show an indicator.");
        state.RecordInput(10);
        state.Observe(0, 800, 1200, false, 20);
        check(state.Opacity(20) == 0, "Programmatic movement adjacent to real input cannot show the indicator.");
        state.Observe(1, 900, 1200, false, 30);
        check(state.Opacity(30) == .55, "Confirmed native User motion shows the indicator.");
        state.Observe(2, 1200, 1200, false, 100);
        check(state.Opacity(700) == .55, "The endpoint retains the same hold interval.");
        check(Math.Abs(state.Opacity(950) - .275) < .00001 && state.Opacity(1100) == 0,
            "The indicator fades and disappears while scrollY remains nonzero.");
        state.Observe(2, 1100, 1200, false, 1200);
        check(state.Opacity(1200) == 0, "Horizontal or programmatic motion without a new User sequence cannot revive it.");
        state.RecordInput(1300);
        state.Observe(3, 800, 1200, false, 1320);
        check(state.Opacity(1320) == .55, "Reverse vertical user motion is accepted.");
        state.Observe(4, 700, 1200, true, 1340);
        check(state.Opacity(1340) == 0, "Computed scrollbar-width none immediately suppresses the thumb.");
        state.Observe(4, 700, 1200, false, 1360);
        check(state.Opacity(1360) == 0, "Removing none does not restore a previous input session.");
        state.RecordInput(1400);
        state.Observe(5, 0, 0, false, 1420);
        check(state.Opacity(1420) == 0, "A short overflow-scroll document has no indicator.");
        state.Reset();
        state.Observe(0, 500, 1200, false, 1500);
        check(state.Opacity(1500) == 0, "Navigation and rotation reset input causality, even at nonzero Y.");
        state.RecordInput(1600);
        state.Observe(1, 600, 1200, false, 2300);
        check(state.Opacity(2300) == 0, "Expired input cannot be borrowed by later movement.");
        state.RecordInput(2400);
        state.Observe(2, 700, 1200, false, 2410);
        state.Observe(3, 800, 1200, false, 5000);
        check(state.Opacity(5000) == 0, "A new sequence after an unsampled idle interval cannot borrow stale following state.");
        state.Reset(true);
        state.RecordInput(5100);
        state.Observe(3, 800, 1200, false, 5110);
        check(state.Opacity(5110) == 0, "Geometry reset preserves the previous native sequence baseline.");
        state.Reset();
        state.Observe(4, 800, 1200, false, 5200);
        state.RecordInput(5210);
        state.Observe(4, 800, 1200, false, 5220);
        check(state.Opacity(5220) == 0, "Priming a new geometry with an unsampled earlier motion cannot satisfy later blocked input.");
        state.Dispose();
        state.RecordInput(2400);
        state.Observe(2, 900, 1200, false, 2410);
        check(state.Opacity(2410) == 0, "Disposed indicator state rejects late input and results.");
    }
}
