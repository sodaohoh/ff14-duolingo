using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Lumina.Excel.GeneratedSheets;
using Newtonsoft.Json;
using Dalamud.Interface.Windowing; // Needed if you want to draw config UI

namespace SamplePlugin;

public sealed unsafe class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

    public Configuration Configuration { get; init; }

    // Memory Management
    private IntPtr _lastAllocatedStringPtr = IntPtr.Zero;
    private string _lastGeneratedString = string.Empty;

    // Data Sources
    private Lumina.Excel.ExcelSheet<Action>? _luminaActionSheet;
    private Dictionary<uint, string>? _externalActionMap;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Initialize Data based on Config
        ReloadDataSources();

        // Register Hooks
        AddonLifecycle.RegisterListener(AddonEvent.PreDraw, "_TargetInfo", OnAddonPreDraw);
        AddonLifecycle.RegisterListener(AddonEvent.PreDraw, "_TargetInfoCastBar", OnAddonPreDraw);
        AddonLifecycle.RegisterListener(AddonEvent.PreDraw, "_FocusTargetInfo", OnAddonPreDraw);

        // Register UI for settings
        PluginInterface.UiBuilder.Draw += DrawConfigUI;
        PluginInterface.UiBuilder.OpenConfigUi += () => _isConfigOpen = true;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= DrawConfigUI;
        AddonLifecycle.UnregisterListener(OnAddonPreDraw);
        FreeLastString();
    }

    // --- Configuration UI (Simple ImGui wrapper) ---
    private bool _isConfigOpen = false;
    private void DrawConfigUI()
    {
        if (!_isConfigOpen) return;

        if (ImGuiNET.ImGui.Begin("Translation Config", ref _isConfigOpen, ImGuiNET.ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGuiNET.ImGui.Text("Select Target Language:");

            var currentLang = Configuration.TargetLanguage;
            if (ImGuiNET.ImGui.BeginCombo("Language", currentLang.ToString()))
            {
                foreach (var lang in Enum.GetValues<TargetLanguage>())
                {
                    if (ImGuiNET.ImGui.Selectable(lang.ToString(), lang == currentLang))
                    {
                        Configuration.TargetLanguage = lang;
                        Configuration.Save();
                        ReloadDataSources(); // Reload data immediately on change
                    }
                }
                ImGuiNET.ImGui.EndCombo();
            }

            ImGuiNET.ImGui.TextDisabled("Note: 'ChineseTraditional' requires actions_zhtw.json");
            ImGuiNET.ImGui.End();
        }
    }

    // --- Data Management ---
    private void ReloadDataSources()
    {
        // Clear existing data
        _luminaActionSheet = null;
        _externalActionMap = null;

        try
        {
            switch (Configuration.TargetLanguage)
            {
                case TargetLanguage.English:
                    _luminaActionSheet = DataManager.Excel.GetSheet<Action>(Lumina.Data.Language.English);
                    break;
                case TargetLanguage.Japanese:
                    _luminaActionSheet = DataManager.Excel.GetSheet<Action>(Lumina.Data.Language.Japanese);
                    break;
                case TargetLanguage.German:
                    _luminaActionSheet = DataManager.Excel.GetSheet<Action>(Lumina.Data.Language.German);
                    break;
                case TargetLanguage.French:
                    _luminaActionSheet = DataManager.Excel.GetSheet<Action>(Lumina.Data.Language.French);
                    break;
                case TargetLanguage.ChineseTraditional:
                    LoadJsonData("actions_zhtw.json");
                    break;
            }
        }
        catch (Exception ex)
        {
            PluginInterface.UiBuilder.AddNotification($"Failed to load language: {ex.Message}", "Translation Plugin", Dalamud.Interface.ImGuiNotification.NotificationType.Error);
        }
    }

    private void LoadJsonData(string filename)
    {
        var path = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, filename);
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            _externalActionMap = JsonConvert.DeserializeObject<Dictionary<uint, string>>(json);
        }
        else
        {
            PluginInterface.UiBuilder.AddNotification($"Missing file: {filename}", "Translation Plugin", Dalamud.Interface.ImGuiNotification.NotificationType.Warning);
        }
    }

    // --- Core Logic ---

    private void OnAddonPreDraw(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon;
        if (addon == null || !addon->IsVisible) return;

        // 1. Identify Addon & Node
        IGameObject? target = null;
        uint textNodeId = 0;

        switch (args.AddonName)
        {
            case "_TargetInfo": target = TargetManager.Target; textNodeId = 12; break;
            case "_TargetInfoCastBar": target = TargetManager.Target; textNodeId = 4; break;
            case "_FocusTargetInfo": target = TargetManager.FocusTarget; textNodeId = 5; break;
        }

        if (target is not IBattleChara battleChara || !battleChara.IsCasting) return;

        // 2. Get Node
        var textNode = GetTextNodeById(addon, textNodeId);
        if (textNode == null) return;

        // 3. Get Original Text
        var originalText = Marshal.PtrToStringUTF8((IntPtr)textNode->NodeText.StringPtr);
        if (string.IsNullOrEmpty(originalText)) return;

        // 4. Cache Check (Performance)
        if (originalText == _lastGeneratedString)
        {
            // Enforce height
            if (textNode->AtkResNode.Height != 44) textNode->AtkResNode.SetHeight(44);
            return;
        }

        // 5. Lookup Translation
        string translatedName = GetTranslatedActionName(battleChara.CastActionId);

        // If translation missing or same as original, skip
        if (string.IsNullOrEmpty(translatedName) || originalText.Contains(translatedName)) return;

        // 6. Combine & Write
        var newText = $"{translatedName}\n{originalText}";
        SetNodeTextSafe(textNode, newText);

        // 7. Adjust Layout
        if (textNode->AtkResNode.Height < 44)
        {
            textNode->AtkResNode.SetHeight(44);
        }
    }

    private string GetTranslatedActionName(uint actionId)
    {
        // Strategy: Check Dictionary first (if loaded), then check Lumina (if loaded)

        // Case 1: External JSON (Chinese)
        if (_externalActionMap != null)
        {
            return _externalActionMap.TryGetValue(actionId, out var name) ? name : string.Empty;
        }

        // Case 2: Internal Game Data (EN/JA/DE/FR)
        if (_luminaActionSheet != null)
        {
            var row = _luminaActionSheet.GetRow(actionId);
            return row?.Name.RawString ?? string.Empty;
        }

        return string.Empty;
    }

    private AtkTextNode* GetTextNodeById(AtkUnitBase* addon, uint nodeId)
    {
        if (addon->UldManager.NodeListCount <= nodeId) return null;
        var node = addon->GetNodeById(nodeId);
        if (node == null || node->Type != NodeType.Text) return null;
        return (AtkTextNode*)node;
    }

    private void SetNodeTextSafe(AtkTextNode* node, string text)
    {
        FreeLastString();
        byte[] stringBytes = Encoding.UTF8.GetBytes(text + "\0");
        _lastAllocatedStringPtr = Marshal.AllocHGlobal(stringBytes.Length);
        Marshal.Copy(stringBytes, 0, _lastAllocatedStringPtr, stringBytes.Length);
        node->SetText((byte*)_lastAllocatedStringPtr);
        _lastGeneratedString = text;
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
