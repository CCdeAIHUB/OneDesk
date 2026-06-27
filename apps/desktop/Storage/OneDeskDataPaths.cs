using System.IO;

namespace OneDesk.Desktop.Storage;

public sealed class OneDeskDataPaths
{
    public OneDeskDataPaths()
    {
        Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OneDesk");
        Components = Path.Combine(Root, "components");
        Actions = Path.Combine(Root, "actions");
        Pages = Path.Combine(Root, "pages");
        Schemes = Path.Combine(Root, "schemes");
        Plugins = Path.Combine(Root, "plugins");
        Logs = Path.Combine(Root, "logs");
        Temp = Path.Combine(Root, "temp");
    }

    public string Root { get; }
    public string Components { get; }
    public string Actions { get; }
    public string Pages { get; }
    public string Schemes { get; }
    public string Plugins { get; }
    public string Logs { get; }
    public string Temp { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Components);
        Directory.CreateDirectory(Actions);
        Directory.CreateDirectory(Pages);
        Directory.CreateDirectory(Schemes);
        Directory.CreateDirectory(Plugins);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Temp);
    }
}
