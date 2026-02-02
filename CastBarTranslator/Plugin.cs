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
using ActionSheet = Lumina.Excel.Sheets.Action;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
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

    // Data file for Chinese Traditional translations
    private const string ChineseDataFilename = "actions_zhtw.json";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;

    public Configuration Configuration { get; init; }

    // Memory management for unmanaged string allocation
    private IntPtr _lastAllocatedStringPtr = IntPtr.Zero;

    // Track last processed action to avoid redundant updates
    private uint _lastActionId = 0;

    // Data sources for top language (learning target)
    private Lumina.Excel.ExcelSheet<ActionSheet>? _topLuminaSheet;
    private Dictionary<uint, string>? _topExternalMap;

    // Data sources for bottom language (native/reference)
    private Lumina.Excel.ExcelSheet<ActionSheet>? _bottomLuminaSheet;
    private Dictionary<uint, string>? _bottomExternalMap;

    // Window system for configuration UI
    private readonly WindowSystem _windowSystem = new("CastBarTranslator");
    private readonly ConfigWindow _configWindow;

    // Public properties for ConfigWindow to access data state
    public bool IsChineseDataLoaded => _topExternalMap != null || _bottomExternalMap != null;
    public string ChineseDataSource => GetChineseDataPath();

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Initialize config window
        _configWindow = new ConfigWindow(this);
        _windowSystem.AddWindow(_configWindow);

        // Initialize data based on config
        ReloadDataSources();

        // Register cast bar hooks (PostDraw = every frame after game draws)
        AddonLifecycle.RegisterListener(AddonEvent.PostDraw, AddonTargetInfo, OnAddonPostDraw);
        AddonLifecycle.RegisterListener(AddonEvent.PostDraw, AddonTargetInfoCastBar, OnAddonPostDraw);
        AddonLifecycle.RegisterListener(AddonEvent.PostDraw, AddonFocusTargetInfo, OnAddonPostDraw);

        // Register UI handlers
        PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += () => _configWindow.IsOpen = true;

        Log.Information("Cast Bar Translator loaded.");
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        _windowSystem.RemoveAllWindows();
        AddonLifecycle.UnregisterListener(OnAddonPostDraw);
        FreeLastString();
    }

    /// <summary>
    /// Reloads translation data sources for both languages.
    /// </summary>
    public void ReloadDataSources()
    {
        // Clear existing data
        _topLuminaSheet = null;
        _topExternalMap = null;
        _bottomLuminaSheet = null;
        _bottomExternalMap = null;

        try
        {
            // Load top language (learning target)
            LoadLanguageData(Configuration.TopLanguage, out _topLuminaSheet, out _topExternalMap);

            // Load bottom language (native/reference)
            LoadLanguageData(Configuration.BottomLanguage, out _bottomLuminaSheet, out _bottomExternalMap);

            Log.Information($"Loaded languages: {Configuration.TopLanguage} (top) / {Configuration.BottomLanguage} (bottom)");
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

    private void LoadLanguageData(
        GameLanguage language,
        out Lumina.Excel.ExcelSheet<ActionSheet>? luminaSheet,
        out Dictionary<uint, string>? externalMap)
    {
        luminaSheet = null;
        externalMap = null;

        switch (language)
        {
            case GameLanguage.English:
                luminaSheet = DataManager.GetExcelSheet<ActionSheet>(Dalamud.Game.ClientLanguage.English);
                break;
            case GameLanguage.Japanese:
                luminaSheet = DataManager.GetExcelSheet<ActionSheet>(Dalamud.Game.ClientLanguage.Japanese);
                break;
            case GameLanguage.German:
                luminaSheet = DataManager.GetExcelSheet<ActionSheet>(Dalamud.Game.ClientLanguage.German);
                break;
            case GameLanguage.French:
                luminaSheet = DataManager.GetExcelSheet<ActionSheet>(Dalamud.Game.ClientLanguage.French);
                break;
            case GameLanguage.ChineseTraditional:
                externalMap = LoadChineseData();
                break;
        }
    }

    private Dictionary<uint, string>? LoadChineseData()
    {
        var directory = PluginInterface.AssemblyLocation.Directory?.FullName;
        if (directory == null)
        {
            Log.Warning("Unable to determine plugin directory for Chinese data.");
            return null;
        }

        var path = Path.Combine(directory, ChineseDataFilename);
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<Dictionary<uint, string>>(json);
        }
        else
        {
            Log.Warning($"Chinese data file not found: {path}");
            NotificationManager.AddNotification(new Dalamud.Interface.ImGuiNotification.Notification
            {
                Content = $"Missing file: {ChineseDataFilename}",
                Title = "Cast Bar Translator",
                Type = Dalamud.Interface.ImGuiNotification.NotificationType.Warning,
            });
            return null;
        }
    }

    /// <summary>
    /// Attempts to reload Chinese data from disk.
    /// </summary>
    public void CheckAndDownloadChineseData(bool showNotification)
    {
        ReloadDataSources();
        if (showNotification && IsChineseDataLoaded)
        {
            NotificationManager.AddNotification(new Dalamud.Interface.ImGuiNotification.Notification
            {
                Content = "Data reloaded successfully.",
                Title = "Cast Bar Translator",
                Type = Dalamud.Interface.ImGuiNotification.NotificationType.Success,
            });
        }
    }

    private string GetChineseDataPath()
    {
        var directory = PluginInterface.AssemblyLocation.Directory?.FullName;
        return directory != null ? Path.Combine(directory, ChineseDataFilename) : ChineseDataFilename;
    }

    private void OnAddonPostDraw(AddonEvent type, AddonArgs args)
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
        var topName = GetActionName(actionId, _topLuminaSheet, _topExternalMap);
        var bottomName = GetActionName(actionId, _bottomLuminaSheet, _bottomExternalMap);

        // Skip if either translation is missing
        if (string.IsNullOrEmpty(topName) || string.IsNullOrEmpty(bottomName))
            return;

        // Skip if both names are the same
        if (topName == bottomName)
            return;

        // Check if we already set the text (avoid flickering)
        var currentText = textNode->NodeText.ToString();
        if (currentText.Contains(bottomName))
            return; // Already has both languages

        // Combine names with SeString (FFXIV native text format)
        var seString = new SeStringBuilder()
            .AddText(topName)
            .Add(new NewLinePayload())
            .AddText(bottomName)
            .Build();

        SetNodeText(textNode, seString);

        // Adjust height for two-line display
        var castBarHeight = Configuration.CastBarHeight;
        if (textNode->AtkResNode.Height < castBarHeight)
        {
            textNode->AtkResNode.SetHeight((ushort)castBarHeight);
        }
    }

    private string GetActionName(
        uint actionId,
        Lumina.Excel.ExcelSheet<ActionSheet>? luminaSheet,
        Dictionary<uint, string>? externalMap)
    {
        // Check external data first (Chinese)
        if (externalMap != null)
        {
            return externalMap.TryGetValue(actionId, out var name) ? name : string.Empty;
        }

        // Check game data (EN/JP/DE/FR)
        if (luminaSheet != null && luminaSheet.TryGetRow(actionId, out var row))
        {
            return row.Name.ExtractText();
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

    private void SetNodeText(AtkTextNode* node, SeString seString)
    {
        FreeLastString();

        var encoded = seString.Encode();
        var bytes = new byte[encoded.Length + 1];
        encoded.CopyTo(bytes, 0);
        bytes[encoded.Length] = 0; // Null terminator

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
