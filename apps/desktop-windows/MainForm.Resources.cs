using System.Text.Json;
using System.Windows.Forms;
using OneDesk.Desktop.Domain;

namespace OneDesk.Windows;

public sealed partial class MainForm
{
    private async Task<BridgeResponse> HandleResourceListAsync(BridgeMessage message)
    {
        if (_repository is null)
        {
            return ShellNotReady(message);
        }

        return new BridgeResponse(message.RequestId, true, await _repository.ListMediaResourcesAsync());
    }

    private async Task<BridgeResponse> HandleResourceAddAsync(BridgeMessage message)
    {
        if (_repository is null)
        {
            return ShellNotReady(message);
        }

        // 资源必须先进入桌面端统一资源管理器，再由页面或组件复制到自己的目录。
        using var dialog = new OpenFileDialog
        {
            Title = "添加媒体资源",
            Filter = "媒体文件 (*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp;*.mp4;*.webm;*.mov;*.mkv;*.avi)|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp;*.mp4;*.webm;*.mov;*.mkv;*.avi|All Files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return new BridgeResponse(message.RequestId, false, null, "UserCancelled", "已取消添加资源");
        }

        try
        {
            return new BridgeResponse(message.RequestId, true, await _repository.AddMediaResourceAsync(dialog.FileName));
        }
        catch (Exception ex)
        {
            return new BridgeResponse(message.RequestId, false, null, "ResourceAddFailed", ex.Message);
        }
    }

    private BridgeResponse HandleResourceDelete(BridgeMessage message)
    {
        var id = ReadPayloadString(message, "id");
        if (_repository is null)
        {
            return ShellNotReady(message);
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return InvalidPayload(message);
        }

        try
        {
            _repository.DeleteMediaResource(id);
            return new BridgeResponse(message.RequestId, true, null);
        }
        catch (Exception ex)
        {
            return new BridgeResponse(message.RequestId, false, null, "ResourceDeleteFailed", ex.Message);
        }
    }

    private async Task<BridgeResponse> HandleResourceCopyToComponentAsync(BridgeMessage message)
    {
        var payload = DeserializePayload<ResourceCopyPayload>(message);
        if (_repository is null)
        {
            return ShellNotReady(message);
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.ResourceId) || string.IsNullOrWhiteSpace(payload.TargetId))
        {
            return InvalidPayload(message);
        }

        try
        {
            // 选择资源时复制到组件目录，避免多个组件共享同一个可变媒体文件。
            return new BridgeResponse(message.RequestId, true, await _repository.CopyMediaResourceToComponentAsync(payload.ResourceId, payload.TargetId));
        }
        catch (Exception ex)
        {
            return new BridgeResponse(message.RequestId, false, null, "ResourceCopyFailed", ex.Message);
        }
    }

    private async Task<BridgeResponse> HandleResourceCopyToPageAsync(BridgeMessage message)
    {
        var payload = DeserializePayload<ResourceCopyPayload>(message);
        if (_repository is null)
        {
            return ShellNotReady(message);
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.ResourceId) || string.IsNullOrWhiteSpace(payload.TargetId))
        {
            return InvalidPayload(message);
        }

        try
        {
            // 选择资源时复制到页面目录，导入导出页面时可以带走自己的媒体依赖。
            return new BridgeResponse(message.RequestId, true, await _repository.CopyMediaResourceToPageAsync(payload.ResourceId, payload.TargetId));
        }
        catch (Exception ex)
        {
            return new BridgeResponse(message.RequestId, false, null, "ResourceCopyFailed", ex.Message);
        }
    }
}
