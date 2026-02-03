using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Windowing;
using Newtonsoft.Json;
using CastBarTranslator.Windows;

namespace CastBarTranslator;

public sealed unsafe class Plugin : IDalamudPlugin
{
    // Addon names for cast bar hooks
    private const string AddonTargetInfo = "_TargetInfo";
    private const string AddonTargetInfoCastBar = "_TargetInfoCastBar";
    private const string AddonFocusTargetInfo = "_FocusTargetInfo";

    // Text node IDs for each addon type
    private const uint NodeIdTargetInfo = 12;
    private const uint NodeIdTargetInfoCastBar = 4;
    private const uint NodeIdFocusTargetInfo = 5;

    // Data filenames for each language
    private static readonly Dictionary<GameLanguage, string> DataFilenames = new()
    {
        { GameLanguage.English, "actions_en.json" },
        { GameLanguage.Japanese, "actions_ja.json" },
        { GameLanguage.German, "actions_de.json" },
        { GameLanguage.French, "actions_fr.json" },
        { GameLanguage.ChineseTraditional, "actions_zhtw.json" },
    };

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;

    public Configuration Configuration { get; init; }

    // Memory management for unmanaged string allocation
    private IntPtr _lastAllocatedStringPtr = IntPtr.Zero;

    // Data sources for languages (all loaded from JSON)
    private Dictionary<uint, string>? _topLanguageMap;
    private Dictionary<uint, string>? _bottomLanguageMap;

    // Window system for configuration UI
    private readonly WindowSystem _windowSystem = new("CastBarTranslator");
    private readonly ConfigWindow _configWindow;

    // Public properties for ConfigWindow to access data state
    public bool IsDataLoaded => _topLanguageMap != null && _bottomLanguageMap != null;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Initialize config window
        _configWindow = new ConfigWindow(this);
        _windowSystem.AddWindow(_configWindow);

        // Initialize data based on config
        ReloadDataSources();

        // Register cast bar hooks (both Pre and Post to ensure our text stays)
        AddonLifecycle.RegisterListener(AddonEvent.PreDraw, AddonTargetInfo, OnAddonDraw);
        AddonLifecycle.RegisterListener(AddonEvent.PreDraw, AddonTargetInfoCastBar, OnAddonDraw);
        AddonLifecycle.RegisterListener(AddonEvent.PreDraw, AddonFocusTargetInfo, OnAddonDraw);
        AddonLifecycle.RegisterListener(AddonEvent.PostDraw, AddonTargetInfo, OnAddonDraw);
        AddonLifecycle.RegisterListener(AddonEvent.PostDraw, AddonTargetInfoCastBar, OnAddonDraw);
        AddonLifecycle.RegisterListener(AddonEvent.PostDraw, AddonFocusTargetInfo, OnAddonDraw);

        // Register UI handlers
        PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += () => _configWindow.IsOpen = true;

