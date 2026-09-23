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
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CastBarTranslator.Features;

internal enum SecondNodeLifetime
{
    Alive,
    Cleaning,
    Destroying,
    Destroyed,
    Leaked,
}

internal enum SecondNodeCleanupReason
{
    PreFinalize,
    AddonReplacement,
    CreationFailure,
    Dispose,
}

public sealed unsafe class CastBarFeature : IDisposable
{
    private const string AddonTargetInfoCastBar = "_TargetInfoCastBar";
    private const string AddonFocusTargetInfo = "_FocusTargetInfo";

    private const uint NodeIdTargetInfoCastBar = 4;
    private const uint NodeIdFocusTargetInfo = 5;
    private const uint SecondNodeIdBase = 100_000_000;

    private readonly ITargetManager _targetManager;
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IGameGui _gameGui;
    private readonly TranslationService _translationService;
    private readonly IPluginLog _log;
    private readonly Dictionary<nint, SecondNodeState> _secondNodes = new();

    public CastBarFeature(
        ITargetManager targetManager,
        IAddonLifecycle addonLifecycle,
        IGameGui gameGui,
        Configuration configuration,
        TranslationService translationService,
        IPluginLog log)
    {
        _targetManager = targetManager;
        _addonLifecycle = addonLifecycle;
        _gameGui = gameGui;
        _ = configuration;
        _translationService = translationService;
        _log = log;

        _addonLifecycle.RegisterListener(AddonEvent.PreDraw, AddonTargetInfoCastBar, OnAddonDraw);
        _addonLifecycle.RegisterListener(AddonEvent.PreDraw, AddonFocusTargetInfo, OnAddonDraw);
        _addonLifecycle.RegisterListener(
            AddonEvent.PreFinalize,
            AddonTargetInfoCastBar,
            OnAddonFinalize);
        _addonLifecycle.RegisterListener(
            AddonEvent.PreFinalize,
            AddonFocusTargetInfo,
            OnAddonFinalize);
    }

    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(OnAddonDraw);
        _addonLifecycle.UnregisterListener(OnAddonFinalize);
        CleanupSecondNodes(SecondNodeCleanupReason.Dispose);
    }

    private void OnAddonFinalize(AddonEvent type, AddonArgs args)
    {
        var addonAddress = (nint)args.Addon;
        if (addonAddress != 0)
        {
            CleanupSecondNode(
                addonAddress,
                SecondNodeCleanupReason.PreFinalize,
                addonAddress);
        }
    }

    private void OnAddonDraw(AddonEvent type, AddonArgs args)
    {
        if (type != AddonEvent.PreDraw)
            return;

        var addon = (AtkUnitBase*)(nint)args.Addon;
        if (addon == null || !addon->IsVisible)
            return;

        IGameObject? target;
        uint textNodeId;
        switch (args.AddonName)
        {
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

        var nativeNode = GetTextNodeById(addon, textNodeId);
        if (nativeNode == null)
        {
            CleanupSecondNode(
                (nint)addon,
                SecondNodeCleanupReason.AddonReplacement);
            return;
        }

        UpdateSecondNode(args.AddonName, addon, nativeNode, target);
    }

    private void UpdateSecondNode(
        string addonName,
        AtkUnitBase* addon,
        AtkTextNode* nativeNode,
        IGameObject? target)
    {
        var addonAddress = (nint)addon;
        CleanupReplacedAddonStates(addonName, addonAddress);

        var parent = nativeNode->AtkResNode.ParentNode;
        if (parent == null)
        {
            CleanupSecondNode(addonAddress, SecondNodeCleanupReason.AddonReplacement);
            return;
        }

        if (_secondNodes.TryGetValue(addonAddress, out var state) &&
            (state.NativeNodePointer != (nint)nativeNode ||
             state.ParentPointer != (nint)parent))
        {
            CleanupSecondNode(addonAddress, SecondNodeCleanupReason.AddonReplacement);
            state = null;
        }

        if (state == null)
        {
            if (DetectOrphanedSecondNode(addon, parent, addonName))
                return;

            try
            {
                state = CreateSecondNode(addonName, addon, nativeNode, parent);
            }
            catch (Exception ex)
            {
                _log.Error(ex, $"Second node creation failed: Addon={addonName}.");
                return;
            }

            if (state == null)
                return;

            _secondNodes.Add(addonAddress, state);
        }

        var pluginNode = (AtkTextNode*)state.PluginNodePointer;
        var manager = (AtkUldManager*)state.UldManagerPointer;
        CopySecondNodeAppearance(nativeNode, pluginNode);

        var targetX = nativeNode->AtkResNode.X;
        var targetY = (short)(nativeNode->AtkResNode.Y + nativeNode->FontSize);
        var positionChanged =
            pluginNode->AtkResNode.X != targetX ||
            pluginNode->AtkResNode.Y != targetY;
        pluginNode->AtkResNode.X = targetX;
        pluginNode->AtkResNode.Y = targetY;

        var battleChara = target as IBattleChara;
        var chineseName = battleChara?.IsCasting == true
            ? _translationService.GetActionName(
                battleChara.CastActionId,
                GameLanguage.ChineseTraditional)
            : null;
        var shouldShow = !string.IsNullOrEmpty(chineseName) &&
                         battleChara?.IsCasting == true &&
                         nativeNode->AtkResNode.IsVisible();
        var visibilityChanged = SetSecondNodeVisibility(state, shouldShow);

        if (!shouldShow)
        {
            if (visibilityChanged)
            {
                pluginNode->AtkResNode.IsDirty = true;
                manager->UpdateDrawNodeList();
            }

            return;
        }

        var effectiveChineseName = chineseName!;
        var fittedFontSize = CastBarFontSizeFitter.SelectFontSize(
            nativeNode->FontSize,
            nativeNode->AtkResNode.Width,
            fontSize => MeasureTextWidth(pluginNode, effectiveChineseName, fontSize));
        var fontSizeChanged = state.LastAppliedFontSize != fittedFontSize;
        pluginNode->FontSize = fittedFontSize;
        state.LastAppliedFontSize = fittedFontSize;

        var textChanged = SetSecondNodeText(state, effectiveChineseName);
        if (fontSizeChanged || textChanged || positionChanged || visibilityChanged)
        {
            pluginNode->AtkResNode.IsDirty = true;
            manager->UpdateDrawNodeList();
        }
    }

    private void CleanupReplacedAddonStates(string addonName, nint currentAddon)
    {
        var replacedAddons = new List<nint>();
        foreach (var pair in _secondNodes)
        {
            if (pair.Value.AddonName == addonName && pair.Key != currentAddon)
                replacedAddons.Add(pair.Key);
        }

        foreach (var addonAddress in replacedAddons)
            CleanupSecondNode(addonAddress, SecondNodeCleanupReason.AddonReplacement);
    }

    private SecondNodeState? CreateSecondNode(
        string addonName,
        AtkUnitBase* addon,
        AtkTextNode* nativeNode,
        AtkResNode* parent)
    {
        var tail = parent->ChildNode;
        if (tail == nativeNode ||
            (tail != null && tail->NextSiblingNode == nativeNode))
        {
            _log.Warning(
                $"Second node creation skipped: Addon={addonName}, " +
                "Reason=NativeCastNodeAdjacentToInsertionTail");
            return null;
        }

        var manager = &addon->UldManager;
        if (!TryGetSecondNodeId(manager, out var nodeId))
        {
            _log.Warning(
                $"Second node creation skipped: Addon={addonName}, Reason=NoFreeNodeId");
            return null;
        }

        var uiSpace = IMemorySpace.GetUISpace();
        var pluginNode = uiSpace == null ? null : uiSpace->Create<AtkTextNode>();
        if (pluginNode == null)
        {
            _log.Warning(
                $"Second node creation skipped: Addon={addonName}, " +
                "Reason=CreateTextNodeFailed");
            return null;
        }

        var state = new SecondNodeState
        {
            AddonName = addonName,
            AddonPointer = (nint)addon,
            UldManagerPointer = (nint)manager,
            NativeNodePointer = (nint)nativeNode,
            ParentPointer = (nint)parent,
            PluginNodePointer = (nint)pluginNode,
            NodeId = nodeId,
        };

        if (!ValidateSecondNodeInitialState(pluginNode))
        {
            DestroyUnattachedSecondNode(state);
            _log.Warning(
                $"Second node creation skipped: Addon={addonName}, " +
                "Reason=InitialStateInvalid");
            return null;
        }

        try
        {
            pluginNode->AtkResNode.NodeId = nodeId;
            CopySecondNodeAppearance(nativeNode, pluginNode);
            AttachSecondNode(parent, pluginNode, state);

            var resolvedAddon = ResolveOwningAddon(&pluginNode->AtkResNode);
            state.ResolvedAddonPointer = (nint)resolvedAddon;
            state.OwnershipVerified = resolvedAddon == addon;
            if (!state.OwnershipVerified)
            {
                throw new InvalidOperationException(
                    "Plugin node did not resolve to expected owning addon.");
            }

            manager = &resolvedAddon->UldManager;
            state.UldManagerPointer = (nint)manager;
            if (!EnsureSecondNodeListCapacity(manager))
                throw new InvalidOperationException("Unable to expand ULD node list.");
            if (!AddSecondNodeToObjectList(manager, &pluginNode->AtkResNode))
                throw new InvalidOperationException("Unable to add node to ULD object list.");

            state.AddedToObjectList = true;
            pluginNode->AtkResNode.IsDirty = true;
            manager->UpdateDrawNodeList();

            if (!VerifySecondNodeAttachment(manager, parent, pluginNode, state))
            {
                throw new InvalidOperationException(
                    "Attached plugin node failed ownership verification.");
            }

            _log.Debug(
                $"Second node attached: Addon={addonName}, " +
                $"NodeId={nodeId}, Plugin={FormatAddress((nint)pluginNode)}");
            return state;
        }
        catch
        {
            CleanupSecondNodeState(
                state,
                SecondNodeCleanupReason.CreationFailure,
                callbackAddon: 0);
            throw;
        }
    }

    private static AtkUnitBase* ResolveOwningAddon(AtkResNode* node)
    {
        var unitManager = RaptureAtkUnitManager.Instance();
        return unitManager == null ? null : unitManager->GetAddonByNode(node);
    }

    private static bool VerifySecondNodeAttachment(
        AtkUldManager* manager,
        AtkResNode* parent,
        AtkTextNode* pluginNode,
        SecondNodeState state)
    {
        var pluginResNode = &pluginNode->AtkResNode;
        var expectedPrev = (AtkResNode*)state.OriginalTail;
        var expectedNext = (AtkResNode*)state.OriginalTailNextSibling;
        if (pluginResNode->ParentNode != parent ||
            pluginResNode->PrevSiblingNode != expectedPrev ||
            pluginResNode->NextSiblingNode != expectedNext ||
            parent->ChildNode != pluginResNode ||
            parent->ChildCount != (ushort)(state.OriginalParentChildCount + 1))
        {
            return false;
        }

        var membership = ScanSecondNodeMembership(
            manager,
            state.PluginNodePointer,
            state.NodeId);
        if (membership.NodeListPointerCount != 1 ||
            membership.NodeListIdCount != 1 ||
            membership.ObjectListPointerCount != 1 ||
            membership.ObjectListIdCount != 1)
        {
            return false;
        }

        var chain = ScanSecondNodeChildChain(
            parent,
            state.PluginNodePointer,
            state.NativeNodePointer);
        return chain.PluginCount == 1 &&
               chain.NativeCount == 1 &&
               chain.IsComplete &&
               !chain.IsRepeated;
    }

    private static void CopySecondNodeAppearance(
        AtkTextNode* nativeNode,
        AtkTextNode* pluginNode)
    {
        pluginNode->AtkResNode.Type = NodeType.Text;
        pluginNode->AtkResNode.X = nativeNode->AtkResNode.X;
        pluginNode->AtkResNode.Y = nativeNode->AtkResNode.Y;
        pluginNode->AtkResNode.Width = nativeNode->AtkResNode.Width;
        pluginNode->AtkResNode.Height = nativeNode->AtkResNode.Height;
        pluginNode->AtkResNode.ScaleX = nativeNode->AtkResNode.ScaleX;
        pluginNode->AtkResNode.ScaleY = nativeNode->AtkResNode.ScaleY;
        pluginNode->AtkResNode.Rotation = nativeNode->AtkResNode.Rotation;
        pluginNode->AtkResNode.OriginX = nativeNode->AtkResNode.OriginX;
        pluginNode->AtkResNode.OriginY = nativeNode->AtkResNode.OriginY;
        pluginNode->AtkResNode.Color = nativeNode->AtkResNode.Color;
        pluginNode->AtkResNode.AddRed = nativeNode->AtkResNode.AddRed;
        pluginNode->AtkResNode.AddGreen = nativeNode->AtkResNode.AddGreen;
        pluginNode->AtkResNode.AddBlue = nativeNode->AtkResNode.AddBlue;
        pluginNode->AtkResNode.MultiplyRed = nativeNode->AtkResNode.MultiplyRed;
        pluginNode->AtkResNode.MultiplyGreen = nativeNode->AtkResNode.MultiplyGreen;
        pluginNode->AtkResNode.MultiplyBlue = nativeNode->AtkResNode.MultiplyBlue;
        pluginNode->AtkResNode.Priority = nativeNode->AtkResNode.Priority;
        pluginNode->AtkResNode.DrawFlags = nativeNode->AtkResNode.DrawFlags;
        var currentVisible =
            pluginNode->AtkResNode.NodeFlags & NodeFlags.Visible;
        pluginNode->AtkResNode.NodeFlags =
            (nativeNode->AtkResNode.NodeFlags & ~NodeFlags.Visible) |
            currentVisible;

        pluginNode->TextId = 0;
        pluginNode->TextColor = nativeNode->TextColor;
        pluginNode->EdgeColor = nativeNode->EdgeColor;
        pluginNode->BackgroundColor = nativeNode->BackgroundColor;
        pluginNode->LineSpacing = nativeNode->LineSpacing;
        pluginNode->CharSpacing = nativeNode->CharSpacing;
        pluginNode->NumberFormat = nativeNode->NumberFormat;
        pluginNode->SheetType = nativeNode->SheetType;
        pluginNode->TextFlags =
            nativeNode->TextFlags &
            ~(TextFlags.MultiLine | TextFlags.WordWrap | TextFlags.Ellipsis);
        pluginNode->FontType = nativeNode->FontType;
        pluginNode->AlignmentType = nativeNode->AlignmentType;
        pluginNode->FontSize = nativeNode->FontSize;
    }

    private static void AttachSecondNode(
        AtkResNode* parent,
        AtkTextNode* pluginNode,
        SecondNodeState state)
    {
        if (parent->ChildCount == ushort.MaxValue)
            throw new InvalidOperationException("Parent child count is full.");

        var tail = parent->ChildNode;
        state.OriginalParentChildNode = (nint)parent->ChildNode;
        state.OriginalTail = (nint)tail;
        state.OriginalTailNextSibling =
            tail == null ? 0 : (nint)tail->NextSiblingNode;
        state.OriginalParentChildCount = parent->ChildCount;

        var nativeNode = (AtkResNode*)state.NativeNodePointer;
        state.OriginalNativePrevSibling =
            nativeNode->PrevSiblingNode == null
                ? 0
                : (nint)nativeNode->PrevSiblingNode;
        state.OriginalNativeNextSibling =
            nativeNode->NextSiblingNode == null
                ? 0
                : (nint)nativeNode->NextSiblingNode;

        var next = tail == null ? null : tail->NextSiblingNode;
        pluginNode->AtkResNode.ParentNode = parent;
        pluginNode->AtkResNode.PrevSiblingNode = tail;
        pluginNode->AtkResNode.NextSiblingNode = next;
        if (tail != null)
            tail->NextSiblingNode = &pluginNode->AtkResNode;
        if (next != null)
            next->PrevSiblingNode = &pluginNode->AtkResNode;

        parent->ChildNode = &pluginNode->AtkResNode;
        parent->ChildCount++;
        state.Attached = true;

        if (nativeNode->PrevSiblingNode !=
                (AtkResNode*)state.OriginalNativePrevSibling ||
            nativeNode->NextSiblingNode !=
                (AtkResNode*)state.OriginalNativeNextSibling)
        {
            throw new InvalidOperationException("Native sibling links changed.");
        }
    }

    private static bool SetSecondNodeVisibility(
        SecondNodeState state,
        bool visible)
    {
        var pluginNode = (AtkTextNode*)state.PluginNodePointer;
        var changed = pluginNode->AtkResNode.IsVisible() != visible;
        if (changed)
            pluginNode->AtkResNode.ToggleVisibility(visible);
        state.LastVisible = visible;
        return changed;
    }

    private static bool TryGetSecondNodeId(AtkUldManager* manager, out uint nodeId)
    {
        for (var candidate = SecondNodeIdBase; candidate < uint.MaxValue; candidate++)
        {
            if (!ContainsNodeId(manager, candidate))
            {
                nodeId = candidate;
                return true;
            }
        }

        nodeId = 0;
        return false;
    }

    private static bool ContainsNodeId(AtkUldManager* manager, uint nodeId)
    {
        if (manager == null)
            return false;

        if (manager->NodeList != null)
        {
            for (var index = 0; index < manager->NodeListCount; index++)
            {
                var node = manager->NodeList[index];
                if (node != null && node->NodeId == nodeId)
                    return true;
            }
        }

        var objects = manager->Objects;
        if (objects == null || objects->NodeList == null)
            return false;

        for (var index = 0; index < objects->NodeCount; index++)
        {
            var node = objects->NodeList[index];
            if (node != null && node->NodeId == nodeId)
                return true;
        }

        return false;
    }

    private static bool EnsureSecondNodeListCapacity(AtkUldManager* manager)
    {
        if (manager->NodeListCount < manager->NodeListSize)
            return true;
        if (manager->NodeListCount == ushort.MaxValue)
            return false;

        manager->ExpandNodeListSize((ushort)(manager->NodeListCount + 1));
        return manager->NodeList != null &&
               manager->NodeListCount < manager->NodeListSize;
    }

    private static bool AddSecondNodeToObjectList(
        AtkUldManager* manager,
        AtkResNode* node)
    {
        var objects = manager->Objects;
        var uiSpace = IMemorySpace.GetUISpace();
        if (objects == null ||
            uiSpace == null ||
            node == null ||
            (objects->NodeCount > 0 && objects->NodeList == null))
        {
            return false;
        }

        for (var index = 0; index < objects->NodeCount; index++)
        {
            if (objects->NodeList != null && objects->NodeList[index] == node)
                return true;
        }

        var oldCount = objects->NodeCount;
        var newBuffer = (AtkResNode**)uiSpace->Malloc(
            (ulong)(sizeof(nint) * (oldCount + 1)),
            8);
        if (newBuffer == null)
            return false;

        for (var index = 0; index < oldCount; index++)
            newBuffer[index] = objects->NodeList[index];
        newBuffer[oldCount] = node;

        if (objects->NodeList != null)
            IMemorySpace.Free(objects->NodeList, 0);
        objects->NodeList = newBuffer;
        objects->NodeCount = oldCount + 1;
        return true;
    }

    private static bool RemoveSecondNodeFromObjectList(
        AtkUldManager* manager,
        AtkResNode* node)
    {
        var objects = manager->Objects;
        if (objects == null || objects->NodeList == null || objects->NodeCount <= 0)
            return false;

        var removeIndex = -1;
        for (var index = 0; index < objects->NodeCount; index++)
        {
            if (objects->NodeList[index] == node)
            {
                removeIndex = index;
                break;
            }
        }

        if (removeIndex < 0)
            return false;

        for (var index = removeIndex; index < objects->NodeCount - 1; index++)
            objects->NodeList[index] = objects->NodeList[index + 1];
        objects->NodeList[objects->NodeCount - 1] = null;
        objects->NodeCount--;
        return true;
    }

    private void CleanupSecondNodes(SecondNodeCleanupReason reason)
    {
        var addonAddresses = new List<nint>(_secondNodes.Keys);
        foreach (var addonAddress in addonAddresses)
            CleanupSecondNode(addonAddress, reason);
    }

    private void CleanupSecondNode(
        nint addonAddress,
        SecondNodeCleanupReason reason,
        nint callbackAddon = 0)
    {
        if (!_secondNodes.Remove(addonAddress, out var state))
            return;

        CleanupSecondNodeState(state, reason, callbackAddon);
    }

    private bool CleanupSecondNodeState(
        SecondNodeState state,
        SecondNodeCleanupReason reason,
        nint callbackAddon)
    {
        if (state.Lifetime != SecondNodeLifetime.Alive)
            return false;

        state.Lifetime = SecondNodeLifetime.Cleaning;
        if (!state.Attached)
        {
            DestroyUnattachedSecondNode(state);
            return true;
        }

        var freshAddon = ResolveFreshAddon(state.AddonName);
        var callbackMatchesStored =
            reason != SecondNodeCleanupReason.PreFinalize ||
            (callbackAddon != 0 && callbackAddon == state.AddonPointer);
        var addonVerified =
            freshAddon != null &&
            (nint)freshAddon == state.AddonPointer &&
            callbackMatchesStored &&
            state.OwnershipVerified;
        if (!addonVerified)
        {
            MarkSecondNodeLeaked(
                state,
                "Fresh addon or ownership verification failed.");
            return false;
        }

        var manager = &freshAddon->UldManager;
        var validation = ValidateSecondNodeDetach(manager, state);
        if (!validation.CanDetach)
        {
            MarkSecondNodeLeaked(state, "Detach validation failed.");
            return false;
        }

        DetachSecondNode(validation, state);
        var objectListRemoved =
            RemoveSecondNodeFromObjectList(manager, validation.PluginResNode);
        if (objectListRemoved)
            state.AddedToObjectList = false;
        if (objectListRemoved)
            manager->UpdateDrawNodeList();

        var membership = ScanSecondNodeMembership(
            manager,
            state.PluginNodePointer,
            state.NodeId);
        var childChain = ScanSecondNodeChildChain(
            validation.Parent,
            state.PluginNodePointer,
            state.NativeNodePointer);
        var detached =
            membership.NodeListPointerCount == 0 &&
            membership.NodeListIdCount == 0 &&
            membership.ObjectListPointerCount == 0 &&
            membership.ObjectListIdCount == 0 &&
            childChain.PluginCount == 0 &&
            childChain.IsComplete &&
            !childChain.IsRepeated &&
            validation.Parent->ChildCount == state.OriginalParentChildCount &&
            NativeSiblingChainRestored(state) &&
            validation.PluginResNode->ParentNode == null &&
            validation.PluginResNode->PrevSiblingNode == null &&
            validation.PluginResNode->NextSiblingNode == null;
        if (!detached)
        {
            MarkSecondNodeLeaked(state, "Post-detach verification failed.");
            return false;
        }

        var pluginNode = (AtkTextNode*)state.PluginNodePointer;
        var retainedPointer = (nint)state.TextBuffer;
        var originalTextPointer = (nint)pluginNode->OriginalTextPointer.Value;
        var textPointerSafe =
            !state.HasSetText
                ? retainedPointer == 0 && originalTextPointer == 0
                : retainedPointer != 0 && retainedPointer == originalTextPointer;
        if (!textPointerSafe)
        {
            MarkSecondNodeLeaked(
                state,
                $"Current text pointer mismatch before Destroy: " +
                $"Retained={FormatAddress(retainedPointer)}, " +
                $"Original={FormatAddress(originalTextPointer)}");
            return false;
        }

        state.Lifetime = SecondNodeLifetime.Destroying;
        pluginNode->Destroy(true);
        FreeRetainedText(state);
        state.Lifetime = SecondNodeLifetime.Destroyed;
        ClearSecondNodePointers(state);
        return true;
    }

    private AtkUnitBase* ResolveFreshAddon(string addonName)
    {
        try
        {
            return _gameGui.GetAddonByName<AtkUnitBase>(addonName, 1);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, $"Fresh addon lookup failed: Addon={addonName}.");
            return null;
        }
    }

    private static SecondNodeDetachValidation ValidateSecondNodeDetach(
        AtkUldManager* manager,
        SecondNodeState state)
    {
        var validation = new SecondNodeDetachValidation();
        if (manager == null || !state.Attached)
            return validation;

        var membership = ScanSecondNodeMembership(
            manager,
            state.PluginNodePointer,
            state.NodeId);
        validation.NodeListPointerCount = membership.NodeListPointerCount;
        validation.NodeListIdCount = membership.NodeListIdCount;
        validation.ObjectListPointerCount = membership.ObjectListPointerCount;
        validation.ObjectListIdCount = membership.ObjectListIdCount;
        if (validation.NodeListPointerCount != 1 ||
            validation.ObjectListPointerCount != 1)
        {
            return validation;
        }

        var pluginNode = (AtkTextNode*)state.PluginNodePointer;
        validation.PluginResNode = &pluginNode->AtkResNode;
        validation.PluginNodeIdMatches =
            validation.PluginResNode->NodeId == state.NodeId;
        validation.Parent = validation.PluginResNode->ParentNode;
        if (validation.Parent == null)
            return validation;

        validation.ParentMatches = (nint)validation.Parent == state.ParentPointer;
        validation.PrevMatches =
            validation.PluginResNode->PrevSiblingNode ==
            (AtkResNode*)state.OriginalTail;
        validation.NextMatches =
            validation.PluginResNode->NextSiblingNode ==
            (AtkResNode*)state.OriginalTailNextSibling;
        validation.ParentChildMatches =
            validation.Parent->ChildNode == validation.PluginResNode;
        validation.ChildCountMatches =
            validation.Parent->ChildCount ==
            (ushort)(state.OriginalParentChildCount + 1);
        validation.PreviousLinkMatches =
            validation.PluginResNode->PrevSiblingNode == null ||
            validation.PluginResNode->PrevSiblingNode->NextSiblingNode ==
            validation.PluginResNode;
        validation.NextLinkMatches =
            validation.PluginResNode->NextSiblingNode == null ||
            validation.PluginResNode->NextSiblingNode->PrevSiblingNode ==
            validation.PluginResNode;

        var chain = ScanSecondNodeChildChain(
            validation.Parent,
            state.PluginNodePointer,
            state.NativeNodePointer);
        validation.PluginInChildChain = chain.PluginCount == 1;
        validation.NativeNodeInChildChain = chain.NativeCount == 1;
        validation.ChildChainComplete = chain.IsComplete;
        validation.ChildChainRepeated = chain.IsRepeated;

        var nativeNode = (AtkResNode*)state.NativeNodePointer;
        validation.NativeSiblingLinksMatch =
            nativeNode != null &&
            nativeNode->PrevSiblingNode ==
            (AtkResNode*)state.OriginalNativePrevSibling &&
            nativeNode->NextSiblingNode ==
            (AtkResNode*)state.OriginalNativeNextSibling;
        return validation;
    }

    private static SecondNodeMembership ScanSecondNodeMembership(
        AtkUldManager* manager,
        nint pluginPointer,
        uint nodeId)
    {
        var membership = new SecondNodeMembership();
        if (manager == null)
            return membership;

        if (manager->NodeList != null)
        {
            for (var index = 0; index < manager->NodeListCount; index++)
            {
                var node = manager->NodeList[index];
                if ((nint)node == pluginPointer)
                    membership.NodeListPointerCount++;
                if (node != null && node->NodeId == nodeId)
                    membership.NodeListIdCount++;
            }
        }

        var objects = manager->Objects;
        if (objects == null || objects->NodeList == null)
            return membership;

        for (var index = 0; index < objects->NodeCount; index++)
        {
            var node = objects->NodeList[index];
            if ((nint)node == pluginPointer)
                membership.ObjectListPointerCount++;
            if (node != null && node->NodeId == nodeId)
                membership.ObjectListIdCount++;
        }

        return membership;
    }

    private static SecondNodeChildChain ScanSecondNodeChildChain(
        AtkResNode* parent,
        nint pluginPointer,
        nint nativePointer)
    {
        var result = new SecondNodeChildChain();
        if (parent == null)
            return result;

        var seen = new HashSet<nint>();
        var child = parent->ChildNode;
        for (var index = 0; child != null && index < 64; index++)
        {
            if (!seen.Add((nint)child))
            {
                result.IsRepeated = true;
                break;
            }

            if ((nint)child == pluginPointer)
                result.PluginCount++;
            if ((nint)child == nativePointer)
                result.NativeCount++;
            child = child->PrevSiblingNode;
        }

        result.IsComplete = child == null;
        return result;
    }

    private static void DetachSecondNode(
        SecondNodeDetachValidation validation,
        SecondNodeState state)
    {
        var plugin = validation.PluginResNode;
        var parent = validation.Parent;
        var previous = plugin->PrevSiblingNode;
        var next = plugin->NextSiblingNode;

        if (previous != null)
            previous->NextSiblingNode = next;
        if (next != null)
            next->PrevSiblingNode = previous;

        parent->ChildNode = (AtkResNode*)state.OriginalParentChildNode;
        parent->ChildCount--;
        plugin->ParentNode = null;
        plugin->PrevSiblingNode = null;
        plugin->NextSiblingNode = null;
        state.Attached = false;
    }

    private static bool NativeSiblingChainRestored(SecondNodeState state)
    {
        var nativeNode = (AtkResNode*)state.NativeNodePointer;
        return nativeNode != null &&
               nativeNode->PrevSiblingNode ==
               (AtkResNode*)state.OriginalNativePrevSibling &&
               nativeNode->NextSiblingNode ==
               (AtkResNode*)state.OriginalNativeNextSibling;
    }

    private static bool ValidateSecondNodeInitialState(AtkTextNode* node)
    {
        var resNode = &node->AtkResNode;
        return (nint)node->VirtualTable != 0 &&
               (nint)node->OriginalTextPointer.Value == 0 &&
               (nint)node->LinkData == 0 &&
               node->FontCacheHandle == 0 &&
               (nint)resNode->Timeline == 0;
    }

    private static void DestroyUnattachedSecondNode(SecondNodeState state)
    {
        if ((state.Lifetime != SecondNodeLifetime.Alive &&
             state.Lifetime != SecondNodeLifetime.Cleaning) ||
            state.PluginNodePointer == 0)
        {
            return;
        }

        state.Lifetime = SecondNodeLifetime.Destroying;
        var pluginNode = (AtkTextNode*)state.PluginNodePointer;
        pluginNode->Destroy(true);
        FreeRetainedText(state);
        state.Lifetime = SecondNodeLifetime.Destroyed;
        ClearSecondNodePointers(state);
    }

    private void MarkSecondNodeLeaked(SecondNodeState state, string reason)
    {
        state.Lifetime = SecondNodeLifetime.Leaked;
        _log.Warning(
            $"Second node cleanup skipped (intentional safety leak): " +
            $"Addon={state.AddonName}, " +
            $"Plugin={FormatAddress(state.PluginNodePointer)}, Reason={reason}");
    }

    private static void ClearSecondNodePointers(SecondNodeState state)
    {
        state.AddonPointer = 0;
        state.ResolvedAddonPointer = 0;
        state.UldManagerPointer = 0;
        state.NativeNodePointer = 0;
        state.ParentPointer = 0;
        state.PluginNodePointer = 0;
        state.TextBuffer = IntPtr.Zero;
        state.DeferredTextBuffers.Clear();
        state.HasSetText = false;
        state.Text = null;
        state.AddedToObjectList = false;
        state.Attached = false;
        state.OwnershipVerified = false;
        state.OriginalParentChildNode = 0;
        state.OriginalTail = 0;
        state.OriginalTailNextSibling = 0;
        state.OriginalNativePrevSibling = 0;
        state.OriginalNativeNextSibling = 0;
    }

    private static void FreeRetainedText(SecondNodeState state)
    {
        var currentBuffer = state.TextBuffer;
        if (currentBuffer != IntPtr.Zero)
            Marshal.FreeHGlobal(currentBuffer);

        foreach (var textBuffer in state.DeferredTextBuffers)
        {
            if (textBuffer != IntPtr.Zero && textBuffer != currentBuffer)
                Marshal.FreeHGlobal(textBuffer);
        }

        state.DeferredTextBuffers.Clear();
        state.TextBuffer = IntPtr.Zero;
    }

    private static void RetainDeferredTextBuffer(
        SecondNodeState state,
        IntPtr textBuffer)
    {
        if (textBuffer != IntPtr.Zero &&
            !state.DeferredTextBuffers.Contains(textBuffer))
        {
            state.DeferredTextBuffers.Add(textBuffer);
        }
    }

    private bool DetectOrphanedSecondNode(
        AtkUnitBase* addon,
        AtkResNode* parent,
        string addonName)
    {
        var manager = &addon->UldManager;
        var tracked = new HashSet<nint>();
        foreach (var state in _secondNodes.Values)
        {
            if (state.AddonPointer == (nint)addon &&
                state.PluginNodePointer != 0)
            {
                tracked.Add(state.PluginNodePointer);
            }
        }

        if (manager->NodeList != null)
        {
            for (var index = 0; index < manager->NodeListCount; index++)
            {
                var node = manager->NodeList[index];
                if (node != null &&
                    node->NodeId >= SecondNodeIdBase &&
                    !tracked.Contains((nint)node))
                {
                    _log.Warning(
                        $"Second node orphan detected: Addon={addonName}, " +
                        $"NodeId={node->NodeId}");
                    return true;
                }
            }
        }

        var child = parent->ChildNode;
        for (var index = 0; child != null && index < 64; index++)
        {
            if (child->NodeId >= SecondNodeIdBase &&
                !tracked.Contains((nint)child))
            {
                _log.Warning(
                    $"Second node orphan detected: Addon={addonName}, " +
                    $"NodeId={child->NodeId}");
                return true;
            }

            child = child->PrevSiblingNode;
        }

        return false;
    }

    private bool SetSecondNodeText(
        SecondNodeState state,
        string text)
    {
        if (string.Equals(state.Text, text, StringComparison.Ordinal))
            return false;

        var pluginNode = (AtkTextNode*)state.PluginNodePointer;
        var previousBuffer = state.TextBuffer;
        var bytes = Encoding.UTF8.GetBytes(text + "\0");
        var newBuffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, newBuffer, bytes.Length);
            pluginNode->SetText((byte*)newBuffer);
        }
        catch
        {
            Marshal.FreeHGlobal(newBuffer);
            throw;
        }

        var originalTextPointerAfter = (nint)pluginNode->OriginalTextPointer.Value;
        var invariantHolds = originalTextPointerAfter == (nint)newBuffer;
        if (!invariantHolds)
        {
            RetainDeferredTextBuffer(state, previousBuffer);
            RetainDeferredTextBuffer(state, newBuffer);
            state.TextBuffer =
                originalTextPointerAfter == (nint)previousBuffer
                    ? previousBuffer
                    : IntPtr.Zero;
            state.HasSetText = true;
            state.Text = text;
            _log.Warning(
                $"Second node SetText invariant failed: " +
                $"Addon={state.AddonName}, " +
                $"NewInput={FormatAddress((nint)newBuffer)}, " +
                $"OriginalAfter={FormatAddress(originalTextPointerAfter)}");
            return true;
        }

        if (previousBuffer != IntPtr.Zero &&
            !state.DeferredTextBuffers.Contains(previousBuffer))
        {
            Marshal.FreeHGlobal(previousBuffer);
        }

        state.TextBuffer = newBuffer;
        state.HasSetText = true;
        state.Text = text;
        return true;
    }

    private static ushort MeasureTextWidth(
        AtkTextNode* node,
        string text,
        byte fontSize)
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
            node->GetTextDrawSize(&width, &height, textPointer);
        return width;
    }

    private static AtkTextNode* GetTextNodeById(AtkUnitBase* addon, uint nodeId)
    {
        if (addon->UldManager.NodeListCount <= nodeId)
            return null;

        var node = addon->GetNodeById(nodeId);
        if (node == null || node->Type != NodeType.Text)
            return null;

        return (AtkTextNode*)node;
    }

    private static string FormatAddress(nint address)
    {
        return $"0x{(long)address:X}";
    }

    private sealed class SecondNodeDetachValidation
    {
        public AtkResNode* PluginResNode;
        public AtkResNode* Parent;
        public int NodeListPointerCount;
        public int ObjectListPointerCount;
        public int NodeListIdCount;
        public int ObjectListIdCount;
        public bool PluginNodeIdMatches;
        public bool ParentMatches;
        public bool PrevMatches;
        public bool NextMatches;
        public bool ParentChildMatches;
        public bool ChildCountMatches;
        public bool PreviousLinkMatches;
        public bool NextLinkMatches;
        public bool PluginInChildChain;
        public bool NativeNodeInChildChain;
        public bool ChildChainComplete;
        public bool ChildChainRepeated;
        public bool NativeSiblingLinksMatch;

        public bool CanDetach =>
            NodeListPointerCount == 1 &&
            ObjectListPointerCount == 1 &&
            NodeListIdCount == 1 &&
            ObjectListIdCount == 1 &&
            PluginNodeIdMatches &&
            Parent != null &&
            ParentMatches &&
            PrevMatches &&
            NextMatches &&
            ParentChildMatches &&
            ChildCountMatches &&
            PreviousLinkMatches &&
            NextLinkMatches &&
            PluginInChildChain &&
            NativeNodeInChildChain &&
            ChildChainComplete &&
            !ChildChainRepeated &&
            NativeSiblingLinksMatch;
    }

    private sealed class SecondNodeMembership
    {
        public int NodeListPointerCount;
        public int ObjectListPointerCount;
        public int NodeListIdCount;
        public int ObjectListIdCount;
    }

    private sealed class SecondNodeChildChain
    {
        public int PluginCount;
        public int NativeCount;
        public bool IsComplete;
        public bool IsRepeated;
    }

    private sealed class SecondNodeState
    {
        public string AddonName = string.Empty;
        public nint AddonPointer;
        public nint ResolvedAddonPointer;
        public nint UldManagerPointer;
        public nint NativeNodePointer;
        public nint ParentPointer;
        public nint PluginNodePointer;
        public uint NodeId;
        public SecondNodeLifetime Lifetime = SecondNodeLifetime.Alive;
        public IntPtr TextBuffer;
        public readonly List<IntPtr> DeferredTextBuffers = new();
        public bool HasSetText;
        public string? Text;
        public bool AddedToObjectList;
        public bool Attached;
        public bool OwnershipVerified;
        public nint OriginalParentChildNode;
        public nint OriginalTail;
        public nint OriginalTailNextSibling;
        public ushort OriginalParentChildCount;
        public nint OriginalNativePrevSibling;
        public nint OriginalNativeNextSibling;
        public byte? LastAppliedFontSize;
        public bool LastVisible;
    }
}
