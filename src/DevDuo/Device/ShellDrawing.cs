using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace DevDuo;
// Outer artwork is drawn independently; the native browser owns its actual inner mask.
public sealed class ShellDrawing(DeviceProfile profile,double scaleX,double scaleY) : Control
{
    private readonly Geometry? shell=profile.ShellPath is {} path?Geometry.Parse(path):null;
    private readonly Geometry? hinge=profile.HingePath is {} h?Geometry.Parse(h):null;
    public override void Render(DrawingContext context)
    {
        using(context.PushTransform(Matrix.CreateScale(scaleX,scaleY)))
        {
            if(hinge is not null)context.DrawGeometry(Brush.Parse("#454545"),new Pen(Brushes.Black,4),hinge);
            if(shell is not null)context.DrawGeometry(Brush.Parse("#131313"),profile.ShellStrokeWidth>0?new Pen(Brush.Parse("#242424"),profile.ShellStrokeWidth):null,shell);
        }
    }
}
