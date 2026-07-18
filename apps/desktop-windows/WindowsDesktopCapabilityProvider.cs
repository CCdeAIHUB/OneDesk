using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.Devices.Enumeration;
using OneDesk.Desktop.Services;

namespace OneDesk.Windows;

internal sealed class WindowsDesktopCapabilityProvider : IDesktopCapabilityProvider, IDisposable
{
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfUnicode = 0x0004;
    private const uint InputKeyboard = 1;
    private const uint InputMouse = 0;
    private const uint MouseeventfMove = 0x0001;
    private const uint MouseeventfLeftdown = 0x0002;
    private const uint MouseeventfLeftup = 0x0004;
    private const uint MouseeventfRightdown = 0x0008;
    private const uint MouseeventfRightup = 0x0010;
    private const uint MouseeventfWheel = 0x0800;

    private readonly MainForm _owner;
    private readonly Action<string, string> _showNativeNotification;
    private readonly Action<string> _showInAppNotification;
    private readonly Dictionary<string, int> _hotkeyIds = new(StringComparer.OrdinalIgnoreCase);
    private int _nextHotkeyId = 0x4100;

    public WindowsDesktopCapabilityProvider(
        MainForm owner,
        Action<string, string> showNativeNotification,
        Action<string> showInAppNotification)
    {
        _owner = owner;
        _showNativeNotification = showNativeNotification;
        _showInAppNotification = showInAppNotification;
    }

    public IReadOnlySet<string> CapabilityIds => DesktopCapabilityContracts.Windows;

    public Task<JsApiResult> ExecuteAsync(JsApiRequest request, CancellationToken cancellationToken = default) => request.Capability switch
    {
        "device.display.list" => Task.FromResult(ListDisplays()),
        "device.power.status" => Task.FromResult(PowerStatus()),
        "clipboard.read" => OnUiAsync(ReadClipboard),
        "clipboard.write" => OnUiAsync(() => WriteClipboard(request)),
        "notification.native" => OnUiAsync(() => NativeNotification(request)),
        "notification.inApp" => OnUiAsync(() => InAppNotification(request)),
        "input.hotkey.register" => OnUiAsync(() => RegisterHotkey(request)),
        "input.hotkey.unregister" => OnUiAsync(() => UnregisterHotkey(request)),
        "input.keyboardMouseSimulation" => Task.FromResult(SimulateInput(request)),
        "memory.read" => Task.FromResult(ReadMemory(request)),
        "memory.write" => Task.FromResult(WriteMemory(request)),
        "camera.access" => EnumerateMediaDevicesAsync(DeviceClass.VideoCapture, cancellationToken),
        "microphone.access" => EnumerateMediaDevicesAsync(DeviceClass.AudioCapture, cancellationToken),
        "screen.capture" => CaptureScreenAsync(request, cancellationToken),
        "screen.record" => RecordScreenAsync(request, cancellationToken),
        _ => Task.FromResult(JsApiResult.Error("CapabilityPlatformHandlerMissing", "Windows 能力提供器未注册该能力。")),
    };

    public bool HandleWindowMessage(int message, nint wParam)
    {
        if (message != 0x0312) return false;
        var id = unchecked((int)wParam);
        var name = _hotkeyIds.FirstOrDefault(pair => pair.Value == id).Key;
        if (!string.IsNullOrWhiteSpace(name)) _showInAppNotification($"快捷键已触发：{name}");
        return true;
    }

    private JsApiResult ListDisplays() => JsApiResult.Success(Screen.AllScreens.Select(screen => new
    {
        id = screen.DeviceName,
        primary = screen.Primary,
        bounds = new { screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height },
        workingArea = new { screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height },
        scale = _owner.DeviceDpi / 96d,
        orientation = screen.Bounds.Width >= screen.Bounds.Height ? "landscape" : "portrait",
    }).ToArray());

    private static JsApiResult PowerStatus()
    {
        var status = SystemInformation.PowerStatus;
        return JsApiResult.Success(new
        {
            source = status.PowerLineStatus.ToString(),
            batteryPercent = status.BatteryLifePercent < 0 ? null : (int?)Math.Round(status.BatteryLifePercent * 100),
            remainingSeconds = status.BatteryLifeRemaining < 0 ? null : (int?)status.BatteryLifeRemaining,
            chargeStatus = status.BatteryChargeStatus.ToString(),
        });
    }

    private static JsApiResult ReadClipboard() => JsApiResult.Success(new
    {
        text = Clipboard.ContainsText() ? Clipboard.GetText(TextDataFormat.UnicodeText) : "",
        containsText = Clipboard.ContainsText(),
    });

