using System.Text.Json;
using DevDuo;

internal static class DeviceSceneChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var scenes = SafariScene.Load();
        check(scenes.Length == 8, "Retain all eight body/fold mappings.");
        check(scenes.Sum(scene => scene.States.EnumerateObject().Count() + scene.SingleStates.EnumerateObject().Count()) == 24,
            "Retain all 24 Safari initial/scrolled states, including Open single-tab variants.");
        var profiles = DeviceProfile.Load();
        foreach (var profile in profiles)
        {
            var zero = scenes.Single(scene => scene.Fold == profile.Id && scene.BodyRotationDegrees == 0);
            foreach (var scene in scenes.Where(scene => scene.Fold == profile.Id))
            {
                var pose = new DevicePose(profile, scene, zero.SceneRotationDegrees);
                check(pose.BodyInCanvasDegrees == scene.BodyRotationDegrees, "Body pose follows the commanded canvas angle.");
                check(pose.Scene.ScreenWidthCssPx == scene.Width && pose.Scene.ScreenHeightCssPx == scene.Height,
                    "Presentation must retain the measured scene size.");
                check(pose.Body.RotationClockwiseDegrees == (zero.SceneRotationDegrees + scene.BodyRotationDegrees) % 360,
                    "Artwork rotates mechanically from the fold's body-zero pose.");
                var swapped = pose.SceneInCanvasDegrees % 180 != 0;
                check((swapped ? pose.Scene.ScreenHeightCssPx : pose.Scene.ScreenWidthCssPx) == pose.Body.ScreenWidthCssPx &&
                    (swapped ? pose.Scene.ScreenWidthCssPx : pose.Scene.ScreenHeightCssPx) == pose.Body.ScreenHeightCssPx,
                    "Rotated scene and physical screen have matching extents.");
            }
        }
        var closed180 = new DevicePose(profiles[0], scenes.Single(scene => scene.Fold == "closed" && scene.BodyRotationDegrees == 180), 0);
        check(closed180.Scene.RotationClockwiseDegrees == 90 && closed180.Body.RotationClockwiseDegrees == 180 && closed180.SceneInCanvasDegrees == 90,
            "Closed 180 preserves landscape history while rotating the body to 180.");
        using (var references = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "reference-manifest.json"))))
            foreach (var reference in references.RootElement.GetProperty("cases").EnumerateArray())
            {
                var scene = scenes.Single(scene => scene.Fold == reference.GetProperty("fold").GetString() &&
                    scene.BodyRotationDegrees == reference.GetProperty("bodyAngleClockwise").GetInt32());
                var zero = scenes.Single(candidate => candidate.Fold == scene.Fold && candidate.BodyRotationDegrees == 0);
                var pose = new DevicePose(profiles.Single(profile => profile.Id == scene.Fold), scene, zero.SceneRotationDegrees);
                check(pose.SceneInCanvasDegrees == reference.GetProperty("nativeSceneToCanvasClockwise").GetInt32(),
                    "Presented content direction matches original Safari case " + reference.GetProperty("id").GetString());
                check((pose.BodyInCanvasDegrees + pose.ContentInBodyDegrees) % 360 == pose.SceneInCanvasDegrees,
                    "Body and content transforms compose to the observed canvas direction.");
            }
        foreach (var angle in new[] { 0, 90, 180, 270 })
            foreach (var scale in new[] { 1d, .94, .75, .5, 2 })
            {
                var turn = new QuarterTurn(678 * scale, 466 * scale, angle);
                foreach (var point in new[] { (0d, 0d), (678 * scale, 466 * scale), (25.25 * scale, 40.75 * scale), (-10d, 800d) })
                {
                    var canvas = turn.Forward(point.Item1, point.Item2);
                    var inverse = turn.Inverse(canvas.X, canvas.Y);
                    check(Math.Abs(inverse.X - point.Item1) < 1e-9 && Math.Abs(inverse.Y - point.Item2) < 1e-9,
                        "Physical input inverse includes rotation, fractional Fit and captured out-of-bounds drags.");
                }
            }
    }
}
