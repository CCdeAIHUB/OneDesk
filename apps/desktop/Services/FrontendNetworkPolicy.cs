namespace OneDesk.Desktop.Services;

public sealed class FrontendNetworkPolicy
{
    public bool BlockDirectFrontendNetworking { get; set; } = true;

    public bool ShouldBlock(Uri uri)
    {
        if (!BlockDirectFrontendNetworking)
        {
            return false;
        }

        return uri.Scheme is "http" or "https" or "ws" or "wss";
    }
}