    private static JsApiResult WriteClipboard(JsApiRequest request)
    {
        Clipboard.SetText(ReadString(request.Payload, "text", ""), TextDataFormat.UnicodeText);
        return JsApiResult.Success(new { written = true });
    }

    private JsApiResult NativeNotification(JsApiRequest request)
    {
        _showNativeNotification(ReadString(request.Payload, "title", "OneDesk"), ReadString(request.Payload, "message", "OneDesk 通知"));
        return JsApiResult.Success(new { shown = true });
    }

    private JsApiResult InAppNotification(JsApiRequest request)
    {
        _showInAppNotification(ReadString(request.Payload, "message", "OneDesk 通知"));
        return JsApiResult.Success(new { shown = true });
    }

    private JsApiResult RegisterHotkey(JsApiRequest request)
    {
        var name = ReadString(request.Payload, "name", "");
        var virtualKey = ReadInt(request.Payload, "virtualKey", 0);
        if (string.IsNullOrWhiteSpace(name) || virtualKey <= 0) return JsApiResult.Error("InvalidPayload", "注册快捷键需要 name 和 virtualKey。");
        if (_hotkeyIds.ContainsKey(name)) return JsApiResult.Error("HotkeyAlreadyRegistered", "同名快捷键已经注册。");
        var modifiers = ReadModifiers(request.Payload);
        var id = Interlocked.Increment(ref _nextHotkeyId);
        if (!RegisterHotKey(_owner.Handle, id, modifiers, unchecked((uint)virtualKey)))
            return JsApiResult.Error("HotkeyRegistrationFailed", $"系统拒绝注册快捷键，Win32={Marshal.GetLastWin32Error()}。");
        _hotkeyIds[name] = id;
        return JsApiResult.Success(new { name, virtualKey, modifiers });
    }

    private JsApiResult UnregisterHotkey(JsApiRequest request)
    {
        var name = ReadString(request.Payload, "name", "");
        if (!_hotkeyIds.Remove(name, out var id)) return JsApiResult.Success(new { name, removed = false });
        var removed = UnregisterHotKey(_owner.Handle, id);
        return removed
            ? JsApiResult.Success(new { name, removed = true })
            : JsApiResult.Error("HotkeyUnregisterFailed", $"系统拒绝注销快捷键，Win32={Marshal.GetLastWin32Error()}。");
    }

    private static JsApiResult SimulateInput(JsApiRequest request)
    {
        var type = ReadString(request.Payload, "type", "text").ToLowerInvariant();
        return type switch
        {
            "text" => SendText(ReadString(request.Payload, "text", "")),
            "key" => SendVirtualKey(ReadInt(request.Payload, "virtualKey", 0), ReadString(request.Payload, "action", "press")),
            "mouse" => SendMouse(request),
            _ => JsApiResult.Error("InvalidPayload", "输入类型必须是 text、key 或 mouse。"),
        };
    }

    private static JsApiResult SendText(string text)
    {
        var inputs = text.SelectMany(character => new[]
        {
            CreateKeyboardInput(character, KeyeventfUnicode),
            CreateKeyboardInput(character, KeyeventfUnicode | KeyeventfKeyup),
        }).ToArray();
        return SendInputs(inputs);
    }

    private static JsApiResult SendVirtualKey(int virtualKey, string action)
    {
        if (virtualKey <= 0) return JsApiResult.Error("InvalidPayload", "virtualKey 必须大于 0。");
        var inputs = action.ToLowerInvariant() switch
        {
            "down" => new[] { CreateKeyboardInput(unchecked((ushort)virtualKey), 0) },
            "up" => new[] { CreateKeyboardInput(unchecked((ushort)virtualKey), KeyeventfKeyup) },
            _ => new[] { CreateKeyboardInput(unchecked((ushort)virtualKey), 0), CreateKeyboardInput(unchecked((ushort)virtualKey), KeyeventfKeyup) },
        };
        return SendInputs(inputs);
    }

    private static JsApiResult SendMouse(JsApiRequest request)
    {
        var action = ReadString(request.Payload, "action", "move").ToLowerInvariant();
        var flags = action switch
        {
            "leftdown" => MouseeventfLeftdown,
            "leftup" => MouseeventfLeftup,
            "rightdown" => MouseeventfRightdown,
            "rightup" => MouseeventfRightup,
            "wheel" => MouseeventfWheel,
            _ => MouseeventfMove,
        };
        var input = new Input
        {
            Type = InputMouse,
            Union = new InputUnion
            {
                Mouse = new MouseInput
                {
                    Dx = ReadInt(request.Payload, "dx", 0),
                    Dy = ReadInt(request.Payload, "dy", 0),
                    MouseData = unchecked((uint)ReadInt(request.Payload, "wheelDelta", 0)),
                    Flags = flags,
                },
            },
        };
        return SendInputs([input]);
    }

