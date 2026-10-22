namespace DevDuo;

// Body angles are relative to the measured body-zero pose. The artwork's portrait
// canvas is not necessarily that pose (Open body zero is landscape).
public sealed class DevicePose
{
    public DeviceProfile Body { get; }
    public DeviceProfile Scene { get; }
    public int BodyInCanvasDegrees { get; }
    public int ContentInBodyDegrees { get; }
    public int SceneInCanvasDegrees => Normalize(BodyInCanvasDegrees + ContentInBodyDegrees);

    public DevicePose(DeviceProfile profile, SafariScene scene, int bodyZeroSceneDegrees)
    {
        BodyInCanvasDegrees = scene.BodyRotationDegrees;
        // Mask orientation does not establish the observed direction of page content.
        // The Device Hub canvas cohort records that transform independently.
        ContentInBodyDegrees = Normalize(scene.NativeSceneToCanvasClockwise - BodyInCanvasDegrees);
        Body = Rotate(profile, Normalize(bodyZeroSceneDegrees + BodyInCanvasDegrees));
        Scene = Rotate(profile, scene.SceneRotationDegrees);
    }

    private static DeviceProfile Rotate(DeviceProfile profile, int degrees)
    {
        if (degrees % 90 != 0) throw new ArgumentOutOfRangeException(nameof(degrees));
        for (var angle = 0; angle < degrees; angle += 90) profile = profile.RotateClockwise();
        return profile;
    }

    private static int Normalize(int degrees) => (degrees % 360 + 360) % 360;
}
