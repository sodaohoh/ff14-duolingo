using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using CastBarTranslator.Presentation;
using CastBarTranslator.Translation;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CastBarTranslator.Features;
#if DEBUG
internal enum DebugCastMode
{
    Off,
    Long,
    Native,
}
#endif

public sealed unsafe class CastBarFeature : IDisposable
{
    private const string AddonTargetInfo = "_TargetInfo";
    private const string AddonTargetInfoCastBar = "_TargetInfoCastBar";
    private const string AddonFocusTargetInfo = "_FocusTargetInfo";

    private const uint NodeIdTargetInfo = 12;
    private const uint NodeIdTargetInfoCastBar = 4;
    private const uint NodeIdFocusTargetInfo = 5;
#if DEBUG
    private const string DebugJapaneseName = "これはレイアウト確認用の非常に長いアクション名称です";
    private const string DebugChineseName = "這是一個專門用來測試版面配置的超級長技能名稱";
#endif

    private readonly ITargetManager _targetManager;
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly Configuration _configuration;
    private readonly TranslationService _translationService;
    private readonly IPluginLog _log;
    private readonly Dictionary<nint, NativeNodeState> _modifiedNodeStates = new();
#if DEBUG
    private DebugCastMode _debugMode;
    private readonly Dictionary<NativeDiagnosticSlot, NativeDiagnosticState> _nativeDiagnostics = new();
#endif
    private IntPtr _lastAllocatedStringPtr;

    public CastBarFeature(
        ITargetManager targetManager,
        IAddonLifecycle addonLifecycle,
        Configuration configuration,
        TranslationService translationService,
        IPluginLog log)
    {
        _targetManager = targetManager;
        _addonLifecycle = addonLifecycle;
        _configuration = configuration;
        _translationService = translationService;
        _log = log;

        _addonLifecycle.RegisterListener(AddonEvent.PreDraw, AddonTargetInfo, OnAddonDraw);
        _addonLifecycle.RegisterListener(AddonEvent.PreDraw, AddonTargetInfoCastBar, OnAddonDraw);
        _addonLifecycle.RegisterListener(AddonEvent.PreDraw, AddonFocusTargetInfo, OnAddonDraw);
        _addonLifecycle.RegisterListener(AddonEvent.PostDraw, AddonTargetInfo, OnAddonDraw);
        _addonLifecycle.RegisterListener(AddonEvent.PostDraw, AddonTargetInfoCastBar, OnAddonDraw);
        _addonLifecycle.RegisterListener(AddonEvent.PostDraw, AddonFocusTargetInfo, OnAddonDraw);
    }

    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(OnAddonDraw);
        RestoreModifiedNodeStates();
        FreeLastString();
#if DEBUG
        _nativeDiagnostics.Clear();
#endif
    }
#if DEBUG
    internal void SetDebugMode(DebugCastMode mode)
    {
        if (_debugMode == mode)
            return;

        // A node contaminated by an older plugin instance cannot reveal its original height.
        RestoreModifiedNodeStates();
        _debugMode = mode;
        _nativeDiagnostics.Clear();
    }
#endif

    private void OnAddonDraw(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)(nint)args.Addon;
        if (addon == null || !addon->IsVisible)
            return;

        IGameObject? target;
        uint textNodeId;

        switch (args.AddonName)
        {
            case AddonTargetInfo:
                target = _targetManager.Target;
                textNodeId = NodeIdTargetInfo;
                break;
            case AddonTargetInfoCastBar:
                target = _targetManager.Target;
                textNodeId = NodeIdTargetInfoCastBar;
                break;
            case AddonFocusTargetInfo:
                target = _targetManager.FocusTarget;
                textNodeId = NodeIdFocusTargetInfo;
                break;
            default:
                return;
        }

        var textNode = GetTextNodeById(addon, textNodeId);
        if (textNode == null)
        {
#if DEBUG
            _nativeDiagnostics.Remove(new NativeDiagnosticSlot(args.AddonName, textNodeId));
#endif
            return;
        }

        // Restore state from the previous replacement before every independent cast.
        RestoreNodeState(textNode);

        if (target is not IBattleChara battleChara || !battleChara.IsCasting)
        {
#if DEBUG
            if (_debugMode == DebugCastMode.Native && type == AddonEvent.PostDraw)
                MarkNativeCastStopped(args.AddonName, textNodeId);
#endif
            return;
        }

        var actionId = battleChara.CastActionId;
#if DEBUG
        if (_debugMode == DebugCastMode.Native)
        {
            LogNativeState(args.AddonName, textNodeId, textNode, battleChara, actionId, type);
            return;
        }

        string? topName;
        string? bottomName;
        if (_debugMode == DebugCastMode.Long)
        {
            topName = DebugJapaneseName;
            bottomName = DebugChineseName;
        }
        else
        {
            topName = _translationService.GetActionName(actionId, _configuration.TopLanguage);
            bottomName = _translationService.GetActionName(actionId, _configuration.BottomLanguage);
        }
