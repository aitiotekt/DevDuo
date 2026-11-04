using DevDuo;

internal static class ColorStabilityChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var edge = new EdgeColorState();
        check(edge.Observe(1, 0x123456, 0), "Reliable edge is accepted.");
        check(!edge.Observe(0, 0, 200) && edge.Color == 0x123456, "One absent frame cannot expose the page.");
        edge.BeginRequest(300);
        check(edge.Observe(0, 0, 400) && edge.Color is null, "Confirmed absence removes the extension.");
        edge.Observe(1, 0x654321, 500);
        edge.Observe(2, 0, 600);
        edge.Observe(3, 0, 1000);
        check(!edge.Expire(1199) && edge.Expire(1200) && edge.Color is null, "Failures cannot prolong the 600 ms deadline.");
        edge.Observe(1, 0xffffff, 1300);
        edge.Reset();
        check(edge.Color is null, "Navigation never carries an old edge color.");
        edge.Observe(1, 0x123456, 1400);
        edge.BeginRequest(1500);
        edge.BeginRequest(1700);
        check(!edge.Expire(2099) && edge.Expire(2100), "An in-flight request cannot extend a stale color beyond 600 ms.");
        edge.Observe(1, 0x123456, 2200);
        edge.Observe(3, 0, 2300);
        check(edge.Color == 0x123456, "A geometry invalidation retains the current document's accepted fill.");
        edge.Observe(3, 0, 2600);
        edge.BeginRequest(2700);
        check(!edge.Expire(2899) && edge.Expire(2900), "Repeated geometry changes and requests cannot renew the retention deadline.");
        edge.Observe(1, 0x654321, 3000);
        check(edge.Color == 0x654321 && !edge.Expire(4000), "A fresh geometry result replaces retained color and clears its deadline.");

        var material = new MaterialColorState();
        material.Reset(0);
        check(!material.Observe(0x808080, 0), "A transient sample is not immediately displayed.");
        check(material.Observe(0x818181, 180), "Two nearby samples establish a target.");
        material.Advance(240);
        check(material.Display == 0x404040 && material.Animating, "Material interpolation starts at its displayed value.");
        material.Advance(300);
        check(material.Display == 0x818181 && !material.Animating, "Material settles after 120 ms.");
        check(!material.Observe(0x838383, 400) && !material.Pending, "Small RGB noise creates no work.");
        material.Observe(0x303030, 500);
        material.Observe(0x505050, 700);
        check(material.Observe(0x707070, 900), "Continuous changes cannot postpone acceptance beyond 400 ms.");
        material.Advance(960);
        var middle = material.Display;
        material.Observe(0xffffff, 970);
        material.Observe(0xffffff, 980);
        check(material.Display != 0xffffff && material.Display <= middle, "A replacement target does not jump to its endpoint.");
        material.Reset(0x112233);
        check(material.Target is null && material.Raw is null && !material.Pending && !material.Animating,
            "Navigation cancels both candidate and animation state.");
        material.Observe(0x303030, 1000);
        material.Observe(0x505050, 1180);
        material.Observe(0x707070, 1360);
        check(!material.AcceptPending(1399) && material.AcceptPending(1400) && material.Target == 0x707070,
            "The deadline accepts the latest valid color even when bounded sampling has stopped.");
        material.Advance(1520);
        check(material.Display == 0x707070 && !material.Pending && !material.Animating,
            "Deadline acceptance also settles and stops all material timer work.");
    }
}
