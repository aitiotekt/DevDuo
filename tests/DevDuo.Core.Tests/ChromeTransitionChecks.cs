using DevDuo;

internal static class ChromeTransitionChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var transition = new SafariChromeTransition();
        check(transition.Retarget(true, 0, 220, true) && transition.Running && transition.Fraction == 0,
            "Compaction starts at the previous presentation endpoint.");
        transition.Advance(55);
        check(Math.Abs(transition.Fraction - .15625) < .000001, "The first quarter follows cubic easing rather than a linear jump.");
        check(!transition.Retarget(true, 80, 220, true), "Repeated observations do not postpone a transition.");
        transition.Advance(110);
        check(transition.Fraction == .5, "The curve crosses its midpoint continuously.");
        check(transition.Retarget(false, 111, 220, true) && transition.Fraction == 0 && transition.Running,
            "Reversal begins a new interpolation from the displayed host frames.");
        transition.Advance(331);
        check(!transition.Running && transition.Fraction == 1 && !transition.Compact, "Expansion settles exactly at its endpoint.");
        transition.Retarget(true, 400, 0, true);
        check(!transition.Running && transition.Fraction == 1, "Zero duration disables motion.");
        transition.Retarget(false, 500, 220, false);
        check(!transition.Running && transition.Fraction == 1, "A disabled OS animation preference selects the endpoint immediately.");
        transition.Reset(true);
        check(transition.Compact && !transition.Running, "Navigation/scene reset cancels old presentation work.");
    }
}
