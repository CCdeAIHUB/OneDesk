using OneDesk.Desktop.Domain;
using OneDesk.Desktop.Storage;

namespace OneDesk.Desktop.Services;

public sealed class WorkspaceBootstrapper
{
    private readonly OneDeskRepository _repository;
    private readonly PermissionService _permissions;

    public WorkspaceBootstrapper(OneDeskRepository repository, PermissionService permissions)
    {
        _repository = repository;
        _permissions = permissions;
    }

    public async Task EnsureSeedDataAsync(CancellationToken cancellationToken = default)
    {
        var sceneTrigger = new TriggerDefinition
        {
            Id = "three-finger-swipe-up",
            Category = "touch.standard",
            DisplayName = "三指上滑",
            FingerCount = 3,
            PlatformLimited = false
        };

        await _repository.SaveActionAsync(new ActionDefinition
        {
            Id = "action-switch-scene",
            Name = "切换直播场景",
            Trigger = sceneTrigger,
            Invocations =
            [
                new JsApiInvocationDefinition
                {
                    TargetDeviceId = "desktop",
                    Capability = "plugin.invoke",
                    Parameters = new Dictionary<string, object?>
                    {
                        ["pluginId"] = "cc.onedesk.example.obs",
                        ["method"] = "switchScene",
                        ["parameters"] = new { scene = "主画面" }
                    }
                }
            ]
        }, cancellationToken);

        await _repository.SaveActionAsync(new ActionDefinition
        {
            Id = "action-system-notify",
            Name = "发送系统通知",
            Trigger = new TriggerDefinition
            {
                Id = "long-press",
                Category = "touch.standard",
                DisplayName = "长按",
                FingerCount = 1,
                PlatformLimited = false
            },
            Invocations =
            [
                new JsApiInvocationDefinition
                {
                    TargetDeviceId = "desktop",
                    Capability = "notification.native",
                    Parameters = new Dictionary<string, object?>
                    {
                        ["title"] = "OneDesk",
                        ["message"] = "动作已经触发"
                    }
                }
            ]
        }, cancellationToken);

        var grantedComponent = new TrustedSource("scheme-live-console", "page-capture", "component-scene-switch", null, "component");
        _permissions.Grant(PermissionService.SourceKey(grantedComponent), "plugin.invoke");
        _permissions.Grant(PermissionService.SourceKey(grantedComponent), "notification.native");

        await _repository.SaveComponentAsync(new ComponentDefinition
        {
            Id = "component-scene-switch",
            Name = "场景切换",
            Version = "1.0.0",
            EditMode = ComponentEditMode.Visual,
            EntryFile = "src/SceneSwitch.vue",
            VisualConfigFile = "onedesk.visual.json",
            ActionIds = ["action-switch-scene", "action-system-notify"],
            RequestedPermissions =
            [
                new PermissionGrant
                {
                    Category = "plugin",
                    Capability = "plugin.invoke",
                    HighRisk = false,
                    Description = "调用桌面端插件方法"
                },
                new PermissionGrant
                {
                    Category = "notification",
                    Capability = "notification.native",
                    HighRisk = false,
                    Description = "发送系统通知"
                }
            ],
            PluginDependencies =
            [
                new DependencyDefinition
                {
                    Id = "cc.onedesk.example.obs",
                    Version = "1.0.0",
                    Kind = "plugin"
                }
            ]
        }, cancellationToken);

        await _repository.SaveComponentAsync(new ComponentDefinition
        {
            Id = "component-volume-strip",
            Name = "音量推子",
            Version = "1.0.0",
            EditMode = ComponentEditMode.Code,
            EntryFile = "src/VolumeStrip.vue",
            VisualConfigFile = null,
            ActionIds = [],
            RequestedPermissions =
            [
                new PermissionGrant
                {
                    Category = "input",
                    Capability = "input.keyboardMouseSimulation",
                    HighRisk = true,
                    Description = "模拟键盘快捷键调整音量"
                }
            ],
            PluginDependencies = []
        }, cancellationToken);

        await _repository.SavePageAsync(CreateCapturePage(), cancellationToken);
        await _repository.SavePageAsync(CreateLivePage(), cancellationToken);

        await _repository.SaveSchemeAsync(new SchemeDefinition
        {
            Id = "scheme-live-console",
            Name = "直播控制台",
            Version = "1.0.0",
            PageIds = ["page-capture", "page-live"],
            GlobalPrevious = new PageSwitchDefinition
            {
                Trigger = new TriggerDefinition
                {
                    Id = "three-finger-swipe-down",
                    Category = "touch.standard",
                    DisplayName = "三指下滑",
                    FingerCount = 3
                },
                Animation = "fade"
            },
            GlobalNext = new PageSwitchDefinition
            {
                Trigger = sceneTrigger,
                Animation = "fade"
            },
            Edges =
            [
                new PageSwitchEdge
                {
                    FromPageId = "page-capture",
                    ToPageId = "page-live",
                    Trigger = new TriggerDefinition
                    {
                        Id = "three-finger-swipe-right",
                        Category = "touch.standard",
                        DisplayName = "三指右滑",
                        FingerCount = 3
                    },
                    Animation = "slide"
                }
            ],
            PluginDependencies =
            [
                new DependencyDefinition
                {
                    Id = "cc.onedesk.example.obs",
                    Version = "1.0.0",
                    Kind = "plugin"
                }
            ]
        }, cancellationToken);
        await _repository.ApplySchemeAsync("scheme-live-console", cancellationToken);
    }

    private static PageDefinition CreateCapturePage()
    {
        return new PageDefinition
        {
            Id = "page-capture",
            Name = "采集",
            Rows = 4,
            Columns = 3,
            BackgroundKind = "gradient",
            BackgroundValue = "sky",
            Spacing = new GridSpacing { Padding = 16, RowGap = 10, ColumnGap = 10 },
            Cells = Enumerable.Range(0, 12).Select(index => new GridCellDefinition
            {
                Id = $"capture-cell-{index + 1}",
                Row = index / 3 + 1,
                Column = index % 3 + 1,
                RowSpan = 1,
                ColumnSpan = 1,
                ComponentId = index == 0 ? "component-scene-switch" : index == 1 ? "component-volume-strip" : null,
                Style = DefaultCellStyle()
            }).ToArray()
        };
    }

    private static PageDefinition CreateLivePage()
    {
        return new PageDefinition
        {
            Id = "page-live",
            Name = "直播",
            Rows = 5,
            Columns = 3,
            BackgroundKind = "solid",
            BackgroundValue = "#0ea5e9",
            Spacing = new GridSpacing { Padding = 18, RowGap = 12, ColumnGap = 12 },
            Cells = Enumerable.Range(0, 15).Select(index => new GridCellDefinition
            {
                Id = $"live-cell-{index + 1}",
                Row = index / 3 + 1,
                Column = index % 3 + 1,
                RowSpan = index == 0 ? 2 : 1,
                ColumnSpan = index == 0 ? 2 : 1,
                ComponentId = index == 0 ? "component-scene-switch" : null,
                Style = DefaultCellStyle()
            }).ToArray()
        };
    }

    private static CellStyleDefinition DefaultCellStyle()
    {
        return new CellStyleDefinition
        {
            BorderRadius = 16,
            OutlineColor = "#e2e8f0",
            OutlineWidth = 1,
            OutlineStyle = "solid"
        };
    }
}
