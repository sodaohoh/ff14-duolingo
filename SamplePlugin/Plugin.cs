using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Runtime.InteropServices;
using System.Text;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;

namespace SamplePlugin;

public sealed unsafe class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

    public Configuration Configuration { get; init; }

    // We keep a reference to the last string we allocated to free it later
    // to avoid memory leaks in unmanaged memory.
    private IntPtr _lastAllocatedStringPtr = IntPtr.Zero;
    private string _lastGeneratedString = string.Empty;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Register the PreDraw listener for the 3 potential cast bar addons
        AddonLifecycle.RegisterListener(AddonEvent.PreDraw, "_TargetInfo", OnAddonPreDraw);
        AddonLifecycle.RegisterListener(AddonEvent.PreDraw, "_TargetInfoCastBar", OnAddonPreDraw);
        AddonLifecycle.RegisterListener(AddonEvent.PreDraw, "_FocusTargetInfo", OnAddonPreDraw);
    }

    public void Dispose()
    {
        AddonLifecycle.UnregisterListener(OnAddonPreDraw);
        FreeLastString();
    }

    private void OnAddonPreDraw(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon;
        if (addon == null || !addon->IsVisible) return;

        // 1. Identify which addon we are dealing with and select the correct target & Node ID
        IGameObject? target = null;
        uint textNodeId = 0;

        switch (args.AddonName)
        {
            case "_TargetInfo":
                // Standard Target Frame (Merged) -> Node 12 is usually the cast text
                // We double check if the cast bar component inside it is actually active
                // But specifically for text modification, we just check if the node exists.
                target = TargetManager.Target;
                textNodeId = 12;
                break;

            case "_TargetInfoCastBar":
                // Split Target Cast Bar -> Node 4
                target = TargetManager.Target;
                textNodeId = 4;
                break;

            case "_FocusTargetInfo":
                // Focus Target -> Node 5
                target = TargetManager.FocusTarget;
                textNodeId = 5;
                break;
        }

        if (target is not IBattleChara battleChara || !battleChara.IsCasting) return;

        // 2. Get the specific text node
        var textNode = GetTextNodeById(addon, textNodeId);
        if (textNode == null) return;

        // 3. Get the current original text (Japanese/Game Language)
        // We read what the game thinks the text should be.
        var originalText = Marshal.PtrToStringUTF8((IntPtr)textNode->NodeText.StringPtr);
        if (string.IsNullOrEmpty(originalText)) return;

        // 4. Check if we already modified it to avoid flicker/infinite loops
        // If the text currently in the node matches what we generated last frame, don't touch it.
        // This is crucial for performance.
        if (originalText == _lastGeneratedString)
        {
            // Ensure height is still correct (game might reset it)
            if (textNode->AtkResNode.Height != 40) textNode->AtkResNode.SetHeight(40);
            return;
        }

        // 5. If the text is "pure" (not modified yet), let's translate it.
        // But wait! If the game updates the text to "Fire IV", our check above (originalText == _lastGeneratedString) fails.
        // So we proceed to generate the new string.

        // Get English name from Excel
        var englishName = GetEnglishActionName(battleChara.CastActionId);

        // If english name is missing or same as original, do nothing (or maybe just keep original)
        if (string.IsNullOrEmpty(englishName) || originalText.Contains(englishName)) return;

        // 6. Construct the new bilingual string
        // Format: English \n Japanese
        var newText = $"{englishName}\n{originalText}";

        // 7. Write to memory safely
        SetNodeTextSafe(textNode, newText);

        // 8. Adjust Layout
        // The default height is usually around 20-24. We need more for 2 lines.
        if (textNode->AtkResNode.Height < 40)
        {
            textNode->AtkResNode.SetHeight(40);
            // Optional: Adjust width or position if needed
            // textNode->AtkResNode.SetWidth(200);
        }

        // Optional: Adjust Line Spacing if the font supports it, or use SetScale if text is too big
        // textNode->LineSpacing = 20;
    }

    private string GetEnglishActionName(uint actionId)
    {
        // Using Lumina to fetch the English sheet
        var actionSheet = DataManager.Excel.GetSheet<Lumina.Excel.GeneratedSheets.Action>(Lumina.Data.Language.English);
        var action = actionSheet?.GetRow(actionId);
        return action?.Name.RawString ?? string.Empty;
    }

    // Helper to find a TextNode by ID safely
    private AtkTextNode* GetTextNodeById(AtkUnitBase* addon, uint nodeId)
    {
        // We use GetNodeById which returns AtkResNode*, then cast to AtkTextNode*
        // Check bounds based on SimpleTweaks logic
        if (addon->UldManager.NodeListCount <= nodeId) return null;

        var node = addon->GetNodeById(nodeId);
        if (node == null) return null;

        // Ensure it is actually a text node (Type 3 usually)
        if (node->Type != NodeType.Text) return null;

        return (AtkTextNode*)node;
    }

    // Safe memory setting for AtkTextNode
    private void SetNodeTextSafe(AtkTextNode* node, string text)
    {
        // 1. Free previous string to avoid leaks
        FreeLastString();

        // 2. Allocate new unmanaged memory for the string
        // FFXIV uses UTF-8 for UI text
        byte[] stringBytes = Encoding.UTF8.GetBytes(text + "\0"); // Null-terminated
        _lastAllocatedStringPtr = Marshal.AllocHGlobal(stringBytes.Length);
        Marshal.Copy(stringBytes, 0, _lastAllocatedStringPtr, stringBytes.Length);

        // 3. Point the node to our memory
        node->SetText((byte*)_lastAllocatedStringPtr);

        // 4. Cache the string so we know we did this
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