    private static JsApiResult SendInputs(Input[] inputs)
    {
        if (inputs.Length == 0) return JsApiResult.Success(new { sent = 0 });
        var sent = SendInput(unchecked((uint)inputs.Length), inputs, Marshal.SizeOf<Input>());
        return sent == inputs.Length
            ? JsApiResult.Success(new { sent })
            : JsApiResult.Error("InputSimulationFailed", $"只发送了 {sent}/{inputs.Length} 个输入事件，Win32={Marshal.GetLastWin32Error()}。");
    }

    private static Input CreateKeyboardInput(ushort value, uint flags) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = (flags & KeyeventfUnicode) != 0 ? (ushort)0 : value, ScanCode = (flags & KeyeventfUnicode) != 0 ? value : (ushort)0, Flags = flags } },
    };

    private static JsApiResult ReadMemory(JsApiRequest request)
    {
        var processId = ReadInt(request.Payload, "processId", 0);
        var address = ReadAddress(request.Payload);
        var length = Math.Clamp(ReadInt(request.Payload, "length", 1), 1, 1024 * 1024);
        using var handle = OpenProcessHandle(ProcessVmRead | ProcessQueryLimitedInformation, processId);
        var buffer = new byte[length];
        if (!ReadProcessMemory(handle.DangerousGetHandle(), address, buffer, buffer.Length, out var read))
            return JsApiResult.Error("MemoryReadFailed", $"读取进程内存失败，Win32={Marshal.GetLastWin32Error()}。");
        if (read < buffer.Length) Array.Resize(ref buffer, checked((int)read));
        return JsApiResult.Success(new { processId, address = $"0x{address.ToInt64():X}", bytesRead = buffer.Length, base64 = Convert.ToBase64String(buffer) });
    }

    private static JsApiResult WriteMemory(JsApiRequest request)
    {
        var processId = ReadInt(request.Payload, "processId", 0);
        var address = ReadAddress(request.Payload);
        byte[] bytes;
        try { bytes = Convert.FromBase64String(ReadString(request.Payload, "base64", "")); }
        catch (FormatException) { return JsApiResult.Error("InvalidPayload", "memory.write 需要有效的 base64。"); }
        if (bytes.Length is 0 or > 1024 * 1024) return JsApiResult.Error("InvalidPayload", "单次内存写入必须为 1 字节到 1 MiB。");
        using var handle = OpenProcessHandle(ProcessVmWrite | ProcessVmOperation | ProcessQueryLimitedInformation, processId);
        if (!WriteProcessMemory(handle.DangerousGetHandle(), address, bytes, bytes.Length, out var written))
            return JsApiResult.Error("MemoryWriteFailed", $"写入进程内存失败，Win32={Marshal.GetLastWin32Error()}。");
        return JsApiResult.Success(new { processId, address = $"0x{address.ToInt64():X}", bytesWritten = written });
    }

    private static async Task<JsApiResult> EnumerateMediaDevicesAsync(DeviceClass deviceClass, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var devices = await DeviceInformation.FindAllAsync(deviceClass);
        cancellationToken.ThrowIfCancellationRequested();
        return JsApiResult.Success(devices.Select(device => new { id = device.Id, name = device.Name, enabled = device.IsEnabled }).ToArray());
    }

    private static Task<JsApiResult> CaptureScreenAsync(JsApiRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bounds = CaptureBounds(request);
        var path = OutputPath(request, ".png");
        using var bitmap = Capture(bounds);
        bitmap.Save(path, ImageFormat.Png);
        return Task.FromResult(JsApiResult.Success(new { path, width = bounds.Width, height = bounds.Height, format = "png" }));
    }

    private static async Task<JsApiResult> RecordScreenAsync(JsApiRequest request, CancellationToken cancellationToken)
    {
        var bounds = CaptureBounds(request);
        var durationSeconds = Math.Clamp(ReadInt(request.Payload, "durationSeconds", 3), 1, 10);
        var framesPerSecond = Math.Clamp(ReadInt(request.Payload, "framesPerSecond", 5), 1, 10);
        var path = OutputPath(request, ".gif");
        var codec = ImageCodecInfo.GetImageEncoders().Single(codec => codec.FormatID == ImageFormat.Gif.Guid);
        using var first = Capture(bounds);
        using (var parameters = EncoderParametersFor(EncoderValue.MultiFrame)) first.Save(path, codec, parameters);
        var delay = TimeSpan.FromSeconds(1d / framesPerSecond);
        var totalFrames = durationSeconds * framesPerSecond;
        for (var index = 1; index < totalFrames; index++)
        {
            await Task.Delay(delay, cancellationToken);
            using var frame = Capture(bounds);
            using var parameters = EncoderParametersFor(EncoderValue.FrameDimensionTime);
            first.SaveAdd(frame, parameters);
        }
        using (var parameters = EncoderParametersFor(EncoderValue.Flush)) first.SaveAdd(parameters);
        return JsApiResult.Success(new { path, width = bounds.Width, height = bounds.Height, format = "gif", durationSeconds, framesPerSecond });
    }

    private static EncoderParameters EncoderParametersFor(EncoderValue value)
    {
        var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.SaveFlag, (long)value);
        return parameters;
    }

    private static Bitmap Capture(Rectangle bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static Rectangle CaptureBounds(JsApiRequest request)
    {
        var displayIndex = ReadInt(request.Payload, "displayIndex", 0);
        var screens = Screen.AllScreens;
        if (displayIndex < 0 || displayIndex >= screens.Length) throw new InvalidDataException("DisplayIndexInvalid");
        return screens[displayIndex].Bounds;
    }

    private static string OutputPath(JsApiRequest request, string extension)
    {
        var requested = ReadString(request.Payload, "path", "");
        var path = string.IsNullOrWhiteSpace(requested)
            ? Path.Combine(Path.GetTempPath(), $"onedesk-{Guid.NewGuid():N}{extension}")
            : Path.GetFullPath(requested);
        if (!string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase)) path = Path.ChangeExtension(path, extension);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private Task<JsApiResult> OnUiAsync(Func<JsApiResult> action)
    {
        if (!_owner.InvokeRequired) return Task.FromResult(action());
        var completion = new TaskCompletionSource<JsApiResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _owner.BeginInvoke(new Action(() =>
        {
            try { completion.SetResult(action()); }
            catch (Exception error) { completion.SetException(error); }
        }));
        return completion.Task;
    }

    private static SafeProcessHandle OpenProcessHandle(uint access, int processId)
    {
        if (processId <= 0) throw new InvalidDataException("ProcessIdInvalid");
        var handle = OpenProcess(access, false, unchecked((uint)processId));
        if (handle.IsInvalid) throw new InvalidOperationException($"OpenProcessFailed:{Marshal.GetLastWin32Error()}");
        return handle;
    }

    private static nint ReadAddress(object? payload)
    {
        var text = ReadString(payload, "address", "");
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        if (!long.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var address) || address <= 0)
            throw new InvalidDataException("MemoryAddressInvalid");
        return new nint(address);
    }

    private static uint ReadModifiers(object? payload)
    {
        var modifiers = ReadStringArray(payload, "modifiers");
        uint value = 0;
        if (modifiers.Contains("alt", StringComparer.OrdinalIgnoreCase)) value |= 0x0001;
        if (modifiers.Contains("control", StringComparer.OrdinalIgnoreCase) || modifiers.Contains("ctrl", StringComparer.OrdinalIgnoreCase)) value |= 0x0002;
        if (modifiers.Contains("shift", StringComparer.OrdinalIgnoreCase)) value |= 0x0004;
        if (modifiers.Contains("win", StringComparer.OrdinalIgnoreCase)) value |= 0x0008;
        return value;
    }

    private static JsonElement? ReadElement(object? payload, string key) =>
        payload is JsonElement { ValueKind: JsonValueKind.Object } element && element.TryGetProperty(key, out var value) ? value : null;
    private static string ReadString(object? payload, string key, string fallback) =>
        ReadElement(payload, key) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? fallback : fallback;
    private static int ReadInt(object? payload, string key, int fallback) =>
        ReadElement(payload, key) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number) ? number : fallback;
    private static IReadOnlyList<string> ReadStringArray(object? payload, string key) =>
        ReadElement(payload, key) is { ValueKind: JsonValueKind.Array } value
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray()
            : [];

    public void Dispose()
    {
        foreach (var id in _hotkeyIds.Values) UnregisterHotKey(_owner.Handle, id);
        _hotkeyIds.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Union; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int Dx; public int Dy; public uint MouseData; public uint Flags; public uint Time; public nint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort VirtualKey; public ushort ScanCode; public uint Flags; public uint Time; public nint ExtraInfo; }

    private sealed class SafeProcessHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeProcessHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(nint process, nint address, [Out] byte[] buffer, int size, out nint bytesRead);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(nint process, nint address, byte[] buffer, int size, out nint bytesWritten);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
