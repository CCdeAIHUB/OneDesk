using OneDesk.Desktop.Domain;
using OneDesk.Desktop.Storage;

namespace OneDesk.Desktop.Services;

public sealed class WorkspaceBootstrapper
{
    private readonly OneDeskRepository _repository;

    public WorkspaceBootstrapper(OneDeskRepository repository)
    {
        _repository = repository;
    }

    public async Task EnsureSeedDataAsync(CancellationToken cancellationToken = default)
    {
        if ((await _repository.ListComponentsAsync(cancellationToken)).Count > 0)
        {
            return;
        }

        var trigger = new TriggerDefinition
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
            Trigger = trigger,
            Invocations =
            [
                new JsApiInvocationDefinition
                {
                    TargetDeviceId = "desktop",
                    Capability = "plugin.invoke",
                    Parameters = new Dictionary<string, object?>
                    {
                        ["pluginId"] = "cc.onedesk.example.obs",
                        ["method"] = "switchScene"
                    }
                }
            ]
        }, cancellationToken);

        await _repository.SaveComponentAsync(new ComponentDefinition
        {
            Id = "component-scene-switch",
            Name = "场景切换",
            Version = "1.0.0",
            EditMode = ComponentEditMode.Visual,
            EntryFile = "src/SceneSwitch.vue",
            VisualConfigFile = "onedesk.visual.json",
            ActionIds = ["action-switch-scene"],
            RequestedPermissions =
            [
                new PermissionGrant
                {
                    Category = "插件",
                    Capability = "plugin.invoke",
                    HighRisk = false,
                    Description = "调用桌面端插件方法"
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

        await _repository.SavePageAsync(new PageDefinition
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
                Id = $"cell-{index + 1}",
                Row = index / 3 + 1,
                Column = index % 3 + 1,
                RowSpan = 1,
                ColumnSpan = 1,
                ComponentId = index == 0 ? "component-scene-switch" : null,
                Style = new CellStyleDefinition
                {
                    BorderRadius = 16,
                    OutlineColor = "#e2e8f0",
                    OutlineWidth = 1,
                    OutlineStyle = "solid"
                }
            }).ToArray()
        }, cancellationToken);

        await _repository.SaveSchemeAsync(new SchemeDefinition
        {
            Id = "scheme-live-console",
            Name = "直播控制台",
            Version = "1.0.0",
            PageIds = ["page-capture"],
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
                Trigger = trigger,
                Animation = "fade"
            },
            Edges = [],
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
    }
}
