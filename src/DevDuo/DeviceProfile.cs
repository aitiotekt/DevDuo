using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
namespace DevDuo;
public sealed class DeviceProfile
{
    public string Id { get; set; } = "unmeasured";
    public string Label { get; set; } = "Unmeasured";
    public double ScreenWidthCssPx { get; set; } = 466;
    public double ScreenHeightCssPx { get; set; } = 678;
    public double MaskCanvasWidth { get; set; } = 466;
    public double MaskCanvasHeight { get; set; } = 678;
    public string? ScreenMaskPath { get; set; }
    public double? OuterCornerRadiusDip { get; set; }
    public double? ShellInsetDip { get; set; }
    public double ShellCanvasWidth { get; set; }
    public double ShellCanvasHeight { get; set; }
    public double ScreenX { get; set; }
    public double ScreenY { get; set; }
    public double ScreenCanvasWidth { get; set; }
    public double ScreenCanvasHeight { get; set; }
    public string? ShellPath { get; set; }
    public string? HingePath { get; set; }
    public double ShellStrokeWidth { get; set; }
    public string GeometryStatus { get; set; } = "unmeasured";
    public int RotationClockwiseDegrees { get; set; }
    public DeviceProfile RotateClockwise()
    {
        var next=(DeviceProfile)MemberwiseClone();
        next.RotationClockwiseDegrees=(RotationClockwiseDegrees+90)%360;
        next.ScreenWidthCssPx=ScreenHeightCssPx;next.ScreenHeightCssPx=ScreenWidthCssPx;
        next.ScreenMaskPath=RotatePath(ScreenMaskPath,MaskCanvasHeight);
        next.MaskCanvasWidth=MaskCanvasHeight;next.MaskCanvasHeight=MaskCanvasWidth;
        next.ShellPath=RotatePath(ShellPath,ShellCanvasHeight);next.HingePath=RotatePath(HingePath,ShellCanvasHeight);
        next.ShellCanvasWidth=ShellCanvasHeight;next.ShellCanvasHeight=ShellCanvasWidth;
        next.ScreenX=ShellCanvasHeight-ScreenY-ScreenCanvasHeight;next.ScreenY=ScreenX;
        next.ScreenCanvasWidth=ScreenCanvasHeight;next.ScreenCanvasHeight=ScreenCanvasWidth;
        return next;
    }
    private static string? RotatePath(string? source,double height)
    {
        if(source is null)return null;
        var tokens=Regex.Matches(source,@"[MLCZ]|[-+]?(?:\d*\.)?\d+(?:[eE][-+]?\d+)?").Select(m=>m.Value).ToArray();
        var output=new List<string>();var index=0;
        while(index<tokens.Length)
        {
            var command=tokens[index++];output.Add(command);
            var count=command switch {"M" or "L"=>1,"C"=>3,"Z"=>0,_=>throw new FormatException("Unsupported mask command")};
            for(var n=0;n<count;n++)
            {
                var x=double.Parse(tokens[index++],CultureInfo.InvariantCulture);var y=double.Parse(tokens[index++],CultureInfo.InvariantCulture);
                output.Add((height-y).ToString("R",CultureInfo.InvariantCulture));output.Add(x.ToString("R",CultureInfo.InvariantCulture));
            }
        }
        return string.Join(" ",output);
    }
    public static DeviceProfile[] Load() => JsonSerializer.Deserialize<DeviceProfile[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"config","device-profiles.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
}
