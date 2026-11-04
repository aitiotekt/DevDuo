#if WINDOWS
using System.Runtime.InteropServices;
namespace DevDuo;

internal sealed partial class WindowsWebViewPanel
{
    private readonly SafariChromeTransition chromeMotion = new();
    private readonly System.Windows.Forms.Timer chromeMotionTimer = new() { Interval = 16 };
    private WindowsComposition.Chrome[] motionFrom = [], motionTarget = [];
    private string[] motionFromNames = [], motionTargetNames = [];
    private long chromeMotionFrames;
    private (double Width, double Height, double Scale, string Mode)? motionContext;
    private bool clientAnimationsEnabled;
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetAnimationPreference(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool enabled, uint flags);

    private static WindowsComposition.Kind MotionRole(WindowsComposition.Kind kind) =>
        kind == WindowsComposition.Kind.CompactAddress ? WindowsComposition.Kind.Address : kind;

    private void ResetChromeMotion()
    {
        chromeMotionTimer.Stop();
        chromeMotion.Reset(compact);
        motionFrom = motionTarget = [];
        motionFromNames = motionTargetNames = [];
    }

    private void SetChromeTarget(WindowsComposition.Chrome[] frames, string[] names)
    {
        var context = (options.LogicalWidth, options.LogicalHeight, webkit.BackingScale, options.Mode);
        if (motionContext != context) { ResetChromeMotion(); motionContext = context; }
        if (chromeMotion.Compact != compact)
            clientAnimationsEnabled = GetAnimationPreference(0x1042, 0, out var enabled, 0) && enabled;
        if (chromeMotion.Retarget(compact, Environment.TickCount64, options.WindowsMaterial.ChromeTransitionMilliseconds,
            clientAnimationsEnabled && motionTarget.Length > 0 && options.Mode == "safari"))
        {
            // Interrupted transitions start at their actual displayed frames.
            motionFrom = webkit.Chrome.ToArray();
            motionFromNames = webkit.ChromeNames.ToArray();
        }
        motionTarget = frames;
        motionTargetNames = names;
        PresentChromeMotion();
        if (chromeMotion.Running) chromeMotionTimer.Start();
    }

    private void PresentChromeMotion()
    {
        chromeMotion.Advance(Environment.TickCount64);
        if (!chromeMotion.Running)
        {
            chromeMotionTimer.Stop();
            webkit.Chrome = motionTarget;
            webkit.ChromeNames = motionTargetNames;
        }
        else
        {
            var frames = new List<WindowsComposition.Chrome>();
            var names = new List<string>();
            float Mix(float from, float to) => from + (to - from) * (float)chromeMotion.Fraction;
            WindowsComposition.Chrome Shrink(WindowsComposition.Chrome item)
            {
                item.X += item.Width / 2; item.Y += item.Height / 2;
                item.Width = item.Height = item.VisualX = item.VisualY = item.VisualWidth = item.VisualHeight = item.Radius = item.ClipWidth = 0;
                item.FontSize = 1;
                return item;
            }
            WindowsComposition.Chrome Blend(WindowsComposition.Chrome from, WindowsComposition.Chrome to)
            {
                var item = to;
                item.X = Mix(from.X, to.X); item.Y = Mix(from.Y, to.Y);
                item.Width = Mix(from.Width, to.Width); item.Height = Mix(from.Height, to.Height);
                item.VisualX = Mix(from.VisualX, to.VisualX); item.VisualY = Mix(from.VisualY, to.VisualY);
                item.VisualWidth = Mix(from.VisualWidth, to.VisualWidth); item.VisualHeight = Mix(from.VisualHeight, to.VisualHeight);
                item.Radius = Mix(from.Radius, to.Radius); item.FontSize = Mix(from.FontSize, to.FontSize);
                item.ClipWidth = Mix(from.ClipWidth, to.ClipWidth);
                item.CompactMaterial = Mix(from.CompactMaterial, to.CompactMaterial);
                return item;
            }
            for (var index = 0; index < motionTarget.Length; index++)
            {
                var target = motionTarget[index];
                var old = Array.FindIndex(motionFrom, item => MotionRole(item.Kind) == MotionRole(target.Kind));
                frames.Add(Blend(old >= 0 ? motionFrom[old] : Shrink(target), target));
                names.Add(motionTargetNames[index]);
            }
            for (var index = 0; index < motionFrom.Length; index++)
            {
                var from = motionFrom[index];
                if (motionTarget.Any(item => MotionRole(item.Kind) == MotionRole(from.Kind))) continue;
                frames.Add(Blend(from, Shrink(from)));
                names.Add(motionFromNames[index]);
            }
            webkit.Chrome = frames.ToArray();
            webkit.ChromeNames = names.ToArray();
            chromeMotionFrames++;
        }
        // No WebKit geometry, DOM evaluation or telemetry publication per frame.
        webkit.Present();
    }
}
#endif
