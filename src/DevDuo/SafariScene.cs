using System.Text.Json;
namespace DevDuo;
public sealed class SafariScene
{
    public string Id {get;set;}="";
    public string Fold {get;set;}="";
    public int BodyRotationDegrees {get;set;}
    public int SceneRotationDegrees {get;set;}
    public double Width {get;set;}
    public double Height {get;set;}
    public string MappingStatus {get;set;}="";
    public JsonElement SingleStates {get;set;}=JsonSerializer.SerializeToElement(new{});
    public JsonElement States {get;set;}
    public static SafariScene[] Load()=>JsonSerializer.Deserialize<SafariScene[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"config","safari-scenes.json")),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??[];
}
