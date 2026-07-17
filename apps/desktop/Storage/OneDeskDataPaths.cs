using System.IO;

namespace OneDesk.Desktop.Storage;

public sealed class OneDeskDataPaths
{
    public OneDeskDataPaths(string? root = null)
    {
        Root = string.IsNullOrWhiteSpace(root)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OneDesk")
            : Path.GetFullPath(root);
        Components = Path.Combine(Root, "components");
        Actions = Path.Combine(Root, "actions");
        Pages = Path.Combine(Root, "pages");
        Schemes = Path.Combine(Root, "schemes");
        Plugins = Path.Combine(Root, "plugins");
        Resources = Path.Combine(Root, "resources");
        Logs = Path.Combine(Root, "logs");
        Exports = Path.Combine(Root, "exports");
        Cache = Path.Combine(Root, "cache");
        Temp = Path.Combine(Root, "temp");
    }

    public string Root { get; }
    public string Components { get; }
    public string Actions { get; }
    public string Pages { get; }
    public string Schemes { get; }
    public string Plugins { get; }
    public string Resources { get; }
    public string Logs { get; }
    public string Exports { get; }
    public string Cache { get; }
    public string Temp { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Components);
        Directory.CreateDirectory(Actions);
        Directory.CreateDirectory(Pages);
        Directory.CreateDirectory(Schemes);
        Directory.CreateDirectory(Plugins);
        Directory.CreateDirectory(Resources);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Exports);
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(Temp);
    }
}