        Log.Information("Cast Bar Translator loaded.");
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        _windowSystem.RemoveAllWindows();
        AddonLifecycle.UnregisterListener(OnAddonDraw);
        FreeLastString();
    }

    /// <summary>
    /// Reloads translation data sources for both languages.
    /// </summary>
    public void ReloadDataSources()
    {
        _topLanguageMap = null;
        _bottomLanguageMap = null;

        try
        {
            _topLanguageMap = LoadLanguageData(Configuration.TopLanguage);
            _bottomLanguageMap = LoadLanguageData(Configuration.BottomLanguage);

            var topCount = _topLanguageMap?.Count ?? 0;
            var bottomCount = _bottomLanguageMap?.Count ?? 0;
            Log.Information($"Loaded: {Configuration.TopLanguage} ({topCount} entries) / {Configuration.BottomLanguage} ({bottomCount} entries)");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load language data");
            NotificationManager.AddNotification(new Dalamud.Interface.ImGuiNotification.Notification
            {
                Content = $"Failed to load language: {ex.Message}",
                Title = "Cast Bar Translator",
                Type = Dalamud.Interface.ImGuiNotification.NotificationType.Error,
            });
        }
    }

    private Dictionary<uint, string>? LoadLanguageData(GameLanguage language)
    {
        if (!DataFilenames.TryGetValue(language, out var filename))
        {
            Log.Warning($"No data file defined for language: {language}");
            return null;
        }

        var directory = PluginInterface.AssemblyLocation.Directory?.FullName;
        if (directory == null)
        {
            Log.Warning("Unable to determine plugin directory.");
            return null;
        }

        var path = Path.Combine(directory, filename);
        if (!File.Exists(path))
        {
            Log.Warning($"Data file not found: {path}");
            NotificationManager.AddNotification(new Dalamud.Interface.ImGuiNotification.Notification
            {
                Content = $"Missing file: {filename}",
                Title = "Cast Bar Translator",
                Type = Dalamud.Interface.ImGuiNotification.NotificationType.Warning,
            });
            return null;
        }

        var json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<Dictionary<uint, string>>(json);
    }

    /// <summary>
    /// Reloads data from disk.
    /// </summary>
    public void ReloadData(bool showNotification)
    {
        ReloadDataSources();
        if (showNotification && IsDataLoaded)
        {
            NotificationManager.AddNotification(new Dalamud.Interface.ImGuiNotification.Notification
            {
                Content = "Data reloaded successfully.",
                Title = "Cast Bar Translator",
                Type = Dalamud.Interface.ImGuiNotification.NotificationType.Success,
            });
        }
    }

    private void OnAddonDraw(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)(nint)args.Addon;
        if (addon == null || !addon->IsVisible)
            return;

        // Identify addon type and get corresponding target/node ID
        IGameObject? target = null;
        uint textNodeId = 0;

        switch (args.AddonName)
        {
            case AddonTargetInfo:
                target = TargetManager.Target;
                textNodeId = NodeIdTargetInfo;
                break;
            case AddonTargetInfoCastBar:
                target = TargetManager.Target;
                textNodeId = NodeIdTargetInfoCastBar;
                break;
            case AddonFocusTargetInfo:
                target = TargetManager.FocusTarget;
                textNodeId = NodeIdFocusTargetInfo;
                break;
        }

        if (target is not IBattleChara battleChara || !battleChara.IsCasting)
            return;

        // Get text node
        var textNode = GetTextNodeById(addon, textNodeId);
        if (textNode == null)
            return;

        // Get action names in both languages
        var actionId = battleChara.CastActionId;
        var topName = GetActionName(actionId, _topLanguageMap);
        var bottomName = GetActionName(actionId, _bottomLanguageMap);

        // Skip if either translation is missing
        if (string.IsNullOrEmpty(topName) || string.IsNullOrEmpty(bottomName))
            return;

        // Skip if both names are the same
        if (topName == bottomName)
            return;

        // Combine names with newline
        var newText = $"{topName}\n{bottomName}";

        // Always set text (game resets it constantly)
        SetNodeText(textNode, newText);

        // Enable multiline and wordwrap flags for proper line breaking
        textNode->TextFlags |= TextFlags.MultiLine | TextFlags.WordWrap;

        // Always adjust height for two-line display
        var castBarHeight = Configuration.CastBarHeight;
        textNode->AtkResNode.SetHeight((ushort)castBarHeight);
    }

    private string GetActionName(uint actionId, Dictionary<uint, string>? languageMap)
    {
        if (languageMap != null && languageMap.TryGetValue(actionId, out var name))
        {
            return name;
        }
        return string.Empty;
    }

    private AtkTextNode* GetTextNodeById(AtkUnitBase* addon, uint nodeId)
    {
        if (addon->UldManager.NodeListCount <= nodeId)
            return null;

        var node = addon->GetNodeById(nodeId);
        if (node == null || node->Type != NodeType.Text)
            return null;

        return (AtkTextNode*)node;
    }

    private void SetNodeText(AtkTextNode* node, string text)
    {
        FreeLastString();

        var bytes = Encoding.UTF8.GetBytes(text + "\0");
        _lastAllocatedStringPtr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, _lastAllocatedStringPtr, bytes.Length);

        node->SetText((byte*)_lastAllocatedStringPtr);
    }

    private void FreeLastString()
    {
        if (_lastAllocatedStringPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_lastAllocatedStringPtr);
            _lastAllocatedStringPtr = IntPtr.Zero;
        }
    }
}
