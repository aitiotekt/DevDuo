var assertions = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    assertions++;
}

HostPreferenceChecks.Run(Check);
NavigationChecks.Run(Check);
DeviceSceneChecks.Run(Check);
SafariStateChecks.Run(Check);
BuiltinPageChecks.Run(Check);
PageTitleChecks.Run(Check);
ScrollIndicatorChecks.Run(Check);
Console.WriteLine($"Core checks passed: {assertions} assertions.");