#else
        var topName = _translationService.GetActionName(actionId, _configuration.TopLanguage);
        var bottomName = _translationService.GetActionName(actionId, _configuration.BottomLanguage);
#endif
        var newText = CastBarTextFormatter.Format(topName, bottomName);
        if (newText == null)
            return;

        var originalFontSize = textNode->FontSize;
        var originalTextFlags = textNode->TextFlags;
        var replacementTextFlags =
            (originalTextFlags | TextFlags.MultiLine) &
            ~(TextFlags.WordWrap | TextFlags.Ellipsis);

        _modifiedNodeStates[(nint)textNode] = new NativeNodeState(
            originalFontSize,
            originalTextFlags,
            textNode->AtkResNode.Height);

        try
        {
            textNode->TextFlags = replacementTextFlags;

            var fittedFontSize = CastBarFontSizeFitter.SelectFontSize(
                originalFontSize,
                textNode->AtkResNode.Width,
                fontSize => GetWidestLineWidth(
                    textNode,
                    topName!,
                    bottomName!,
                    fontSize));

            textNode->FontSize = fittedFontSize;
            SetNodeText(textNode, newText);
            textNode->AtkResNode.SetHeight((ushort)_configuration.CastBarHeight);
        }
        catch
        {
            RestoreNodeState(textNode);
            throw;
        }
    }

#if DEBUG
    private void LogNativeState(
        string addonName,
        uint nodeId,
        AtkTextNode* node,
        IBattleChara battleChara,
        uint actionId,
        AddonEvent eventType)
    {
        // PostDraw follows FFXIV's addon draw; Native mode performs no presentation writes.
        if (eventType != AddonEvent.PostDraw)
            return;

        var slot = new NativeDiagnosticSlot(addonName, nodeId);
        var key = new NativeDiagnosticKey(
            addonName,
            nodeId,
            (nint)node,
            battleChara.Address,
            actionId);

        if (!_nativeDiagnostics.TryGetValue(slot, out var state))
        {
            // First PostDraw seeds a baseline. A fresh cast transition is required before logging.
            _nativeDiagnostics[slot] = new NativeDiagnosticState(key, false);
            return;
        }

        if (!state.SawCastStopped &&
            state.LastCast is { } previous &&
            previous == key)
        {
            return;
        }

        _nativeDiagnostics[slot] = new NativeDiagnosticState(key, false);


        var flags = node->TextFlags;
        _log.Information(
            $"Native cast node: " +
            $"Event={eventType}, " +
            $"Addon={addonName}, " +
            $"NodeId={nodeId}, " +
            $"Node={FormatAddress((nint)node)}, " +
            $"Target={FormatAddress(battleChara.Address)}, " +
            $"CastActionId={actionId}, " +
            $"Text=\"{GetNativeText(node)}\", " +
            $"FontSize={node->FontSize}, " +
            $"LineSpacing={node->LineSpacing}, " +
            $"CharSpacing={node->CharSpacing}, " +
            $"TextFlags=0x{(ushort)flags:X4} [{DescribeTextFlags(flags)}], " +
            $"Width={node->AtkResNode.Width}, " +
            $"Height={node->AtkResNode.Height}, " +
            $"ScaleX={node->AtkResNode.ScaleX}, " +
            $"ScaleY={node->AtkResNode.ScaleY}, " +
            $"FontType={node->FontType} (raw={(byte)node->FontType}), " +
            $"Alignment={node->AlignmentType} (raw={(byte)node->AlignmentType}), " +
            $"DrawUnscaled={GetNativeTextDrawSize(node, false)}, " +
            $"DrawScaled={GetNativeTextDrawSize(node, true)}");
    }
#if DEBUG
    private void MarkNativeCastStopped(string addonName, uint nodeId)
    {
        var slot = new NativeDiagnosticSlot(addonName, nodeId);
        if (_nativeDiagnostics.TryGetValue(slot, out var state))
        {
            _nativeDiagnostics[slot] = state with { SawCastStopped = true };
        }
        else
        {
            _nativeDiagnostics[slot] = new NativeDiagnosticState(null, true);
        }
    }
