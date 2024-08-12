using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using Lumina.Excel.GeneratedSheets;
using SamplePlugin.Windows;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Linq;
using System;
using System.Runtime.InteropServices;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;
using ImGuiNET;
using Dalamud.Interface;


namespace SamplePlugin;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;




    private const string CommandName = "/dc";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("SamplePlugin");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    public Plugin()
    {
        //AddonLifecycle.RegisterListener(AddonEvent.PreRefresh, "_InfoCastBar", OnTargetInfoCastBarRefresh);

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "A useful message to display in /xlhelp"
        });

        PluginInterface.UiBuilder.Draw += DrawUI;

        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;

        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;


        // 订阅更新事件
        Framework.Update += OnUpdateEvent;
    }

    public void Dispose()
    {
        //AddonLifecycle.UnregisterListener(AddonEvent.PreRefresh, "_CastBar", OnTargetInfoCastBarRefresh);

        Framework.Update -= OnUpdateEvent;
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    /*private unsafe void OnTargetInfoCastBarRefresh(AddonEvent type, AddonArgs args)
    {
        var targetCastBar = (AtkUnitBase*)args.Addon;

        AtkStage.Instance()->GetStringArrayData()[20]->SetValue(0, "test spell", false, true, false);
    }*/

    private void OnUpdateEvent(IFramework framework)
    {
        var target = TargetManager.Target;
        if (target != null && (target.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player || target.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc))
        {
            HandleCasting(target);
        }
    }

    private void HandleCasting(IGameObject gameObject)
    {
        // 从目标对象获取施法信息
        if(IsCasting(gameObject))
        {
            var actionId = GetCastingActionId(gameObject);
            if (actionId != 0)
            {
                DisplaySpellName(actionId);
            }
        } else
        {
            MainWindow.SetSpellNames("", "");

        }
    }

    private bool IsCasting(IGameObject gameObject)
    {
        if (gameObject is ICharacter character)
        {
            return ((byte)character.StatusFlags) >= ((byte)0x80);
        }
        return false;
    }

    private uint GetCastingActionId(IGameObject gameObject)
    {
        if (gameObject is IBattleChara character)
        {
            return character.CastActionId;
        }
        return 0;
    }

    private void DisplaySpellName(uint actionId)
    {
        var action = DataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.Action>()?.GetRow(actionId);
        var actionSheetEnglish = DataManager.Excel.GetSheet<Lumina.Excel.GeneratedSheets.Action>(Lumina.Data.Language.English);

        if (action != null)
        {
            var japaneseName = action.Name.ToString();  // 默认日文名称

            var englishName = actionSheetEnglish?.GetRow(action.RowId)?.Name.RawString;

            // 在主窗口中显示技能名称
            MainWindow.SetSpellNames(japaneseName, englishName);
        }
    }

    private void OnCommand(string command, string args)
    {
        ToggleMainUI();
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();
    public void ToggleMainUI() => MainWindow.Toggle();
}
