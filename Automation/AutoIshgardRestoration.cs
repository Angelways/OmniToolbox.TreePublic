using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Inventory;
using Dalamud.Game.Command;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools.Dalamud;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmenTools.Interop.Game.AddonEvent;
using OmenTools.Info.Game.Packets.Upstream;
using GameEventHandler = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandler;
using GameEventHandlerContent = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandlerContent;
using GameEventID = FFXIVClientStructs.FFXIV.Client.Game.Event.EventId;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace OmniToolbox.TreePublic;

public sealed class AutoIshgardRestoration(AutoIshgardRestorationConfig config) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = "自动重建伊修加德",
        Description = "准备好生产材料后，模块将通过Artisan插件进行自动生产，随后进行自动提交与库啵好运道",
        Category = ModuleCategory.Automation,
        Author = "Angelways",
        SupportUrls = ["https://github.com/Angelways"],
        Commands =
        [
            new ModuleCommand(
                "/omni 自动重建伊修加德 → 打开自动重建伊修加德窗口",
                "/omni 自动重建伊修加德"),
            new ModuleCommand(
                "/omni 开关自动重建伊修加德 → 控制重建伊修加德自动化流程",
                "/omni 开关自动重建伊修加德")
        ]
    };

    private const string OPEN_COMMAND = "自动重建伊修加德";
    private const string TOGGLE_COMMAND = "开关自动重建伊修加德";
    private OmenTools.OmenService.CommandManager? commandManager;
    private readonly Dictionary<string, CommandInfo> registeredCommands = [];

    private static readonly GameInventoryType[] MainInventoryTypes =
    [
        GameInventoryType.Inventory1,
        GameInventoryType.Inventory2,
        GameInventoryType.Inventory3,
        GameInventoryType.Inventory4
    ];

    private static readonly RecipeOption[] Recipes =
    [
        new(34433, 31913, 8, 20, false, "第四期重建用的合板"),
        new(34441, 31921, 8, 40, false, "第四期重建用的木箱"),
        new(34449, 31929, 8, 60, false, "第四期重建用的纺车"),
        new(34457, 31937, 8, 70, false, "第四期重建用的梯子"),
        new(34465, 31945, 8, 80, false, "第四期重建用的睡床"),
        new(34473, 31953, 8, 80, true,  "第四期重建用的特供冰盒"),

        new(34434, 31914, 9, 20, false, "第四期重建用的合金"),
        new(34442, 31922, 9, 40, false, "第四期重建用的铁钉"),
        new(34450, 31930, 9, 60, false, "第四期重建用的手斧"),
        new(34458, 31938, 9, 70, false, "第四期重建用的锯子"),
        new(34466, 31946, 9, 80, false, "第四期重建用的火炉"),
        new(34474, 31954, 9, 80, true,  "第四期重建用的特供风向陆行鸟"),

        new(34435, 31915, 10, 20, false, "第四期重建用的金属板"),
        new(34443, 31923, 10, 40, false, "第四期重建用的铆钉"),
        new(34451, 31931, 10, 60, false, "第四期重建用的吊锅"),
        new(34459, 31939, 10, 70, false, "第四期重建用的口罩"),
        new(34467, 31947, 10, 80, false, "第四期重建用的街灯"),
        new(34475, 31955, 10, 80, true,  "第四期重建用的特供部队储物柜"),

        new(34436, 31916, 11, 20, false, "第四期重建用的金属锭"),
        new(34444, 31924, 11, 40, false, "第四期重建用的铁环"),
        new(34452, 31932, 11, 60, false, "第四期重建用的裁衣工具"),
        new(34460, 31940, 11, 70, false, "第四期重建用的石材"),
        new(34468, 31948, 11, 80, false, "第四期重建用的篝火台"),
        new(34476, 31956, 11, 80, true,  "第四期重建用的特供天文仪"),

        new(34437, 31917, 12, 20, false, "第四期重建用的鞣革"),
        new(34445, 31925, 12, 40, false, "第四期重建用的皮绳"),
        new(34453, 31933, 12, 60, false, "第四期重建用的皮袋"),
        new(34461, 31941, 12, 70, false, "第四期重建用的长靴"),
        new(34469, 31949, 12, 80, false, "第四期重建用的工作服"),
        new(34477, 31957, 12, 80, true,  "第四期重建用的特供工具腰带"),

        new(34438, 31918, 13, 20, false, "第四期重建用的草绳"),
        new(34446, 31926, 13, 40, false, "第四期重建用的布料"),
        new(34454, 31934, 13, 60, false, "第四期重建用的扫把"),
        new(34462, 31942, 13, 70, false, "第四期重建用的手套"),
        new(34470, 31950, 13, 80, false, "第四期重建用的遮蓬"),
        new(34478, 31958, 13, 80, true,  "第四期重建用的特供坎肩"),

        new(34439, 31919, 14, 20, false, "第四期重建用的墨水"),
        new(34447, 31927, 14, 40, false, "第四期重建用的植物油"),
        new(34455, 31935, 14, 60, false, "第四期重建用的圣水"),
        new(34463, 31943, 14, 70, false, "第四期重建用的肥皂"),
        new(34471, 31951, 14, 80, false, "第四期重建用的植物成长剂"),
        new(34479, 31959, 14, 80, true,  "第四期重建用的特供幻药"),

        new(34440, 31920, 15, 20, false, "第四期重建用的麻乳"),
        new(34448, 31928, 15, 40, false, "第四期重建用的芝麻饼干"),
        new(34456, 31936, 15, 60, false, "第四期重建用的红茶"),
        new(34464, 31944, 15, 70, false, "第四期重建用的药汤"),
        new(34472, 31952, 15, 80, false, "第四期重建用的炖菜"),
        new(34480, 31960, 15, 80, true,  "第四期重建用的特供冰糕")
    ];

    private ICallGateSubscriber<ushort, int, object>? craftItem;
    private ICallGateSubscriber<bool>? artisanIsBusy;
    private ICallGateSubscriber<bool>? getStopRequest;
    private ICallGateSubscriber<bool, object>? setStopRequest;
    private DateTime nextCheckAt;
    private DateTime phaseStartedAt;
    private DateTime nextActionAt;
    private bool running;
    private bool windowOpen;
    private bool windowExpanded;
    private bool windowCollapsed;
    private bool windowSizePending;
    private bool windowConfigChanged;
    private bool artisanStartedByModule;
    private bool vnavPathStartedByModule;
    private uint activeRecipeID;
    private uint activeItemID;
    private AutomationPhase phase;
    private int itemCountBeforeTurnIn;
    private uint scripCountBeforeTurnIn;
    private int ticketsToPlay;
    private bool lotteryScratched;
    private bool lotteryTicketCounted;
    private int lotteryScratchAttempts;
    private int craftCycleStartingItemCount;
    private DateTime? movableSince;
    private Vector3 initialDestination;
    private DateTime nextVoucherReadAt;
    private int voucherCount = -1;
    private int voucherLimit = -1;
    private string status = "待机";
    private string lastError = string.Empty;

    private const uint FIRMAMENT_TERRITORY_ID = 886;
    private const string FIRMAMENT_TELEPORT_COMMAND = "/pdrtelepo 无名众人广场";
    private const uint SKYBUILDERS_SCRIP_ITEM_ID = 28063;
    private const int RIGHT_LOTTERY_EVENT_PARAM = 22;
    private const uint APPRAISER_NPC_ID_1 = 1031690;
    private const uint APPRAISER_NPC_ID_2 = 1031677;
    private const uint LOTTERY_NPC_ID = 1031692;
    private static readonly Regex VOUCHER_PATTERN = new("^(\\d+)/(\\d+)$", RegexOptions.Compiled);

    public override bool HasSettings => true;

    protected override void OnEnable()
    {
        craftItem = DalamudServices.PluginInterface.GetIpcSubscriber<ushort, int, object>("Artisan.CraftItem");
        artisanIsBusy = DalamudServices.PluginInterface.GetIpcSubscriber<bool>("Artisan.IsBusy");
        getStopRequest = DalamudServices.PluginInterface.GetIpcSubscriber<bool>("Artisan.GetStopRequest");
        setStopRequest = DalamudServices.PluginInterface.GetIpcSubscriber<bool, object>("Artisan.SetStopRequest");
        RegisterCommands(OmenTools.OmenService.CommandManager.Instance());
        DalamudServices.Framework.Update += OnFrameworkUpdate;
        DalamudServices.PluginInterface.UiBuilder.Draw += DrawConfigurationWindow;
    }

    protected override void OnDisable()
    {
        UnregisterCommands();
        DalamudServices.Framework.Update -= OnFrameworkUpdate;
        DalamudServices.PluginInterface.UiBuilder.Draw -= DrawConfigurationWindow;
        windowOpen = false;
        windowExpanded = false;
        windowCollapsed = false;
        windowSizePending = false;
        StopProduction("模块已停用");
        craftItem = null;
        artisanIsBusy = null;
        getStopRequest = null;
        setStopRequest = null;
    }

    protected override bool OnInterruptAutomation()
    {
        if (!running)
        {
            return false;
        }

        StopProduction("已由 Omni 中断");
        return true;
    }

    protected override void OnDispose()
    {
        CleanupScripPurchase();
        UnregisterCommands();
    }

    private void RegisterCommands(OmenTools.OmenService.CommandManager manager)
    {
        UnregisterCommands();
        commandManager = manager;
        try
        {
            Register(OPEN_COMMAND, "打开自动重建伊修加德窗口");
            Register(TOGGLE_COMMAND, "控制重建伊修加德自动化流程");
        }
        catch
        {
            UnregisterCommands();
            throw;
        }

        void Register(string name, string description)
        {
            var info = new CommandInfo(OnRegisteredCommand) { HelpMessage = description };
            if (!manager.AddSubCommand(name, info))
            {
                throw new InvalidOperationException($"无法注册 /omni {name}：该命令已被占用。");
            }

            registeredCommands.Add(name, info);
        }
    }

    private void UnregisterCommands()
    {
        if (commandManager is not null)
        {
            foreach (var (name, info) in registeredCommands)
            {
                // 仅注销本实例注册的处理器，避免移除其他模块的命令。
                if (commandManager.SubCommands.TryGetValue(name, out var current) &&
                    ReferenceEquals(current, info))
                {
                    commandManager.RemoveSubCommand(name);
                }
            }
        }

        registeredCommands.Clear();
        commandManager = null;
    }

    private void OnRegisteredCommand(string command, string arguments)
    {
        if (IsEnabled)
        {
            TryHandleCommand(command, arguments);
        }
    }

    public override bool TryHandleCommand(string arguments)
    {
        return HandleCommandArguments(arguments);
    }

    public override bool TryHandleCommand(string command, string arguments)
    {
        // 兼容宿主按模块类名分发的入口。
        if (string.Equals(command.Trim(), ModuleName, StringComparison.OrdinalIgnoreCase))
        {
            return HandleCommandArguments(arguments);
        }

        // 子命令表传入的是中文命令名，箭头和说明不参与解析。
        return string.IsNullOrWhiteSpace(arguments) && HandleCommandArguments(command);
    }

    private bool HandleCommandArguments(string arguments)
    {
        var command = arguments.Trim();
        if (string.Equals(command, OPEN_COMMAND, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, "open", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, "窗口", StringComparison.OrdinalIgnoreCase))
        {
            OpenConfigurationWindow();
            return true;
        }

        if (string.Equals(command, TOGGLE_COMMAND, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, "toggle", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, "开关", StringComparison.OrdinalIgnoreCase))
        {
            ToggleProduction();
            return true;
        }

        return false;
    }

    public override bool DrawSettings()
    {
        var changed = windowConfigChanged;
        windowConfigChanged = false;

        if (ImGui.Button("自动重建伊修加德"))
        {
            OpenConfigurationWindow();
        }

        return changed;
    }

    private void DrawConfigurationWindow()
    {
        if (!windowOpen)
        {
            return;
        }

        using var font = OmniFonts.GetUIFont().Push();
        using var style = new ComicStyleScope();
        var compactSize = OmniTheme.Scale(new Vector2(340f, 116f));
        if (!windowExpanded && !windowCollapsed && !string.IsNullOrWhiteSpace(lastError))
        {
            var errorWidth = MathF.Max(
                1f, compactSize.X - 2f * (OmniTheme.ChromeFrameInset() + OmniTheme.WindowInset()));
            compactSize.Y += ImGui.CalcTextSize($"错误：{lastError}", false, errorWidth).Y +
                             ImGui.GetStyle().ItemSpacing.Y;
        }

        var targetSize = windowCollapsed
            ? OmniTheme.CollapsedWindowSize(OmniTheme.Scale(windowExpanded ? 720f : 340f))
            : windowExpanded ? OmniTheme.Scale(new Vector2(720f, 760f)) : compactSize;
        // The compact and collapsed views are intentionally fixed-size. Only the
        // expanded configuration view remains user-resizable.
        if (!windowExpanded || windowCollapsed || windowSizePending)
        {
            ImGui.SetNextWindowSize(targetSize, ImGuiCond.Always);
            windowSizePending = false;
        }
        ImGui.SetNextWindowCollapsed(false, ImGuiCond.Always);
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoBackground;
        if (!ImGui.Begin("###AutoIshgardRestorationWindow", flags))
        {
            ImGui.End();
            return;
        }

        try
        {
            var windowPosition = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();
            var framePosition = windowCollapsed
                ? windowPosition + new Vector2(OmniTheme.CollapsedHeaderSafeInset(), OmniTheme.CollapsedHeaderTop())
                : windowPosition + new Vector2(OmniTheme.ChromeFrameInset());
            var frameSize = windowCollapsed
                ? new Vector2(
                    MathF.Max(1f, windowSize.X - OmniTheme.CollapsedHeaderSafeInset() * 2f),
                    OmniTheme.TitleBarHeight())
                : windowSize - new Vector2(OmniTheme.ChromeFrameInset() * 2f);
            var chrome = OmniWindowChrome.Draw(
                framePosition,
                frameSize,
                windowCollapsed,
                "自动重建伊修加德",
                "##collapseAutoIshgardRestoration",
                "##closeAutoIshgardRestoration");
            if (chrome.ToggleCollapse)
            {
                windowCollapsed = !windowCollapsed;
                windowSizePending = true;
            }

            if (chrome.CloseClicked)
            {
                windowOpen = false;
            }

            if (!windowOpen || windowCollapsed || chrome.ToggleCollapse)
            {
                return;
            }

            var contentPosition = framePosition + new Vector2(
                OmniTheme.WindowInset(),
                OmniTheme.TitleBarHeight() + OmniTheme.WindowInset());
            var contentSize = new Vector2(
                MathF.Max(1f, frameSize.X - OmniTheme.WindowInset() * 2f),
                MathF.Max(
                    1f,
                    frameSize.Y - OmniTheme.TitleBarHeight() - OmniTheme.WindowInset() * 2f));
            ImGui.SetCursorScreenPos(contentPosition);
            if (!windowExpanded)
            {
                DrawWindowActionRow("展开", contentSize.X, () => { windowExpanded = true; windowSizePending = true; });
                if (!string.IsNullOrWhiteSpace(lastError))
                {
                    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + contentSize.X);
                    ImGui.TextUnformatted($"错误：{lastError}");
                    ImGui.PopTextWrapPos();
                }

                return;
            }

            ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            try
            {
                using var content = ImRaii.Child(
                    "##autoIshgardWindowContent",
                    contentSize,
                    false,
                    ImGuiWindowFlags.None);
                if (!content)
                {
                    return;
                }

                if (DrawConfigurationContents())
                {
                    windowConfigChanged = true;
                }
            }
            finally
            {
                ImGui.PopStyleVar();
                ImGui.PopStyleColor();
            }
        }
        finally
        {
            ImGui.End();
        }
    }

    private void DrawWindowActionRow(string secondaryLabel, float availableWidth, System.Action secondaryAction)
    {
        var rowPosition = ImGui.GetCursorScreenPos();
        var buttonHeight = OmniTheme.SmallButtonSize().Y;
        var secondarySize = OmniControls.CompactButtonSize(secondaryLabel);
        var primaryWidth = MathF.Min(
            OmniTheme.Scale(154f),
            MathF.Max(1f, availableWidth - secondarySize.X - ImGui.GetStyle().ItemSpacing.X));
        if (OmniControls.SmallButton(
                running ? "停止" : "启动",
                running,
                new Vector2(primaryWidth, buttonHeight)))
        {
            ToggleProduction();
        }

        ImGui.SetCursorScreenPos(new Vector2(
            rowPosition.X + MathF.Max(0f, availableWidth - secondarySize.X),
            rowPosition.Y));
        if (OmniControls.SmallButton(secondaryLabel, false, secondarySize))
        {
            secondaryAction();
        }

        ImGui.SetCursorScreenPos(new Vector2(
            rowPosition.X,
            rowPosition.Y + MathF.Max(buttonHeight, secondarySize.Y) + ImGui.GetStyle().ItemSpacing.Y));
    }

    private unsafe bool DrawConfigurationContents()
    {
        var changed = false;
        DrawWindowActionRow("收起", ImGui.GetContentRegionAvail().X, () => { windowExpanded = false; windowSizePending = true; });

        ImGui.Separator();
        var playerState = DalamudServices.PlayerState;
        var jobID = playerState.IsLoaded ? playerState.ClassJob.RowId : 0;
        var level = playerState.IsLoaded ? playerState.Level : (short)0;
        var selected = FindRecipe(config.SelectedRecipeID);

        ImGui.TextUnformatted(jobID is >= 8 and <= 15
            ? $"当前职业：{GetJobName(jobID)}  等级：{level}"
            : "当前不是生产职业，请切换职业。");

        var preview = selected is { } value && value.JobID == jobID
            ? FormatRecipe(value)
            : jobID is >= 8 and <= 15
                ? "请先选择你要生产的物品"
                : "请切换至生产职业";
        ImGui.SetNextItemWidth(GetLabeledControlWidth(520f, "生产内容"));
        using (var combo = ImRaii.Combo("生产内容", preview))
        {
            if (combo)
            {
                foreach (var recipe in Recipes)
                {
                    if (recipe.JobID != jobID)
                    {
                        continue;
                    }

                    var available = recipe.Level <= level;
                    using (ImRaii.Disabled(!available))
                    {
                        if (OmniControls.RoundedSelectable(
                                FormatRecipe(recipe),
                                config.SelectedRecipeID == recipe.RecipeID,
                                size: new Vector2(0f, OmniTheme.SmallButtonSize().Y)) && available)
                        {
                            config.SelectedRecipeID = recipe.RecipeID;
                            changed = true;
                        }
                    }
                }
            }
        }

        ImGui.Separator();
        var stopMode = config.StopMode;
        var stopModePreview = stopMode == 0
            ? "背包剩余空格达到下限时提交"
            : "目标物品达到指定数量时提交";
        ImGui.SetNextItemWidth(GetLabeledControlWidth(360f, "物品提交条件"));
        using (var combo = ImRaii.Combo("物品提交条件", stopModePreview))
        {
            if (combo)
            {
                var optionSize = new Vector2(0f, OmniTheme.SmallButtonSize().Y);
                ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(4f)));
                if (OmniControls.RoundedSelectable(
                        "背包剩余空格达到下限时提交",
                        stopMode == 0,
                        size: optionSize))
                {
                    config.StopMode = 0;
                    changed = true;
                }

                if (OmniControls.RoundedSelectable(
                        "目标物品达到指定数量时提交",
                        stopMode == 1,
                        size: optionSize))
                {
                    config.StopMode = 1;
                    changed = true;
                }
            }
        }

        if (config.StopMode == 0)
        {
            ImGui.TextUnformatted("最少保留背包空格");
            var freeSlots = config.MinimumFreeSlots;
            ImGui.SetNextItemWidth(OmniTheme.Scale(160f));
            if (OmniControls.InputInt("##minimumFreeSlots", ref freeSlots))
            {
                config.MinimumFreeSlots = Math.Clamp(freeSlots, 1, 139);
            }

            changed |= ImGui.IsItemDeactivatedAfterEdit();
        }
        else
        {
            ImGui.TextUnformatted("自动提交时的目标物品数量");
            var targetCount = config.TargetItemCount;
            ImGui.SetNextItemWidth(OmniTheme.Scale(160f));
            if (OmniControls.InputInt("##targetItemCount", ref targetCount))
            {
                config.TargetItemCount = Math.Clamp(targetCount, 1, 999);
            }

            changed |= ImGui.IsItemDeactivatedAfterEdit();
        }


        ImGui.Separator();
        ImGui.TextUnformatted("库啵好运票达到数量时开始抽奖");
        var ticketThreshold = config.TicketThreshold;
        ImGui.SetNextItemWidth(OmniTheme.Scale(160f));
        if (OmniControls.InputInt("##ticketThreshold", ref ticketThreshold))
        {
            config.TicketThreshold = Math.Clamp(ticketThreshold, 1, 10);
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();

        changed |= DrawScripPurchaseSettings();

        if (selected is { } current)
        {
            var snapshot = ReadInventory(current.ItemID);
            ImGui.TextUnformatted($"背包空格：{snapshot.FreeSlots} / {snapshot.TotalSlots}");
            ImGui.TextUnformatted($"{current.ItemName}：{snapshot.ItemCount} 个");
        }

        UpdateVoucherCount(DateTime.UtcNow);
        ImGui.TextUnformatted($"好运票：{FormatVoucherCount(voucherCount, voucherLimit)}");

        ImGui.TextUnformatted($"状态：{status}");
        if (!string.IsNullOrWhiteSpace(lastError))
        {
            ImGui.TextWrapped($"错误：{lastError}");
        }

        return changed;
    }

    private static float GetLabeledControlWidth(float preferredWidth, string label)
    {
        var available = ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(label).X -
                        ImGui.GetStyle().ItemSpacing.X;
        return MathF.Max(OmniTheme.Scale(160f), MathF.Min(OmniTheme.Scale(preferredWidth), available));
    }

    private void OpenConfigurationWindow()
    {
        windowOpen = true;
        windowExpanded = false;
        windowCollapsed = false;
        windowSizePending = true;
    }

    private void ToggleProduction()
    {
        if (running)
        {
            StopProduction("已手动停止");
        }
        else
        {
            StartProduction();
        }
    }

    public override bool ResetSettings()
    {
        var defaults = new AutoIshgardRestorationConfig();
        config.SelectedRecipeID = defaults.SelectedRecipeID;
        config.StopMode = defaults.StopMode;
        config.MinimumFreeSlots = defaults.MinimumFreeSlots;
        config.TargetItemCount = defaults.TargetItemCount;
        config.TicketThreshold = defaults.TicketThreshold;
        config.AutoBuyScrips = defaults.AutoBuyScrips;
        config.ScripThreshold = defaults.ScripThreshold;
        config.ScripShopID = defaults.ScripShopID;
        config.ScripItemID = defaults.ScripItemID;
        config.ScripQuantity = defaults.ScripQuantity;
        return true;
    }

    private void StartProduction()
    {
        if (running)
        {
            return;
        }

        lastError = string.Empty;
        var playerState = DalamudServices.PlayerState;
        if (!playerState.IsLoaded)
        {
            Fail("角色尚未登录。");
            return;
        }

        var currentJobID = playerState.ClassJob.RowId;
        if (currentJobID is < 8 or > 15)
        {
            Fail("当前不是生产职业，请切换职业。");
            return;
        }

        var recipe = FindRecipe(config.SelectedRecipeID);
        if (recipe is null || recipe.Value.JobID != currentJobID)
        {
            Fail("请先选择你要生产的物品。");
            return;
        }

        if (playerState.Level < recipe.Value.Level)
        {
            Fail($"当前职业等级不足，需要 {recipe.Value.Level} 级。");
            return;
        }

        try
        {
            if (artisanIsBusy?.InvokeFunc() == true)
            {
                Fail("Artisan 正在执行其他任务。");
                return;
            }

            running = true;
            turnInGeneration = 0;
            purchasedGeneration = -1;
            activeRecipeID = recipe.Value.RecipeID;
            activeItemID = recipe.Value.ItemID;
            nextCheckAt = DateTime.UtcNow;

            if (OmenTools.DService.Instance().ClientState.TerritoryType != FIRMAMENT_TERRITORY_ID)
            {
                if (!TrySendCommand(FIRMAMENT_TELEPORT_COMMAND))
                {
                    FailAutomation("无法执行传送指令 /pdrtelepo 无名众人广场。");
                    return;
                }

                EnterPhase(AutomationPhase.WaitForFirmament);
                status = "正在前往无名众人广场，等待进入天穹街（区域 886）";
                return;
            }

            StartCraftingCycle(recipe.Value, "开始");
        }
        catch (Exception ex)
        {
            FailAutomation($"无法启动自动流程：{ex.Message}");
        }
    }

    private void StopProduction(string reason)
    {
        CleanupScripPurchase();
        StopOwnedArtisan();
        StopOwnedVnavPath();

        running = false;
        phase = AutomationPhase.Idle;
        activeRecipeID = 0;
        activeItemID = 0;
        ticketsToPlay = 0;
        lotteryScratched = false;
        lotteryTicketCounted = false;
        lotteryScratchAttempts = 0;
        craftCycleStartingItemCount = 0;
        movableSince = null;
        initialDestination = default;
        artisanStartedByModule = false;
        vnavPathStartedByModule = false;
        status = reason;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!running || DateTime.UtcNow < nextCheckAt)
        {
            return;
        }

        nextCheckAt = DateTime.UtcNow.AddMilliseconds(100);
        try
        {
            UpdateVoucherCount(DateTime.UtcNow);
            if (IsScripPurchasePhase())
            {
                DriveScripPurchase();
                return;
            }

            var activeRecipe = FindRecipe(activeRecipeID);
            if (activeRecipe is null || activeRecipe.Value.ItemID != activeItemID)
            {
                StopProduction("运行数据无效，已停止");
                return;
            }

            DriveAutomation(activeRecipe.Value);
        }
        catch (Exception ex)
        {
            StopProduction("监控异常，已停止");
            lastError = ex.Message;
        }
    }

    private void DriveAutomation(RecipeOption recipe)
    {
        switch (phase)
        {
            case AutomationPhase.WaitForFirmament:
                DriveWaitForFirmament();
                break;
            case AutomationPhase.WaitForPlayerMovable:
                DriveWaitForPlayerMovable();
                break;
            case AutomationPhase.MoveToInitialPoint:
                DriveMoveToInitialPoint(recipe);
                break;
            case AutomationPhase.WaitArtisanStart:
            {
                var snapshot = ReadInventory(activeItemID);
                if (ShouldStop(snapshot, out var reason))
                {
                    RequestArtisanStop();
                    EnterPhase(AutomationPhase.WaitArtisanStop);
                    status = $"{reason}；等待 Artisan 停止";
                }
                else if (artisanIsBusy?.InvokeFunc() == true)
                {
                    EnterPhase(AutomationPhase.Crafting);
                    status = $"Artisan 生产中：{recipe.ItemName}";
                }
                else if (PhaseTimedOut(TimeSpan.FromSeconds(15)))
                {
                    artisanStartedByModule = false;
                    if (snapshot.ItemCount > craftCycleStartingItemCount)
                    {
                        BeginSubmission("Artisan 已完成本轮生产");
                    }
                    else
                    {
                        CompleteAutomation($"无法继续制作 {recipe.ItemName}，可能缺少材料或不满足制作条件");
                    }
                }

                break;
            }
            case AutomationPhase.Crafting:
            {
                var snapshot = ReadInventory(activeItemID);
                if (ShouldStop(snapshot, out var reason))
                {
                    RequestArtisanStop();
                    EnterPhase(AutomationPhase.WaitArtisanStop);
                    status = $"{reason}；等待 Artisan 停止";
                }
                else if (artisanIsBusy?.InvokeFunc() == false)
                {
                    artisanStartedByModule = false;
                    if (snapshot.ItemCount > craftCycleStartingItemCount)
                    {
                        BeginSubmission("Artisan 已结束本轮生产");
                    }
                    else
                    {
                        CompleteAutomation($"无法继续制作 {recipe.ItemName}，可能缺少材料或不满足制作条件");
                    }
                }

                break;
            }
            case AutomationPhase.WaitArtisanStop:
                if (artisanIsBusy?.InvokeFunc() == false)
                {
                    artisanStartedByModule = false;
                    BeginSubmission("Artisan 已停止");
                }
                else if (PhaseTimedOut(TimeSpan.FromSeconds(30)))
                {
                    FailAutomation("等待 Artisan 停止超时。");
                }
                break;
            case AutomationPhase.MoveToAppraiser:
                DriveStartNPCEvent([APPRAISER_NPC_ID_1, APPRAISER_NPC_ID_2],
                    AutomationPhase.OpenAppraiser, "提交NPC");
                break;
            case AutomationPhase.OpenAppraiser:
                DriveOpenAppraiser();
                break;
            case AutomationPhase.SelectJob:
                DriveSelectJob(recipe);
                break;
            case AutomationPhase.SelectItem:
                DriveSelectItem(recipe);
                break;
            case AutomationPhase.FillRequest:
                DriveFillRequest();
                break;
            case AutomationPhase.WaitTurnIn:
                DriveWaitTurnIn(recipe);
                break;
            case AutomationPhase.WaitSupplyRefresh:
                DriveWaitSupplyRefresh(recipe);
                break;
            case AutomationPhase.MoveToLottery:
                DriveStartNPCEvent([LOTTERY_NPC_ID], AutomationPhase.OpenLottery, "库啵好运道NPC");
                break;
            case AutomationPhase.OpenLottery:
                DriveOpenLottery();
                break;
            case AutomationPhase.PlayLottery:
                DrivePlayLottery();
                break;
            case AutomationPhase.PostLottery:
                DrivePostLottery(recipe);
                break;
            case AutomationPhase.PrepareNextCycle:
                DrivePrepareNextCycle(recipe);
                break;
        }
    }

    private void DriveWaitForFirmament()
    {
        if (OmenTools.DService.Instance().ClientState.TerritoryType == FIRMAMENT_TERRITORY_ID)
        {
            movableSince = null;
            EnterPhase(AutomationPhase.WaitForPlayerMovable);
            status = "已进入天穹街，等待玩家连续可动 1 秒";
            return;
        }

        status = "等待进入天穹街（区域 886）";
        if (PhaseTimedOut(TimeSpan.FromMinutes(3)))
        {
            FailAutomation("执行传送指令后，3 分钟内未进入天穹街（区域 886）。");
        }
    }

    private void DriveWaitForPlayerMovable()
    {
        if (OmenTools.DService.Instance().ClientState.TerritoryType != FIRMAMENT_TERRITORY_ID)
        {
            movableSince = null;
            status = "区域尚未稳定，等待进入天穹街（区域 886）";
            return;
        }

        if (!IsPlayerMovable())
        {
            movableSince = null;
            status = "已进入天穹街，等待玩家恢复可动";
            return;
        }

        movableSince ??= DateTime.UtcNow;
        if (DateTime.UtcNow - movableSince.Value < TimeSpan.FromSeconds(1))
        {
            status = "玩家已可动，等待状态持续 1 秒";
            return;
        }

        if (!vnavmeshIPC.IsPluginEnabled())
        {
            FailAutomation("vnavmesh 未安装或未启用。");
            return;
        }

        if (!vnavmeshIPC.GetIsNavReady())
        {
            status = "玩家已可动，等待 vnavmesh 导航网格准备完成";
            if (PhaseTimedOut(TimeSpan.FromSeconds(90)))
            {
                FailAutomation("进入天穹街后，vnavmesh 导航网格未能准备完成。");
            }
            return;
        }

        initialDestination = new Vector3(
            38f + (Random.Shared.NextSingle() * 9f),
            -16f,
            162f + (Random.Shared.NextSingle() * 14f));
        try
        {
            vnavmeshIPC.PathfindAndMoveTo(initialDestination, false);
            vnavPathStartedByModule = true;
        }
        catch (Exception ex)
        {
            FailAutomation($"无法执行初始导航：{ex.Message}");
            return;
        }
        EnterPhase(AutomationPhase.MoveToInitialPoint);
        nextActionAt = DateTime.UtcNow.AddSeconds(5);
        status = $"正在前往随机生产点：{FormatPosition(initialDestination)}";
    }

    private void DriveMoveToInitialPoint(RecipeOption recipe)
    {
        if (OmenTools.DService.Instance().ClientState.TerritoryType != FIRMAMENT_TERRITORY_ID)
        {
            FailAutomation("前往随机生产点时离开了天穹街（区域 886）。");
            return;
        }

        var player = OmenTools.DService.Instance().ObjectTable.LocalPlayer;
        if (player is null)
        {
            status = "等待读取玩家位置";
            return;
        }

        if (Vector3.Distance(player.Position, initialDestination) <= 2f)
        {
            StopOwnedVnavPath();
            StartCraftingCycle(recipe, "已到达随机生产点");
            return;
        }

        if (!vnavmeshIPC.GetIsPathfindRunning() && DateTime.UtcNow >= nextActionAt)
        {
            try
            {
                vnavmeshIPC.PathfindAndMoveTo(initialDestination, false);
                vnavPathStartedByModule = true;
            }
            catch (Exception ex)
            {
                FailAutomation($"无法再次执行初始导航：{ex.Message}");
                return;
            }
            nextActionAt = DateTime.UtcNow.AddSeconds(5);
        }

        status = $"正在前往随机生产点：{FormatPosition(initialDestination)}";
        if (PhaseTimedOut(TimeSpan.FromSeconds(90)))
        {
            FailAutomation("前往随机生产点超时。");
        }
    }

    private void StartCraftingCycle(RecipeOption recipe, string reason)
    {
        var snapshot = ReadInventory(recipe.ItemID);
        if (ShouldStop(snapshot, out var stopReason))
        {
            BeginSubmission(stopReason);
            return;
        }

        if (artisanIsBusy?.InvokeFunc() == true)
        {
            FailAutomation("Artisan 正在执行其他任务，无法开始下一轮生产。");
            return;
        }

        if (getStopRequest?.InvokeFunc() == true)
        {
            setStopRequest?.InvokeAction(false);
        }

        var amount = config.StopMode == 1
            ? Math.Max(1, config.TargetItemCount - snapshot.ItemCount)
            : 9999;
        craftCycleStartingItemCount = snapshot.ItemCount;
        if (craftItem is null)
        {
            FailAutomation("Artisan 插件接口不可用。");
            return;
        }

        artisanStartedByModule = true;
        craftItem.InvokeAction((ushort)recipe.RecipeID, amount);
        EnterPhase(AutomationPhase.WaitArtisanStart);
        status = $"{reason}；正在启动 Artisan 制作 {recipe.ItemName}";
    }

    private void BeginSubmission(string reason)
    {
        if (OmenTools.DService.Instance().ClientState.TerritoryType != FIRMAMENT_TERRITORY_ID)
        {
            FailAutomation("自动提交仅支持在天穹街内启动（区域 886）。");
            return;
        }

        if (ReadInventory(activeItemID).ItemCount <= 0)
        {
            CompleteAutomation("生产结束，但背包中没有可提交的目标物品，可能已无法继续制作");
            return;
        }

        EnterPhase(AutomationPhase.MoveToAppraiser);
        status = $"{reason}；前往提交NPC";
    }

    private void RequestArtisanStop()
    {
        if (!artisanStartedByModule)
        {
            return;
        }

        if (getStopRequest?.InvokeFunc() != true)
        {
            setStopRequest?.InvokeAction(true);
        }
    }

    private void DriveStartNPCEvent(uint[] npcIDs, AutomationPhase nextPhase, string label)
    {
        if (IsOccupied())
        {
            status = $"等待交互结束后打开{label}";
            return;
        }

        var npc = OmenTools.DService.Instance().ObjectTable
            .FirstOrDefault(o => npcIDs.Contains(GetBaseID(o.Address)) && o.IsTargetable);
        if (npc is null)
        {
            FailAutomation($"未找到{label}。");
            return;
        }

        if (!SendNPCEventStart(npc.Address))
        {
            FailAutomation($"无法打开{label}界面。");
            return;
        }

        EnterPhase(nextPhase);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
        status = $"正在打开{label}界面";
    }

    private unsafe void DriveOpenAppraiser()
    {
        if (GetAddon("HWDSupply") != null)
        {
            EnterPhase(AutomationPhase.SelectJob);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
            status = "提交界面已打开";
            return;
        }

        var talk = GetAddon("Talk");
        if (DateTime.UtcNow >= nextActionAt && IsOccupied() && talk != null)
        {
            talk->FireCallbackInt(0);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
            return;
        }

        if (PhaseTimedOut(TimeSpan.FromSeconds(15)))
        {
            FailAutomation("未能打开 HWDSupply 提交界面。");
        }
    }

    private unsafe void DriveSelectJob(RecipeOption recipe)
    {
        if (DateTime.UtcNow < nextActionAt)
        {
            return;
        }

        var addon = GetAddon("HWDSupply");
        if (addon == null)
        {
            FailAutomation("HWDSupply 提交界面意外关闭。");
            return;
        }

        addon->Callback(0, (int)recipe.JobID - 8);
        EnterPhase(AutomationPhase.SelectItem);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(700);
        status = $"已选择{GetJobName(recipe.JobID)}提交列表";
    }

    private unsafe void DriveSelectItem(RecipeOption recipe)
    {
        if (DateTime.UtcNow < nextActionAt)
        {
            return;
        }

        if (TryBeginAutoScripPurchase()) return;

        if (ReadInventory(activeItemID).ItemCount <= 0)
        {
            FinishSubmissionOrStartLottery(recipe);
            return;
        }

        var addon = GetAddon("HWDSupply");
        if (addon == null)
        {
            FailAutomation("HWDSupply 提交界面意外关闭。");
            return;
        }

        var row = FindSupplyRow(addon, activeItemID);
        if (row < 0)
        {
            if (PhaseTimedOut(TimeSpan.FromSeconds(10)))
            {
                FailAutomation($"提交列表中未找到 {recipe.ItemName}；请确认物品收藏价值达到最低要求。");
            }
            return;
        }

        itemCountBeforeTurnIn = ReadInventory(activeItemID).ItemCount;
        scripCountBeforeTurnIn = ReadSkybuildersScrips();
        addon->Callback(1, row);
        EnterPhase(AutomationPhase.FillRequest);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
        status = $"正在提交：{recipe.ItemName}";
    }

    private unsafe void DriveFillRequest()
    {
        if (DateTime.UtcNow < nextActionAt)
        {
            return;
        }

        if (TryDetectCompletedTurnIn(out var currentCount, out var currentScrips))
        {
            EnterWaitSupplyRefresh(currentCount, currentScrips);
            return;
        }

        var requestBase = GetAddon("Request");
        if (requestBase == null)
        {
            if (PhaseTimedOut(TimeSpan.FromSeconds(10)))
            {
                FailAutomation("提交物品后未出现 Request 窗口。");
            }
            return;
        }

        var request = (AddonRequest*)requestBase;
        if (request->HandOverButton != null && request->HandOverButton->IsEnabled)
        {
            ClickButton(requestBase, request->HandOverButton);
            EnterPhase(AutomationPhase.WaitTurnIn);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
            status = "已点击交纳，等待完成";
            return;
        }

        var picker = GetAddon("ContextIconMenu");
        if (picker != null)
        {
            var icon = picker->AtkValuesCount > 11 && picker->AtkValues[11].Type == ValueType.UInt
                ? picker->AtkValues[11].UInt
                : 0u;
            picker->Callback(0, 0, icon, 0u);
        }
        else
        {
            requestBase->Callback(2, 0u, 0u, 0u);
        }

        nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
        if (PhaseTimedOut(TimeSpan.FromSeconds(15)))
        {
            FailAutomation("自动放入收藏品超时。");
        }
    }

    private unsafe void DriveWaitTurnIn(RecipeOption recipe)
    {
        var yesNoBase = GetAddon("SelectYesno");
        if (DateTime.UtcNow >= nextActionAt && IsOccupied() && yesNoBase != null)
        {
            var yesNo = (AddonSelectYesno*)yesNoBase;
            if (yesNo->YesButton != null && yesNo->YesButton->IsEnabled)
            {
                ClickButton(yesNoBase, yesNo->YesButton);
                nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
            }
            return;
        }

        if (TryDetectCompletedTurnIn(out var currentCount, out var currentScrips))
        {
            EnterWaitSupplyRefresh(currentCount, currentScrips);
            return;
        }

        if (PhaseTimedOut(TimeSpan.FromSeconds(20)))
        {
            FailAutomation("交纳后物品数量没有减少。");
        }
    }

    private unsafe void DriveWaitSupplyRefresh(RecipeOption recipe)
    {
        if (DateTime.UtcNow < nextActionAt)
        {
            return;
        }

        var request = GetAddon("Request");
        if (request != null && !PhaseTimedOut(TimeSpan.FromSeconds(2)))
        {
            status = $"等待本次提交窗口关闭（剩余 {itemCountBeforeTurnIn} 个）";
            return;
        }

        if (request != null)
        {
            CloseAddon("Request");
            nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
            status = "正在关闭已完成的提交窗口";
            if (!PhaseTimedOut(TimeSpan.FromSeconds(5)))
            {
                return;
            }
        }

        if (TryBeginAutoScripPurchase()) return;

        if (GetAddon("HWDSupply") == null)
        {
            if (IsOccupied())
            {
                if (PhaseTimedOut(TimeSpan.FromSeconds(12)))
                {
                    FailAutomation("提交后未能返回物品列表。");
                }
                return;
            }

            EnterPhase(AutomationPhase.MoveToAppraiser);
            status = "提交界面已关闭，正在重新打开";
            return;
        }

        if (TryReadVoucherCount(out var vouchers, out _) && vouchers >= config.TicketThreshold)
        {
            ticketsToPlay = vouchers;
            CloseAddon("HWDSupply");
            EnterPhase(AutomationPhase.MoveToLottery);
            status = $"库啵好运票达到 {vouchers} 张，停止提交并前往抽奖";
            return;
        }

        if (itemCountBeforeTurnIn > 0)
        {
            EnterPhase(AutomationPhase.SelectItem);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(700);
            status = $"继续提交，背包剩余 {itemCountBeforeTurnIn} 个";
        }
        else
        {
            FinishSubmissionOrStartLottery(recipe);
        }
    }

    private unsafe void FinishSubmissionOrStartLottery(RecipeOption recipe)
    {
        if (TryBeginAutoScripPurchase()) return;

        if (TryReadVoucherCount(out var vouchers, out _) && vouchers >= config.TicketThreshold)
        {
            ticketsToPlay = vouchers;
            CloseAddon("HWDSupply");
            EnterPhase(AutomationPhase.MoveToLottery);
            status = $"提交完成，持有 {vouchers} 张票，前往抽奖";
            return;
        }

        BeginNextCraftingCycle($"{recipe.ItemName} 已全部提交");
    }

    private unsafe void DriveOpenLottery()
    {
        var lottery = (AddonHWDLottery*)GetAddon("HWDLottery");
        if (lottery != null)
        {
            if (!lottery->AtkUnitBase.IsReady)
            {
                status = "等待库啵好运道界面初始化";
                if (PhaseTimedOut(TimeSpan.FromSeconds(15)))
                {
                    FailAutomation("HWDLottery 界面已显示但未完成初始化。");
                }
                return;
            }

            PrepareLotteryCard();
            return;
        }

        if (DateTime.UtcNow >= nextActionAt)
        {
            var yesNoBase = GetAddon("SelectYesno");
            var talk = GetAddon("Talk");
            if (yesNoBase != null)
            {
                var yesNo = (AddonSelectYesno*)yesNoBase;
                if (yesNo->YesButton != null && yesNo->YesButton->IsEnabled)
                {
                    ClickButton(yesNoBase, yesNo->YesButton);
                }
            }
            else if (talk != null)
            {
                talk->FireCallbackInt(0);
            }

            nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
        }

        if (PhaseTimedOut(TimeSpan.FromSeconds(15)))
        {
            FailAutomation("未能打开 HWDLottery 抽奖界面。");
        }
    }

    private unsafe void DrivePlayLottery()
    {
        var addon = (AddonHWDLottery*)GetAddon("HWDLottery");
        if (addon == null)
        {
            if (lotteryTicketCounted)
            {
                EnterPhase(AutomationPhase.PostLottery);
                nextActionAt = DateTime.UtcNow.AddMilliseconds(700);
                status = $"已完成一张票，剩余 {ticketsToPlay} 张";
            }
            else if (PhaseTimedOut(TimeSpan.FromSeconds(10)))
            {
                FailAutomation("抽奖界面在刮奖前关闭。");
            }
            return;
        }

        if (!addon->AtkUnitBase.IsReady)
        {
            status = "等待库啵好运道界面准备完成";
            return;
        }

        if (addon->Stage >= 2)
        {
            lotteryScratched = true;
        }

        if (lotteryTicketCounted && addon->Stage < 2 && ticketsToPlay > 0)
        {
            PrepareLotteryCard();
            return;
        }

        if (addon->Stage == 3 && addon->CloseButton != null && addon->CloseButton->IsEnabled)
        {
            if (!lotteryTicketCounted)
            {
                ticketsToPlay = Math.Max(0, ticketsToPlay - 1);
                lotteryTicketCounted = true;
            }

            if (DateTime.UtcNow >= nextActionAt)
            {
                ClickButton(&addon->AtkUnitBase, addon->CloseButton);
                nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
                status = $"本张票已揭晓，正在关闭结果（剩余 {ticketsToPlay} 张）";
            }
        }
        else if (addon->Stage == 2)
        {
            status = $"库啵好运道揭晓中（剩余 {ticketsToPlay} 张）";
        }
        else if (!lotteryScratched && DateTime.UtcNow >= nextActionAt)
        {
            var evt = new AtkEvent { Param = RIGHT_LOTTERY_EVENT_PARAM };
            var eventData = default(AtkEventData);
            addon->AtkUnitBase.ReceiveEvent(
                AtkEventType.ButtonClick,
                RIGHT_LOTTERY_EVENT_PARAM,
                &evt,
                &eventData);
            lotteryScratchAttempts++;
            nextActionAt = DateTime.UtcNow.AddMilliseconds(1500);
            status = $"已触发右侧三格按钮，第 {lotteryScratchAttempts} 次尝试，等待界面确认";
        }

        if (PhaseTimedOut(TimeSpan.FromSeconds(30)))
        {
            FailAutomation("等待库啵好运道揭晓超时。");
        }
    }

    private unsafe void DrivePostLottery(RecipeOption recipe)
    {
        if (DateTime.UtcNow < nextActionAt)
        {
            return;
        }

        var lottery = (AddonHWDLottery*)GetAddon("HWDLottery");
        if (lottery != null)
        {
            if (!lottery->AtkUnitBase.IsReady)
            {
                status = "等待下一张库啵好运票界面初始化";
                return;
            }

            if (lottery->Stage < 2 && ticketsToPlay > 0)
            {
                PrepareLotteryCard();
            }
            else
            {
                EnterPhase(AutomationPhase.PlayLottery);
                nextActionAt = DateTime.UtcNow;
                status = "正在继续抽奖";
            }
            return;
        }

        var talk = GetAddon("Talk");
        if (talk != null)
        {
            talk->FireCallbackInt(0);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
            return;
        }

        var yesNoBase = GetAddon("SelectYesno");
        if (yesNoBase != null)
        {
            var yesNo = (AddonSelectYesno*)yesNoBase;
            if (yesNo->YesButton != null && yesNo->YesButton->IsEnabled)
            {
                ClickButton(yesNoBase, yesNo->YesButton);
                nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
            }
            return;
        }

        if (IsOccupied())
        {
            return;
        }

        if (ticketsToPlay > 0)
        {
            var npc = OmenTools.DService.Instance().ObjectTable
                .FirstOrDefault(o => GetBaseID(o.Address) == LOTTERY_NPC_ID && o.IsTargetable);
            if (npc is null)
            {
                FailAutomation("未找到库啵好运道NPC。");
                return;
            }

            if (!SendNPCEventStart(npc.Address))
            {
                FailAutomation("无法打开库啵好运道NPC界面。");
                return;
            }

            EnterPhase(AutomationPhase.OpenLottery);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
            lotteryScratched = false;
            lotteryTicketCounted = false;
            lotteryScratchAttempts = 0;
            status = $"继续抽奖，剩余 {ticketsToPlay} 张";
            return;
        }

        if (ReadInventory(activeItemID).ItemCount > 0)
        {
            EnterPhase(AutomationPhase.MoveToAppraiser);
            status = $"抽奖完成，返回提交剩余的 {recipe.ItemName}";
        }
        else
        {
            BeginNextCraftingCycle("物品提交及库啵好运道抽奖完成");
        }
    }

    private unsafe void BeginNextCraftingCycle(string reason)
    {
        CloseAddon("Request");
        CloseAddon("HWDSupply");
        EnterPhase(AutomationPhase.PrepareNextCycle);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(800);
        status = $"{reason}；准备下一轮生产";
    }

    private unsafe void DrivePrepareNextCycle(RecipeOption recipe)
    {
        if (DateTime.UtcNow < nextActionAt)
        {
            return;
        }

        if (GetAddon("Request") != null || GetAddon("HWDSupply") != null)
        {
            CloseAddon("Request");
            CloseAddon("HWDSupply");
            nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
            status = "正在关闭提交界面，准备下一轮生产";
            return;
        }

        if (IsOccupied())
        {
            status = "等待交互结束，准备下一轮生产";
            if (PhaseTimedOut(TimeSpan.FromSeconds(20)))
            {
                FailAutomation("准备下一轮生产时，玩家长时间处于不可操作状态。");
            }
            return;
        }

        StartCraftingCycle(recipe, "开始下一轮");
    }

    private void PrepareLotteryCard()
    {
        EnterPhase(AutomationPhase.PlayLottery);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(800);
        lotteryScratched = false;
        lotteryTicketCounted = false;
        lotteryScratchAttempts = 0;
        status = $"准备选择右侧三个格子，剩余 {ticketsToPlay} 张";
    }

    private bool TryDetectCompletedTurnIn(out int currentCount, out uint currentScrips)
    {
        currentCount = ReadInventory(activeItemID).ItemCount;
        currentScrips = ReadSkybuildersScrips();
        return currentCount < itemCountBeforeTurnIn || currentScrips > scripCountBeforeTurnIn;
    }

    private void EnterWaitSupplyRefresh(int currentCount, uint currentScrips)
    {
        turnInGeneration++;
        var detectedByScrips = currentScrips > scripCountBeforeTurnIn;
        itemCountBeforeTurnIn = currentCount;
        scripCountBeforeTurnIn = currentScrips;
        EnterPhase(AutomationPhase.WaitSupplyRefresh);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(800);
        status = detectedByScrips
            ? $"已通过天穹街振兴票变化确认提交，等待界面刷新（剩余 {currentCount} 个）"
            : $"已提交 1 件，等待提交界面刷新（剩余 {currentCount} 个）";
    }

    // SpecialShop rows verified against the Chinese client. Names/prices are not duplicated here.
    private static readonly uint[] ScripShopIDs = [1770041, 1770281, 1770301];
    private readonly List<ScripProduct> scripProducts = [];
    private string scripProductSearch = string.Empty;
    private string scripCatalogError = string.Empty;
    private bool scripCatalogLoaded;
    private ScripProduct? pendingScripProduct;
    private bool scripEventOwned;
    private bool scripEventEnded;
    private bool scripQuantitySent;
    private bool scripConfirmSent;
    private int scripRemaining;
    private int scripPurchased;
    private int scripBatch;
    private int scripSelectedIndex = -1;
    private uint scripBalanceBefore;
    private int scripItemCountBefore;
    private int scripVouchersBefore = -1;
    private int turnInGeneration;
    private int purchasedGeneration = -1;

    private sealed record ScripProduct(uint ShopID, uint ItemID, string Name, int Price, int StackSize, bool Unique);

    private void EnsureScripCatalog()
    {
        if (scripCatalogLoaded) return;
        try
        {
            var products = new List<ScripProduct>();
            foreach (var id in ScripShopIDs)
            {
                if (!LuminaGetter.TryGetRow<SpecialShop>(id, out var shop))
                    throw new InvalidOperationException("无法读取振兴票商店数据。");
                products.AddRange(ReadScripProducts(shop, itemID => LuminaGetter.GetRow<Item>(itemID)));
            }

            scripProducts.AddRange(products.OrderBy(x => x.ShopID).ThenBy(x => x.Name));
            scripCatalogLoaded = true;
            scripCatalogError = string.Empty;
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "AutoIshgardRestoration: failed to load scrip products.");
            scripCatalogError = "振兴票商品数据暂不可用，请重新打开模块后重试。";
        }
    }

    private static List<ScripProduct> ReadScripProducts(SpecialShop shop, Func<uint, Item?> getItem)
    {
        var result = new List<ScripProduct>();
        foreach (var entry in shop.Item)
        {
            var rewards = entry.ReceiveItems.Where(x => x.Item.RowId != 0).ToArray();
            var costs = entry.ItemCosts.Where(x => x.ItemCost.RowId != 0 || x.CurrencyCost != 0).ToArray();
            // Only expose one-item, one-currency recipes that this buyer can verify exactly.
            if (rewards.Length != 1 || rewards[0].ReceiveCount != 1 || rewards[0].ReceiveHq ||
                costs.Length != 1 || costs[0].ItemCost.RowId != SKYBUILDERS_SCRIP_ITEM_ID ||
                costs[0].CurrencyCost is 0 or > 10000 || costs[0].CostType != 0 || costs[0].CollectabilityCost != 0)
                continue;
            var item = getItem(rewards[0].Item.RowId);
            if (item is not { } value || value.StackSize == 0) continue;
            result.Add(new ScripProduct(shop.RowId, value.RowId, value.Name.ExtractText(),
                (int)costs[0].CurrencyCost, (int)value.StackSize, value.IsUnique));
        }
        return result;
    }

    private ScripProduct? SelectedScripProduct() => scripProducts.FirstOrDefault(
        x => x.ShopID == config.ScripShopID && x.ItemID == config.ScripItemID);

    private static bool MatchesScripSearch(string name, string search) =>
        name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase);

    private bool DrawScripPurchaseSettings()
    {
        ImGui.Separator();
        var changed = false;
        using (ImRaii.Disabled(running))
        {
            var enabled = config.AutoBuyScrips;
            if (OmniControls.Checkbox("天穹街振兴票自动购买", ref enabled))
            {
                config.AutoBuyScrips = enabled;
                changed = true;
            }
            if (!enabled) return changed;
            EnsureScripCatalog();
            ImGui.TextUnformatted($"天穹街振兴票：{ReadSkybuildersScrips()} / 10000");
            ImGui.TextUnformatted("天穹街振兴票达到数量时开始购买");
            var threshold = config.ScripThreshold;
            ImGui.SetNextItemWidth(OmniTheme.Scale(160f));
            if (OmniControls.InputInt("##scripThreshold", ref threshold))
                config.ScripThreshold = Math.Clamp(threshold, 1, 10000);
            changed |= ImGui.IsItemDeactivatedAfterEdit();

            var selected = SelectedScripProduct();
            ImGui.SetNextItemWidth(GetLabeledControlWidth(520f, "购买物品"));
            var popupHeight = MathF.Max(1f, MathF.Min(OmniTheme.Scale(360f), ImGui.GetMainViewport().WorkSize.Y * 0.8f));
            ImGui.SetNextWindowSizeConstraints(new Vector2(0f, popupHeight), new Vector2(float.MaxValue, popupHeight));
            using (var combo = ImRaii.Combo("购买物品", selected is null ? "请选择购买物品" : $"{selected.Name}（{selected.Price} 票）"))
            {
                if (combo)
                {
                    ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(4f)));
                    if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
                    var searchChanged = OmniControls.InputTextWithHint(
                        "##scripProductSearch", "输入物品名称搜索", ref scripProductSearch, 128,
                        ImGui.GetContentRegionAvail().X);
                    ImGui.Separator();
                    var productSelected = false;
                    // Keep the search field visible while only the results scroll.
                    using (var results = ImRaii.Child("##scripProductResults",
                               new Vector2(0f, MathF.Max(1f, ImGui.GetContentRegionAvail().Y))))
                    {
                        if (results)
                        {
                            if (searchChanged) ImGui.SetScrollY(0f);
                            var hasMatches = false;
                            ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(4f)));
                            foreach (var product in scripProducts)
                            {
                                if (!MatchesScripSearch(product.Name, scripProductSearch)) continue;
                                hasMatches = true;
                                if (OmniControls.RoundedSelectable($"{product.Name}（{product.Price} 票）##{product.ShopID}-{product.ItemID}",
                                        selected == product, size: new Vector2(0f, OmniTheme.SmallButtonSize().Y)))
                                {
                                    config.ScripShopID = product.ShopID;
                                    config.ScripItemID = product.ItemID;
                                    selected = product;
                                    config.ScripQuantity = Math.Min(Math.Max(1, config.ScripQuantity), Math.Max(1, GetConfiguredScripLimit(product)));
                                    changed = true;
                                    productSelected = true;
                                }
                            }
                            if (!hasMatches) ImGui.TextDisabled("没有匹配的物品");
                        }
                    }
                    if (productSelected) ImGui.CloseCurrentPopup();
                }
            }

            if (!string.IsNullOrEmpty(scripCatalogError)) ImGui.TextWrapped(scripCatalogError);
            var limit = selected is null ? 0 : GetConfiguredScripLimit(selected);
            ImGui.TextUnformatted($"每次购买数量（按触发票数最多 {limit} 个）");
            // Configuration uses the trigger budget; live currency/capacity is checked only when buying.
            var quantity = limit == 0 ? 0 : Math.Clamp(config.ScripQuantity, 1, limit);
            if (limit > 0 && quantity != config.ScripQuantity)
            {
                config.ScripQuantity = quantity;
                changed = true;
            }
            ImGui.SetNextItemWidth(OmniTheme.Scale(160f));
            using (ImRaii.Disabled(limit == 0))
            {
                if (OmniControls.InputInt("##scripQuantity", ref quantity) && limit > 0)
                    config.ScripQuantity = Math.Clamp(quantity, 1, limit);
                changed |= ImGui.IsItemDeactivatedAfterEdit();
            }
            if (selected is not null)
                ImGui.TextUnformatted($"预计花费：{(long)quantity * selected.Price} 票");
        }
        return changed;
    }

    private int GetConfiguredScripLimit(ScripProduct product) =>
        CalculateConfiguredScripLimit(config.ScripThreshold, product.Price, product.Unique);

    private static int CalculateConfiguredScripLimit(int threshold, int price, bool unique) =>
        CalculateScripLimit(Math.Clamp(threshold, 1, 10000), price, int.MaxValue, unique, 0);

    private static int CalculateScripLimit(int balance, int price, int capacity, bool unique, int owned)
    {
        if (balance < 0 || price <= 0 || capacity <= 0 || owned < 0) return 0;
        var limit = Math.Min(Math.Clamp(balance, 0, 10000) / price, capacity);
        return unique ? Math.Min(limit, owned == 0 ? 1 : 0) : limit;
    }

    private static unsafe int GetScripPurchaseLimit(ScripProduct product)
    {
        var capacity = 0;
        var owned = 0;
        foreach (var type in MainInventoryTypes)
        {
            foreach (ref readonly var item in OmenTools.DService.Instance().GameInventory.GetInventoryItems(type))
            {
                if (item.IsEmpty) capacity += product.StackSize;
                else if (item.BaseItemId == product.ItemID)
                {
                    owned += item.Quantity;
                    if (!item.IsHq) capacity += Math.Max(0, product.StackSize - item.Quantity);
                }
            }
        }
        if (product.Unique)
        {
            var inventory = InventoryManager.Instance();
            if (inventory == null) return 0;
            owned = Math.Max(owned, inventory->GetInventoryItemCount(product.ItemID, false, true, true, 0) +
                                    inventory->GetInventoryItemCount(product.ItemID, true, true, true, 0));
        }
        return CalculateScripLimit((int)ReadSkybuildersScrips(), product.Price, capacity, product.Unique, owned);
    }

    private bool IsScripPurchasePhase() => phase is AutomationPhase.PrepareScripPurchase or AutomationPhase.OpenScripShop
        or AutomationPhase.BuyScripItem or AutomationPhase.VerifyScripPurchase or AutomationPhase.CloseScripShop;

    private bool TryBeginAutoScripPurchase()
    {
        if (!config.AutoBuyScrips || purchasedGeneration == turnInGeneration ||
            ReadSkybuildersScrips() < Math.Clamp(config.ScripThreshold, 1, 10000)) return false;
        purchasedGeneration = turnInGeneration;
        BeginScripPurchase();
        return true;
    }

    private unsafe void BeginScripPurchase()
    {
        EnsureScripCatalog();
        var product = SelectedScripProduct();
        if (product is null)
        {
            FailAutomation("请先选择振兴票购买物品。");
            return;
        }
        var amount = Math.Min(GetConfiguredScripLimit(product),
            Math.Min(Math.Max(1, config.ScripQuantity), GetScripPurchaseLimit(product)));
        if (amount <= 0)
        {
            FailAutomation("无法购买所选物品：触发票数或余额不足、背包已满或已持有唯一物品。");
            return;
        }
        pendingScripProduct = product;
        scripRemaining = amount;
        scripPurchased = 0;
        scripVouchersBefore = TryReadVoucherCount(out var vouchers, out _) ? vouchers : -1;
        scripEventOwned = scripEventEnded = false;
        running = true;
        EnterPhase(AutomationPhase.PrepareScripPurchase);
        nextCheckAt = nextActionAt = DateTime.UtcNow;
        status = "暂停提交，准备购买振兴票商品";
    }

    private static unsafe bool IsScripShopActive()
    {
        var agent = AgentShop.Instance();
        return agent != null && agent->IsAgentActive();
    }

    private static unsafe bool HasScripTransactionDialog() =>
        GetAddon("ShopExchangeCurrencyDialog") != null || GetAddon("SelectYesno") != null;

    private unsafe void DriveScripPurchase()
    {
        var services = OmenTools.DService.Instance();
        var product = pendingScripProduct;
        if (product is null || !services.ClientState.IsLoggedIn ||
            services.ClientState.TerritoryType != FIRMAMENT_TERRITORY_ID ||
            services.Condition[ConditionFlag.BetweenAreas] || services.Condition[ConditionFlag.BetweenAreas51] ||
            services.Condition[ConditionFlag.InCombat])
        {
            FailAutomation("当前状态无法继续购买，已停止。");
            return;
        }
        if (PhaseTimedOut(TimeSpan.FromSeconds(20)))
        {
            FailAutomation(phase == AutomationPhase.VerifyScripPurchase
                ? "未能确认购买结果，已停止以避免重复购买。请检查振兴票和背包。"
                : "购买流程等待超时，已停止。请检查游戏提示。");
            return;
        }
        if (DateTime.UtcNow < nextActionAt) return;
        switch (phase)
        {
            case AutomationPhase.PrepareScripPurchase:
                CloseAddon("Request");
                CloseAddon("HWDSupply");
                if (IsOccupied() || GetAddon("HWDSupply") != null || GetAddon("Request") != null) return;
                if (!IsPlayerMovable() || IsScripShopActive() || HasScripTransactionDialog()) return;
                var player = services.ObjectTable.LocalPlayer;
                if (player is null) return;
                // Directly start the SpecialShop event without pathfinding or NPC interaction.
                scripEventOwned = true;
                var entityID = ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)player.Address)->EntityId;
                new EventStartPackt(entityID, product.ShopID).Send();
                EnterPhase(AutomationPhase.OpenScripShop);
                nextActionAt = DateTime.UtcNow.AddMilliseconds(800);
                status = "正在打开振兴票商店";
                break;
            case AutomationPhase.OpenScripShop:
                if (!IsScripShopActive()) return;
                EnterPhase(AutomationPhase.BuyScripItem);
                nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
                break;
            case AutomationPhase.BuyScripItem:
                if (HasScripTransactionDialog()) return;
                var agent = AgentShop.Instance();
                if (agent == null || !agent->IsAgentActive() || agent->ItemReceive == null) return;
                scripSelectedIndex = -1;
                for (var i = 0; i < agent->ItemReceiveSpan.Length; i++)
                    if (agent->ItemReceiveSpan[i].ItemId == product.ItemID) { scripSelectedIndex = i; break; }
                if (scripSelectedIndex < 0) return;
                scripBatch = Math.Min(Math.Min(scripRemaining, GetScripPurchaseLimit(product)), Math.Min(99, product.StackSize));
                if (scripBatch <= 0)
                {
                    FailAutomation("购买条件已变化，余额或背包空间不足，已停止。");
                    return;
                }
                scripBalanceBefore = ReadSkybuildersScrips();
                scripItemCountBefore = ReadInventory(product.ItemID).ItemCount;
                scripQuantitySent = scripConfirmSent = false;
                // Transition before dispatch: another plugin may confirm synchronously.
                EnterPhase(AutomationPhase.VerifyScripPurchase);
                AgentId.Shop.SendEvent(1, 0, scripSelectedIndex, scripBatch, 0);
                status = $"正在购买：{product.Name} × {scripBatch}";
                nextActionAt = DateTime.UtcNow.AddMilliseconds(400);
                break;
            case AutomationPhase.VerifyScripPurchase:
                if (ScripPurchaseApplied(scripBalanceBefore, ReadSkybuildersScrips(), scripItemCountBefore,
                        ReadInventory(product.ItemID).ItemCount, product.Price, scripBatch))
                {
                    scripPurchased += scripBatch;
                    scripRemaining -= scripBatch;
                    EnterPhase(scripRemaining > 0 ? AutomationPhase.BuyScripItem : AutomationPhase.CloseScripShop);
                    nextActionAt = DateTime.UtcNow.AddMilliseconds(800);
                    status = $"已购买：{product.Name} × {scripPurchased}";
                    return;
                }
                ConfirmScripPurchase(product);
                break;
            case AutomationPhase.CloseScripShop:
                EndScripEvent();
                if (IsScripShopActive() || IsOccupied() || HasScripTransactionDialog() || GetAddon("ShopExchangeCurrency") != null) return;
                var summary = $"已购买：{product.Name} × {scripPurchased}";
                pendingScripProduct = null;
                scripEventOwned = false;
                var recipe = FindRecipe(activeRecipeID);
                if (recipe is null) { FailAutomation("生产配置已变化，请重新启动。"); return; }
                if (scripVouchersBefore >= config.TicketThreshold)
                {
                    ticketsToPlay = scripVouchersBefore;
                    EnterPhase(AutomationPhase.MoveToLottery);
                    status = "购买完成，继续库啵好运道";
                }
                else if (ReadInventory(activeItemID).ItemCount > 0 || scripVouchersBefore < 0)
                {
                    EnterPhase(AutomationPhase.MoveToAppraiser);
                    status = "购买完成，继续提交物品";
                }
                else BeginNextCraftingCycle("振兴票购买完成");
                break;
        }
    }

    private static bool ScripPurchaseApplied(uint beforeBalance, uint nowBalance, int beforeItems, int nowItems, int price, int quantity) =>
        price > 0 && quantity > 0 && beforeBalance >= (long)price * quantity &&
        nowBalance == beforeBalance - (long)price * quantity && nowItems == beforeItems + (long)quantity;

    private unsafe void ConfirmScripPurchase(ScripProduct product)
    {
        var agent = AgentShop.Instance();
        if (phase != AutomationPhase.VerifyScripPurchase || !scripEventOwned || scripEventEnded || agent == null || !agent->IsAgentActive() ||
            agent->SelectedItemIndex != scripSelectedIndex || scripSelectedIndex < 0 ||
            scripSelectedIndex >= agent->ItemReceiveSpan.Length ||
            agent->ItemReceiveSpan[scripSelectedIndex].ItemId != product.ItemID) return;
        var dialog = GetAddon("ShopExchangeCurrencyDialog");
        if (!scripQuantitySent && dialog != null && dialog->IsReady)
        {
            scripQuantitySent = true;
            dialog->Callback(0, scripBatch);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(400);
            return;
        }
        var yesNo = (AddonSelectYesno*)GetAddon("SelectYesno");
        if (!scripConfirmSent && yesNo != null && yesNo->YesButton != null && yesNo->YesButton->IsEnabled)
        {
            // The prompt and item label can be separate nodes and vary by client language.
            // Match the pending transaction and selected item ID above, not localized dialog text.
            scripConfirmSent = true;
            if (!ClickButton((AtkUnitBase*)yesNo, yesNo->YesButton)) scripConfirmSent = false;
            nextActionAt = DateTime.UtcNow.AddMilliseconds(400);
        }
    }

    private unsafe void EndScripEvent()
    {
        if (!scripEventOwned || scripEventEnded || pendingScripProduct is not { } product) return;
        scripEventEnded = true;
        new EventCompletePackt(product.ShopID, 0).Send();
        CloseAddon("ShopExchangeCurrencyDialog");
        CloseAddon("ShopExchangeCurrency");
    }

    private void CleanupScripPurchase()
    {
        if (scripEventOwned)
        {
            try { EndScripEvent(); }
            catch (Exception ex)
            {
                // Log cleanup failures without retrying the purchase or hiding the original error.
                DalamudServices.PluginLog.Warning(ex, "AutoIshgardRestoration: failed to close owned scrip shop event.");
            }
        }
        scripEventOwned = false;
        pendingScripProduct = null;
    }

    private static unsafe uint ReadSkybuildersScrips()
    {
        var manager = CurrencyManager.Instance();
        return manager == null ? 0 : manager->GetItemCount(SKYBUILDERS_SCRIP_ITEM_ID);
    }

    private static unsafe bool ClickButton(AtkUnitBase* addon, AtkComponentButton* button)
    {
        if (addon == null || button == null)
        {
            return false;
        }

        var node = button->AtkComponentBase.OwnerNode;
        if (node == null)
        {
            return false;
        }

        var registered = node->AtkResNode.AtkEventManager.Event;
        if (registered == null)
        {
            return false;
        }

        var evt = default(AtkEvent);
        evt.Target = (AtkEventTarget*)node;
        evt.Listener = (AtkEventListener*)addon;
        var eventData = default(AtkEventData);
        addon->ReceiveEvent(registered->State.EventType, (int)registered->Param, &evt, &eventData);
        return true;
    }

    private static unsafe uint GetBaseID(nint address) =>
        address == nint.Zero ? 0 : ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)address)->BaseId;

    private static unsafe bool SendNPCEventStart(nint address)
    {
        var gameObject = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)address;
        if (gameObject == null || !TryResolveNPCEventID(gameObject, out var eventID))
        {
            return false;
        }

        var objectID = gameObject->GetGameObjectId();
        new EventStartPackt(objectID, eventID).Send();
        return true;
    }

    private static unsafe bool TryResolveNPCEventID(
        FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject* gameObject,
        out GameEventID eventID)
    {
        eventID = default;

        if (gameObject->EventId.Id != 0)
        {
            eventID = gameObject->EventId;
            if (eventID.ContentId == GameEventHandlerContent.HwdDev)
            {
                return true;
            }
        }

        if (gameObject->EventHandler != null)
        {
            var primary = gameObject->EventHandler->GetEventId();
            if (primary.Id != 0)
            {
                if (primary.ContentId == GameEventHandlerContent.HwdDev)
                {
                    eventID = primary;
                    return true;
                }

                if (eventID.Id == 0)
                {
                    eventID = primary;
                }
            }
        }

        var handlers = stackalloc GameEventHandler*[32];
        var handlerCount = Math.Clamp(gameObject->GetEventHandlersImpl(handlers), 0, 32);
        for (var index = 0; index < handlerCount; index++)
        {
            var handler = handlers[index];
            if (handler == null)
            {
                continue;
            }

            var candidate = handler->GetEventId();
            if (candidate.Id == 0)
            {
                continue;
            }

            if (candidate.ContentId == GameEventHandlerContent.HwdDev)
            {
                eventID = candidate;
                return true;
            }

            if (eventID.Id == 0)
            {
                eventID = candidate;
            }
        }

        return eventID.Id != 0;
    }

    private static bool IsOccupied()
    {
        var condition = OmenTools.DService.Instance().Condition;
        return condition[ConditionFlag.Occupied]
            || condition[ConditionFlag.OccupiedInEvent]
            || condition[ConditionFlag.OccupiedInQuestEvent]
            || condition[ConditionFlag.Occupied30]
            || condition[ConditionFlag.Occupied33]
            || condition[ConditionFlag.Occupied38]
            || condition[ConditionFlag.Occupied39];
    }

    private static unsafe bool IsPlayerMovable()
    {
        var services = OmenTools.DService.Instance();
        var player = services.ObjectTable.LocalPlayer;
        if (!services.ClientState.IsLoggedIn || player is null)
        {
            return false;
        }

        var condition = services.Condition;
        return !condition[ConditionFlag.BetweenAreas]
            && !condition[ConditionFlag.BetweenAreas51]
            && !condition[ConditionFlag.Occupied]
            && !condition[ConditionFlag.Occupied30]
            && !condition[ConditionFlag.Occupied33]
            && !condition[ConditionFlag.Occupied38]
            && !condition[ConditionFlag.Occupied39]
            && !condition[ConditionFlag.OccupiedInEvent]
            && !condition[ConditionFlag.OccupiedInQuestEvent]
            && !condition[ConditionFlag.OccupiedInCutSceneEvent]
            && !condition[ConditionFlag.OccupiedSummoningBell]
            && !condition[ConditionFlag.WatchingCutscene]
            && !condition[ConditionFlag.WatchingCutscene78]
            && !condition[ConditionFlag.Unconscious]
            && !condition[ConditionFlag.Casting]
            && !condition[ConditionFlag.Casting87]
            && !condition[ConditionFlag.Mounting]
            && !condition[ConditionFlag.Mounting71]
            && !condition[ConditionFlag.BeingMoved]
            && !condition[ConditionFlag.InThatPosition]
            && GetAddon("NowLoading") == null
            && GetAddon("FadeMiddle") == null
            && GetAddon("FadeBack") == null;
    }

    private static bool TrySendCommand(string command)
    {
        try
        {
            return OmenTools.DService.Instance().Command.ProcessCommand(command);
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "AutoIshgardRestoration: failed to send command.");
            return false;
        }
    }

    private static string FormatPosition(Vector3 position) =>
        $"{position.X.ToString("0.0", CultureInfo.InvariantCulture)}, " +
        $"{position.Y.ToString("0.0", CultureInfo.InvariantCulture)}, " +
        position.Z.ToString("0.0", CultureInfo.InvariantCulture);

    private static unsafe AtkUnitBase* GetAddon(string name)
    {
        var addon = OmenTools.DService.Instance().GameGUI.GetAddonByName<AtkUnitBase>(name);
        return addon != null && addon->IsVisible ? addon : null;
    }

    private static unsafe void CloseAddon(string name)
    {
        var addon = GetAddon(name);
        if (addon != null)
        {
            addon->Close(true);
        }
    }

    private static unsafe int FindSupplyRow(AtkUnitBase* addon, uint itemID)
    {
        var target = itemID + 500000;
        const int INDEX_OFFSET = 18;
        for (var i = 0; i + INDEX_OFFSET < addon->AtkValuesCount; i++)
        {
            ref var value = ref addon->AtkValues[i];
            if (value.Type != ValueType.UInt || value.UInt != target)
            {
                continue;
            }

            ref var index = ref addon->AtkValues[i + INDEX_OFFSET];
            return index.Type == ValueType.UInt ? (int)index.UInt : -1;
        }

        return -1;
    }

    private static unsafe bool TryReadVoucherCount(out int current, out int limit)
    {
        current = -1;
        limit = -1;
        var addon = GetAddon("HWDSupply");
        if (addon == null)
        {
            return false;
        }

        for (var i = 0; i < addon->AtkValuesCount; i++)
        {
            ref var value = ref addon->AtkValues[i];
            if (value.Type != ValueType.String || value.String.Value == null)
            {
                continue;
            }

            var text = Marshal.PtrToStringUTF8((nint)value.String.Value);
            if (text is null)
            {
                continue;
            }

            var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            var match = VOUCHER_PATTERN.Match(normalized);
            if (match.Success
                && int.TryParse(match.Groups[1].Value, out var count)
                && int.TryParse(match.Groups[2].Value, out var parsedLimit)
                && parsedLimit == 10)
            {
                current = count;
                limit = 10;
                return true;
            }
        }

        return false;
    }

    private void UpdateVoucherCount(DateTime now)
    {
        if (now < nextVoucherReadAt)
        {
            return;
        }

        nextVoucherReadAt = now.AddMilliseconds(500);
        TryReadVoucherCount(out voucherCount, out voucherLimit);
    }

    private static string FormatVoucherCount(int count, int limit) =>
        count >= 0 && limit >= 0 ? $"{count}/{limit}" : "未识别";

    private void EnterPhase(AutomationPhase next)
    {
        phase = next;
        phaseStartedAt = DateTime.UtcNow;
    }

    private bool PhaseTimedOut(TimeSpan timeout) => DateTime.UtcNow - phaseStartedAt > timeout;

    private void CompleteAutomation(string message)
    {
        running = false;
        phase = AutomationPhase.Idle;
        activeRecipeID = 0;
        activeItemID = 0;
        ticketsToPlay = 0;
        lotteryScratched = false;
        lotteryTicketCounted = false;
        lotteryScratchAttempts = 0;
        craftCycleStartingItemCount = 0;
        movableSince = null;
        initialDestination = default;
        status = message;
    }

    private void FailAutomation(string message)
    {
        CleanupScripPurchase();
        StopOwnedArtisan();
        StopOwnedVnavPath();

        running = false;
        phase = AutomationPhase.Idle;
        lastError = message;
        status = "自动流程已停止";
    }

    private void StopOwnedArtisan()
    {
        if (!artisanStartedByModule)
        {
            return;
        }

        try
        {
            if (getStopRequest?.InvokeFunc() != true)
            {
                setStopRequest?.InvokeAction(true);
            }
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "AutoIshgardRestoration: failed to stop owned Artisan production.");
            lastError = $"停止 Artisan 失败：{ex.Message}";
        }
        finally
        {
            artisanStartedByModule = false;
        }
    }

    private void StopOwnedVnavPath()
    {
        if (!vnavPathStartedByModule)
        {
            return;
        }

        try
        {
            vnavmeshIPC.StopPathfind();
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "AutoIshgardRestoration: failed to stop owned navigation.");
            lastError = $"停止导航失败：{ex.Message}";
        }
        finally
        {
            vnavPathStartedByModule = false;
        }
    }

    private bool ShouldStop(InventorySnapshot snapshot, out string reason)
    {
        if (config.StopMode == 0 && snapshot.FreeSlots <= config.MinimumFreeSlots)
        {
            reason = $"背包只剩 {snapshot.FreeSlots} 格，已停止生产并准备提交";
            return true;
        }

        if (config.StopMode == 1 && snapshot.ItemCount >= config.TargetItemCount)
        {
            reason = $"目标物品已达到 {snapshot.ItemCount} 个，已停止生产并准备提交";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static InventorySnapshot ReadInventory(uint itemID)
    {
        var freeSlots = 0;
        var totalSlots = 0;
        var itemCount = 0;
        foreach (var type in MainInventoryTypes)
        {
            var items = OmenTools.DService.Instance().GameInventory.GetInventoryItems(type);
            totalSlots += items.Length;
            foreach (ref readonly var item in items)
            {
                if (item.IsEmpty)
                {
                    freeSlots++;
                }
                else if (item.BaseItemId == itemID)
                {
                    itemCount += item.Quantity;
                }
            }
        }

        return new InventorySnapshot(freeSlots, totalSlots, itemCount);
    }

    private void Fail(string message)
    {
        lastError = message;
        status = "无法开始";
    }

    private static RecipeOption? FindRecipe(uint recipeID)
    {
        foreach (var recipe in Recipes)
        {
            if (recipe.RecipeID == recipeID)
            {
                return recipe;
            }
        }

        return null;
    }

    private static string GetJobName(uint jobID) =>
        LuminaGetter.TryGetRow<ClassJob>(jobID, out var job)
            ? job.Name.ExtractText()
            : $"职业 #{jobID}";

    private static string FormatRecipe(RecipeOption recipe) =>
        $"{recipe.Level}级{(recipe.IsExpert ? "高难度" : string.Empty)} · {recipe.ItemName}" +
        (recipe.Level == 80 ? "（可以获得库啵好运章）" : string.Empty);

    private readonly record struct RecipeOption(
        uint RecipeID,
        uint ItemID,
        uint JobID,
        int Level,
        bool IsExpert,
        string ItemName);

    private readonly record struct InventorySnapshot(int FreeSlots, int TotalSlots, int ItemCount);

    private enum AutomationPhase
    {
        Idle,
        WaitForFirmament,
        WaitForPlayerMovable,
        MoveToInitialPoint,
        WaitArtisanStart,
        Crafting,
        WaitArtisanStop,
        MoveToAppraiser,
        OpenAppraiser,
        SelectJob,
        SelectItem,
        FillRequest,
        WaitTurnIn,
        WaitSupplyRefresh,
        MoveToLottery,
        OpenLottery,
        PlayLottery,
        PostLottery,
        PrepareNextCycle,
        PrepareScripPurchase,
        OpenScripShop,
        BuyScripItem,
        VerifyScripPurchase,
        CloseScripShop
    }
}

[Serializable]
public sealed class AutoIshgardRestorationConfig
{
    [JsonPropertyName("SelectedRecipeId")]
    public uint SelectedRecipeID { get; set; }

    public int StopMode { get; set; }

    public int MinimumFreeSlots { get; set; } = 10;

    public int TargetItemCount { get; set; } = 30;

    public int TicketThreshold { get; set; } = 5;

    public bool AutoBuyScrips { get; set; }

    public int ScripThreshold { get; set; } = 9000;

    public uint ScripShopID { get; set; }

    public uint ScripItemID { get; set; }

    public int ScripQuantity { get; set; } = 1;
}