#endif

    private static string GetNativeText(AtkTextNode* node)
    {
        try
        {
            return EscapeNativeText(node->NodeText.ToString());
        }
        catch (Exception ex)
        {
            return $"<unavailable: {ex.GetType().Name}>";
        }
    }

    private static string GetNativeTextDrawSize(AtkTextNode* node, bool considerScale)
    {
        try
        {
            ushort width = 0;
            ushort height = 0;
            node->GetTextDrawSize(&width, &height, null, 0, -1, considerScale);
            return $"{width}x{height}";
        }
        catch (Exception ex)
        {
            return $"unavailable ({ex.GetType().Name})";
        }
    }

    private static string DescribeTextFlags(TextFlags flags)
    {
        var names = new List<string>();
        AddTextFlag(names, flags, TextFlags.AutoAdjustNodeSize, nameof(TextFlags.AutoAdjustNodeSize));
        AddTextFlag(names, flags, TextFlags.Bold, nameof(TextFlags.Bold));
        AddTextFlag(names, flags, TextFlags.Italic, nameof(TextFlags.Italic));
        AddTextFlag(names, flags, TextFlags.Edge, nameof(TextFlags.Edge));
        AddTextFlag(names, flags, TextFlags.Glare, nameof(TextFlags.Glare));
        AddTextFlag(names, flags, TextFlags.Emboss, nameof(TextFlags.Emboss));
        AddTextFlag(names, flags, TextFlags.WordWrap, nameof(TextFlags.WordWrap));
        AddTextFlag(names, flags, TextFlags.MultiLine, nameof(TextFlags.MultiLine));
        AddTextFlag(names, flags, TextFlags.OverflowHidden, nameof(TextFlags.OverflowHidden));
        AddTextFlag(names, flags, TextFlags.LinkData, nameof(TextFlags.LinkData));
        AddTextFlag(names, flags, TextFlags.Ellipsis, nameof(TextFlags.Ellipsis));
        AddTextFlag(names, flags, TextFlags.FixedFontResolution, nameof(TextFlags.FixedFontResolution));
        AddTextFlag(names, flags, TextFlags.FontCache, nameof(TextFlags.FontCache));

        var knownFlags =
            TextFlags.AutoAdjustNodeSize |
            TextFlags.Bold |
            TextFlags.Italic |
            TextFlags.Edge |
            TextFlags.Glare |
            TextFlags.Emboss |
            TextFlags.WordWrap |
            TextFlags.MultiLine |
            TextFlags.OverflowHidden |
            TextFlags.LinkData |
            TextFlags.Ellipsis |
            TextFlags.FixedFontResolution |
            TextFlags.FontCache;
        var unknownFlags = flags & ~knownFlags;
        if (unknownFlags != TextFlags.None)
            names.Add($"Unknown(0x{(ushort)unknownFlags:X4})");

        return names.Count == 0 ? "None" : string.Join("|", names);
    }

    private static void AddTextFlag(
        List<string> names,
        TextFlags flags,
        TextFlags flag,
        string name)
    {
        if ((flags & flag) != TextFlags.None)
            names.Add(name);
    }

    private static string EscapeNativeText(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\"", "\\\"");
    }

    private static string FormatAddress(nint address)
    {
        return $"0x{(long)address:X}";
    }

    private readonly record struct NativeDiagnosticSlot(string AddonName, uint NodeId);

    private readonly record struct NativeDiagnosticKey(
        string AddonName,
        uint NodeId,
        nint NodeAddress,
        nint TargetAddress,
        uint ActionId);
#endif

    private static AtkTextNode* GetTextNodeById(AtkUnitBase* addon, uint nodeId)
    {
        if (addon->UldManager.NodeListCount <= nodeId)
            return null;

        var node = addon->GetNodeById(nodeId);
        if (node == null || node->Type != NodeType.Text)
            return null;

        return (AtkTextNode*)node;
    }

    private static ushort GetWidestLineWidth(
        AtkTextNode* node,
        string topName,
        string bottomName,
        byte fontSize)
    {
        var topWidth = MeasureTextWidth(node, topName, fontSize);
        var bottomWidth = MeasureTextWidth(node, bottomName, fontSize);
        return topWidth >= bottomWidth ? topWidth : bottomWidth;
    }

    private static ushort MeasureTextWidth(AtkTextNode* node, string text, byte fontSize)
    {
        node->FontSize = fontSize;

        var byteCount = Encoding.UTF8.GetByteCount(text);
        Span<byte> utf8 = byteCount <= 512
            ? stackalloc byte[byteCount + 1]
            : new byte[byteCount + 1];
        Encoding.UTF8.GetBytes(text, utf8);
        utf8[byteCount] = 0;

        ushort width = 0;
        ushort height = 0;
        fixed (byte* textPointer = utf8)
        {
            node->GetTextDrawSize(&width, &height, textPointer);
        }

        return width;
    }

    private void RestoreNodeState(AtkTextNode* node)
    {
        if (!_modifiedNodeStates.Remove((nint)node, out var state))
            return;

        node->FontSize = state.FontSize;
        node->TextFlags = state.TextFlags;
        node->AtkResNode.SetHeight(state.Height);
    }

    private void RestoreModifiedNodeStates()
    {
        foreach (var pair in _modifiedNodeStates)
        {
            if (pair.Key == 0)
                continue;

            var node = (AtkTextNode*)pair.Key;
            node->FontSize = pair.Value.FontSize;
            node->TextFlags = pair.Value.TextFlags;
            node->AtkResNode.SetHeight(pair.Value.Height);
        }

        _modifiedNodeStates.Clear();
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
        if (_lastAllocatedStringPtr == IntPtr.Zero)
            return;

        Marshal.FreeHGlobal(_lastAllocatedStringPtr);
        _lastAllocatedStringPtr = IntPtr.Zero;
    }

    private readonly record struct NativeNodeState(byte FontSize, TextFlags TextFlags, ushort Height);
#if DEBUG
    private readonly record struct NativeDiagnosticState(
        NativeDiagnosticKey? LastCast,
        bool SawCastStopped);
#endif
}
