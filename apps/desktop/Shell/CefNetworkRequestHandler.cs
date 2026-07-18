using OneDesk.Desktop.Services;
using Xilium.CefGlue;
using Xilium.CefGlue.Common.Handlers;

namespace OneDesk.Desktop.Shell;

/// <summary>
/// CEF 的渲染进程不能直接联网，也不能读取 wwwroot 之外的本地文件。
/// 所有设备网络请求必须通过壳子 JSAPI 进入受审计的网关。
/// </summary>
public sealed class CefNetworkRequestHandler : RequestHandler
{
    private readonly FrontendNetworkPolicy _networkPolicy;
    private readonly string _frontendRoot;
    private readonly CefFileOnlyResourceHandler _resourceHandler;

    public CefNetworkRequestHandler(FrontendNetworkPolicy networkPolicy, string frontendRoot)
    {
        _networkPolicy = networkPolicy;
        _frontendRoot = Path.GetFullPath(frontendRoot);
        _resourceHandler = new CefFileOnlyResourceHandler(_networkPolicy, _frontendRoot);
    }

    protected override bool OnBeforeBrowse(CefBrowser browser, CefFrame frame, CefRequest request, bool userGesture, bool isRedirect) =>
        IsBlocked(request.Url);

    protected override bool OnOpenUrlFromTab(
        CefBrowser browser,
        CefFrame frame,
        string targetUrl,
        CefWindowOpenDisposition targetDisposition,
        bool userGesture) => true;

    protected override CefResourceRequestHandler GetResourceRequestHandler(
        CefBrowser browser,
        CefFrame frame,
        CefRequest request,
        bool isNavigation,
        bool isDownload,
        string requestInitiator,
        ref bool disableDefaultHandling) => _resourceHandler;

    private bool IsBlocked(string? rawUrl)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri)) return true;
        if (_networkPolicy.ShouldBlock(uri)) return true;
        if (!uri.IsFile) return true;
        var path = Path.GetFullPath(uri.LocalPath);
        return !path.StartsWith(_frontendRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(path, _frontendRoot, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CefFileOnlyResourceHandler : CefResourceRequestHandler
    {
        private readonly FrontendNetworkPolicy _networkPolicy;
        private readonly string _frontendRoot;

        public CefFileOnlyResourceHandler(FrontendNetworkPolicy networkPolicy, string frontendRoot)
        {
            _networkPolicy = networkPolicy;
            _frontendRoot = frontendRoot;
        }

        protected override CefCookieAccessFilter? GetCookieAccessFilter(CefBrowser browser, CefFrame frame, CefRequest request) => null;

        protected override CefReturnValue OnBeforeResourceLoad(CefBrowser browser, CefFrame frame, CefRequest request, CefCallback callback)
        {
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || _networkPolicy.ShouldBlock(uri) || !uri.IsFile)
            {
                return CefReturnValue.Cancel;
            }

            var path = Path.GetFullPath(uri.LocalPath);
            var allowed = path.StartsWith(_frontendRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, _frontendRoot, StringComparison.OrdinalIgnoreCase);
            return allowed ? CefReturnValue.Continue : CefReturnValue.Cancel;
        }
    }
}
