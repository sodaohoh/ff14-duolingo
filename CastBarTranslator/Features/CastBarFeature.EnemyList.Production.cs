using System;
using System.Collections.Generic;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CastBarTranslator.Features;

public sealed unsafe partial class CastBarFeature
{
    private const int EnemyListOrphanFailureKey = -2;
    private readonly Dictionary<int, SecondNodeState> _enemyListCastNodes = new();
    private readonly Dictionary<int, string> _enemyListCastFailures = new();
    private bool _enemyListCastFeatureEnabled;
    private bool _enemyListCastOffFrameworkFailureLogged;
    private string? _enemyListCastLastUpdateFailure;
    private nint _enemyListCastObservedAddon;
    private nint _enemyListCastObservedRoot;

    private void OnEnemyListCastUpdate(IFramework framework)
    {
        if (!framework.IsInFrameworkUpdateThread)
        {
            if (!_enemyListCastOffFrameworkFailureLogged &&
                (_enemyListCastFeatureEnabled || _enemyListCastNodes.Count != 0))
            {
                _log.Warning(
                    "ENEMYLIST_RECOVERABLE_FAILURE reason=framework-thread-unavailable " +
                    "retry=next-update");
            }

            _enemyListCastOffFrameworkFailureLogged = true;
            return;
        }

        _enemyListCastOffFrameworkFailureLogged = false;
        if (!_configuration.TranslateEnemyListCasts)
        {
            _enemyListCastFeatureEnabled = false;
            _enemyListCastLastUpdateFailure = null;
            if (_enemyListCastNodes.Count != 0)
                CleanupEnemyListCastNodes(SecondNodeCleanupReason.Dispose);
            _enemyListCastFailures.Clear();
            return;
        }

        _enemyListCastFeatureEnabled = true;

        try
        {
            UpdateEnemyListCastFrame();
            _enemyListCastLastUpdateFailure = null;
        }
        catch (Exception ex)
        {
            var failure = ex.GetType().Name;
            if (_enemyListCastLastUpdateFailure != failure)
            {
                _log.Error(
                    ex,
                    $"ENEMYLIST_RECOVERABLE_FAILURE scope=frame exception={failure} " +
                    "retry=next-update");
            }

            _enemyListCastLastUpdateFailure = failure;
            if (_enemyListCastNodes.Count != 0)
                CleanupEnemyListCastNodes(SecondNodeCleanupReason.Dispose);
        }
    }
    private void ForgetEnemyListCastAddon()
    {
        if (_enemyListCastObservedAddon == 0)
            return;

        _enemyListCastObservedAddon = 0;
        _enemyListCastFailures.Remove(EnemyListOrphanFailureKey);
        _enemyListCastObservedRoot = 0;
    }

    private void UpdateEnemyListCastFrame()
    {

        var addon = _gameGui.GetAddonByName<AtkUnitBase>(AddonEnemyList, 1);
        if (addon == null)
        {
            ForgetEnemyListCastAddon();
            return;
        }

        var root = addon->RootNode;
        var manager = &addon->UldManager;
        if (root == null || !IsValidEnemyListUldManager(manager))
        {
            RecordEnemyListCastFailure(-1, "addon-root-or-uld-invalid");
            return;
        }

        var addonPointer = (nint)addon;
        var rootPointer = (nint)root;
        if (_enemyListCastObservedAddon != addonPointer ||
            _enemyListCastObservedRoot != rootPointer)
        {
            _enemyListCastFailures.Remove(EnemyListOrphanFailureKey);
        }


        _enemyListCastObservedAddon = addonPointer;
        _enemyListCastObservedRoot = rootPointer;
        if (HasEnemyListCastOwnerChanged(addon, root, manager))
            CleanupEnemyListCastNodes(SecondNodeCleanupReason.AddonReplacement);

        if (!addon->IsVisible)
        {
            HideEnemyListCastNodes(addon, root, manager);
            return;
        }

        var numbers = EnemyListNumberArray.Instance();
        if (numbers == null)
        {
            RecordEnemyListCastFailure(-1, "number-array-unavailable");
            HideEnemyListCastNodes(addon, root, manager);
            return;
        }
        var hasActiveEnemy = false;
        for (var slot = 0; slot < MaxEnemyListOverlaySlots; slot++)
        {
            var row = numbers->Enemies[slot];
            if (row.ActiveInList && row.EntityId != 0)
            {
                hasActiveEnemy = true;
                break;
            }
        }
        if (!hasActiveEnemy)
        {
            HideEnemyListCastNodes(addon, root, manager);
            _enemyListCastFailures.Clear();
            return;
        }

        if (!TryMeasureEnemyRowPitchScreen(
                addon,
                root,
                manager,
                out var rowPitchScreen,
                out var rowPitchSampleCount,
                out var failure))
        {
            RecordEnemyListCastFailure(-1, failure);
            HideEnemyListCastNodes(addon, root, manager);
            return;
        }

        _enemyListCastFailures.Remove(-1);
        for (var slot = 0; slot < MaxEnemyListOverlaySlots; slot++)
        {
            var row = numbers->Enemies[slot];
            var entityId = row.ActiveInList ? unchecked((uint)row.EntityId) : 0;
            try
            {
                UpdateEnemyListCastSlot(
                    addon,
                    root,
                    manager,
                    slot,
                    row.ActiveInList,
                    entityId,
                    rowPitchScreen,
                    rowPitchSampleCount);
            }
            catch (Exception ex)
            {
                HandleEnemyListCastSlotException(slot, addon, root, manager, ex);
            }
        }
    }

    private bool HasEnemyListCastOwnerChanged(
        AtkUnitBase* addon,
        AtkResNode* root,
        AtkUldManager* manager)
    {
        foreach (var state in _enemyListCastNodes.Values)
        {
            if (state.AddonPointer != (nint)addon ||
                state.ParentPointer != (nint)root ||
                state.UldManagerPointer != (nint)manager)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryFindUntrackedEnemyListNode(
        AtkUnitBase* addon,
        AtkResNode* root,
        AtkUldManager* manager,
        out uint orphanNodeId)
    {
        orphanNodeId = 0;
        for (var index = 0; index < manager->NodeListCount; index++)
        {
            var node = manager->NodeList[index];
            if (node == null ||
                node->NodeId < SecondNodeIdBase ||
                IsTrackedEnemyListCastNode(addon, root, manager, node))
            {
                continue;
            }

            orphanNodeId = node->NodeId;
            return true;
        }

        var child = root->ChildNode;
        for (var index = 0; child != null && index < MaxSecondNodeChildChain; index++)
        {
            if (child->NodeId >= SecondNodeIdBase &&
                !IsTrackedEnemyListCastNode(addon, root, manager, child))
            {
                orphanNodeId = child->NodeId;
                return true;
            }

            child = child->PrevSiblingNode;
        }

        return false;
    }

    private bool IsTrackedEnemyListCastNode(
        AtkUnitBase* addon,
        AtkResNode* root,
        AtkUldManager* manager,
        AtkResNode* candidate)
    {
        foreach (var state in _enemyListCastNodes.Values)
        {
            if (state.AddonName != AddonEnemyList ||
                state.AddonPointer != (nint)addon ||
                state.ResolvedAddonPointer != (nint)addon ||
                state.ParentPointer != (nint)root ||
                state.UldManagerPointer != (nint)manager ||
                !state.ParentIsAddonRoot ||
                state.PluginNodePointer != (nint)candidate ||
                state.NodeId != candidate->NodeId)
            {
                continue;
            }

            return ValidateLiveEnemyListOverlay(addon, root, manager, state);
        }

        return false;
    }

    private void UpdateEnemyListCastSlot(
        AtkUnitBase* addon,
        AtkResNode* root,
        AtkUldManager* manager,
        int slot,
        bool rowActive,
        uint entityId,
        float rowPitchScreen,
        int rowPitchSampleCount)
    {
        _enemyListCastNodes.TryGetValue(slot, out var state);
        if (state != null && !ValidateLiveEnemyListOverlay(addon, root, manager, state))
        {
            _enemyListCastNodes.Remove(slot);
            MarkSecondNodeLeaked(state, "Live Enemy List cast node ownership validation failed.");
            RecordEnemyListCastFailure(slot, "plugin-node-ownership-invalid");
            return;
        }

        if (!rowActive || entityId == 0)
        {
            DeactivateEnemyListCastSlot(state, manager, slot);
            _enemyListCastFailures.Remove(slot);
            return;
        }

        var actor = _objectTable.SearchByEntityId(entityId);
        if (actor is not IBattleChara battleChara || !battleChara.IsCasting)
        {
            DeactivateEnemyListCastSlot(state, manager, slot);
            _enemyListCastFailures.Remove(slot);
            return;
        }

        var actionId = battleChara.CastActionId;
        if (actionId == 0)
        {
            DeactivateEnemyListCastSlot(state, manager, slot);
            _enemyListCastFailures.Remove(slot);
            return;
        }

        var translatedName = _translationService.GetActionName(
            actionId,
            _configuration.BottomLanguage);
        if (string.IsNullOrEmpty(translatedName))
        {
            HideEnemyListCastNode(state, manager, slot);
            _enemyListCastFailures.Remove(slot);
            return;
        }

        if (!TryGetEnemyListOverlayRowNodes(
                addon,
                root,
                manager,
                slot,
                out var rowOwner,
                out var castBarNode,
                out var styleSource,
                out var failure))
        {
            RecordEnemyListCastFailure(slot, failure);
            HideEnemyListCastNode(state, manager, slot);
            return;
        }
        if (!rowOwner->AtkResNode.IsVisible())
        {
            DeactivateEnemyListCastSlot(state, manager, slot);
            _enemyListCastFailures.Remove(slot);
            return;
        }

        if (!TryCalculateEnemyListOverlayX(
                root,
                styleSource,
                out var nativeRightScreen,
                out _,
                out var targetX,
                out failure))
        {
            RecordEnemyListCastFailure(slot, failure);
            HideEnemyListCastNode(state, manager, slot);
            return;
        }
        if (!TryGetDerivedEnemyListOverlayFontSize(
                styleSource->FontSize,
                out var derivedFontSize,
                out failure))
        {
            RecordEnemyListCastFailure(slot, failure);
            HideEnemyListCastNode(state, manager, slot);
            return;
        }

        var pluginWidth = styleSource->AtkResNode.Width;
        var nodeCreated = state == null;
        if (state == null)
        {
            if (!IsValidEnemyListUldManager(manager) ||
                !TryScanEnemyListRoot(root, 0, out var existingChildren) ||
                existingChildren >= MaxSecondNodeChildChain)
            {
                RecordEnemyListCastFailure(slot, "enemy-list-root-capacity-invalid");
                return;
            }
            if (TryFindUntrackedEnemyListNode(
                    addon,
                    root,
                    manager,
                    out var orphanNodeId))
            {
                if (!_enemyListCastFailures.ContainsKey(EnemyListOrphanFailureKey))
                {
                    _enemyListCastFailures.Add(
                        EnemyListOrphanFailureKey,
                        "untracked-plugin-node-present");
                    _log.Warning(
                        $"ENEMYLIST_ORPHAN_DETECTED slot={slot} nodeId={orphanNodeId}");
                }

                return;
            }

            _enemyListCastFailures.Remove(EnemyListOrphanFailureKey);

            state = CreateSecondNode(
                AddonEnemyList,
                addon,
                styleSource,
                root,
                parentIsAddonRoot: true);
            if (state == null)
            {
                RecordEnemyListCastFailure(slot, "plugin-node-creation-failed");
                return;
            }

            _enemyListCastNodes.Add(slot, state);
            _log.Debug($"ENEMYLIST_NODE_CREATED slot={slot} nodeId={state.NodeId}");
        }

        var pluginNode = (AtkTextNode*)state.PluginNodePointer;
        var styleChanged = ApplyEnemyListCastTextStyle(
            state,
            pluginNode,
            pluginWidth,
            derivedFontSize);
        var changed = nodeCreated || styleChanged;
        var textChanged = !string.Equals(
            state.Text,
            translatedName,
            StringComparison.Ordinal);
        if (textChanged)
        {
            SetSecondNodeText(state, translatedName);
            changed = true;
        }

        if (changed || state.EnemyListMeasuredTextHeight == 0)
        {
            ushort measuredWidth = 0;
            ushort measuredHeight = 0;
            pluginNode->GetTextDrawSize(&measuredWidth, &measuredHeight);
            if (measuredWidth == 0 || measuredHeight == 0)
            {
                state.EnemyListMeasuredTextHeight = 0;

                RecordEnemyListCastFailure(slot, "text-measurement-empty");
                HideEnemyListCastNode(state, manager, slot);
                return;
            }

            state.EnemyListMeasuredTextHeight = measuredHeight;
        }

        if (!TryCalculateEnemyListOverlayAutoLayout(
                root,
                castBarNode,
                styleSource,
                pluginNode,
                rowPitchScreen,
                rowPitchSampleCount,
                nativeRightScreen,
                targetX,
                pluginWidth,
                derivedFontSize,
                state.EnemyListMeasuredTextHeight,
                out var pluginRootLocalY,
                out failure))
        {

            RecordEnemyListCastFailure(slot, failure);
            HideEnemyListCastNode(state, manager, slot);
            return;
        }

        changed |= ApplyEnemyListUpperLayout(
            pluginNode,
            targetX,
            pluginRootLocalY,
            state.EnemyListMeasuredTextHeight);

        if (SetSecondNodeVisibility(state, visible: true))
            changed = true;
        _enemyListCastFailures.Remove(slot);

        if (changed)
        {
            pluginNode->AtkResNode.IsDirty = true;
            manager->UpdateDrawNodeList();
        }
    }

    private static bool ApplyEnemyListCastTextStyle(
        SecondNodeState state,
        AtkTextNode* pluginNode,
        ushort width,
        byte fontSize)
    {
        var changed = false;
        if (pluginNode->AtkResNode.Width != width)
        {
            pluginNode->AtkResNode.Width = width;
            changed = true;
        }
        if (pluginNode->FontSize != fontSize)
        {
            pluginNode->FontSize = fontSize;
            changed = true;
        }
        if (pluginNode->AlignmentType != AlignmentType.Right)
        {
            pluginNode->AlignmentType = AlignmentType.Right;
            changed = true;
        }

        var flags = GetEnemyListTextFlowFlags(pluginNode->TextFlags, useEllipsis: true);
        if (pluginNode->TextFlags != flags)
        {
            pluginNode->TextFlags = flags;
            changed = true;
        }
        if (!state.OverlayStyleApplied)
        {
            pluginNode->BackgroundColor = default;
            state.OverlayStyleApplied = true;
            changed = true;
        }

        return changed;
    }

    private void DeactivateEnemyListCastSlot(
        SecondNodeState? state,
        AtkUldManager* manager,
        int slot)
    {
        if (state == null)
            return;

        HideEnemyListCastNode(state, manager, slot);
    }

    private void HideEnemyListCastNodes(
        AtkUnitBase* addon,
        AtkResNode* root,
        AtkUldManager* manager)
    {
        for (var slot = 0; slot < MaxEnemyListOverlaySlots; slot++)
        {
            if (!_enemyListCastNodes.TryGetValue(slot, out var state))
                continue;
            if (!ValidateLiveEnemyListOverlay(addon, root, manager, state))
            {
                _enemyListCastNodes.Remove(slot);
                MarkSecondNodeLeaked(state, "Hidden Enemy List cast node ownership validation failed.");
                continue;
            }

            DeactivateEnemyListCastSlot(state, manager, slot);
        }
    }

    private void HideEnemyListCastNode(
        SecondNodeState? state,
        AtkUldManager* manager,
        int slot)
    {
        if (state == null || !SetSecondNodeVisibility(state, visible: false))
            return;

        _log.Debug($"ENEMYLIST_NODE_HIDDEN slot={slot} nodeId={state.NodeId}");
        var pluginNode = (AtkTextNode*)state.PluginNodePointer;
        pluginNode->AtkResNode.IsDirty = true;
        manager->UpdateDrawNodeList();
    }

    private void HandleEnemyListCastSlotException(
        int slot,
        AtkUnitBase* addon,
        AtkResNode* root,
        AtkUldManager* manager,
        Exception exception)
    {
        var reason = $"exception:{exception.GetType().Name}";
        if (!_enemyListCastFailures.TryGetValue(slot, out var previousReason) ||
            previousReason != reason)
        {
            _enemyListCastFailures[slot] = reason;
            _log.Error(
                exception,
                $"ENEMYLIST_RECOVERABLE_FAILURE slot={slot} exception={reason} " +
                "retry=next-update");
        }

        if (!_enemyListCastNodes.TryGetValue(slot, out var state))
            return;
        try
        {
            if (ValidateLiveEnemyListOverlay(addon, root, manager, state))
            {
                HideEnemyListCastNode(state, manager, slot);
                return;
            }
        }
        catch (Exception hideException)
        {
            _log.Error(hideException, $"Enemy List cast node cleanup failed for slot {slot}.");
        }

        _enemyListCastNodes.Remove(slot);
        MarkSecondNodeLeaked(state, "Exception prevented safe Enemy List cast node validation.");
    }

    private void RecordEnemyListCastFailure(int slot, string reason)
    {
        if (_enemyListCastFailures.TryGetValue(slot, out var previousReason) &&
            previousReason == reason)
        {
            return;
        }

        _enemyListCastFailures[slot] = reason;
        if (slot < 0)
            _log.Warning($"ENEMYLIST_RECOVERABLE_FAILURE reason={reason}.");
        else
            _log.Warning(
                $"ENEMYLIST_RECOVERABLE_FAILURE slot={slot} reason={reason}.");
    }

    private void CleanupEnemyListCastNodes(
        SecondNodeCleanupReason reason,
        nint callbackAddon = 0)
    {
        if (!_framework.IsInFrameworkUpdateThread)
        {
            LeakEnemyListCastNodes("Cast node cleanup requested off Framework thread.");
            return;
        }
        if (_enemyListCastNodes.Count == 0)
        {
            _enemyListCastFailures.Clear();
            return;
        }

        var slots = new List<int>(_enemyListCastNodes.Keys);
        foreach (var slot in slots)
        {
            if (!_enemyListCastNodes.TryGetValue(slot, out var state) ||
                (callbackAddon != 0 && state.AddonPointer != callbackAddon))
            {
                continue;
            }

            _enemyListCastNodes.Remove(slot);
            _enemyListCastFailures.Remove(slot);
            try
            {
                var cleaned = CleanupSecondNodeState(state, reason, callbackAddon);
                _log.Debug(
                    $"ENEMYLIST_NODE_CLEANUP slot={slot} nodeId={state.NodeId} " +
                    $"result={(cleaned ? "success" : "intentionally-leaked")} reason={reason}");
            }
            catch (Exception ex)
            {
                MarkSecondNodeLeaked(state, $"Cast node cleanup threw: {ex.Message}");
                _log.Debug(
                    $"ENEMYLIST_NODE_CLEANUP slot={slot} nodeId={state.NodeId} " +
                    $"result=intentionally-leaked reason={reason}");
            }
        }
    }

    private void LeakEnemyListCastNodes(string reason)
    {
        foreach (var entry in _enemyListCastNodes)
        {
            MarkSecondNodeLeaked(entry.Value, reason);
            _log.Debug(
                $"ENEMYLIST_NODE_CLEANUP slot={entry.Key} nodeId={entry.Value.NodeId} " +
                $"result=intentionally-leaked reason={reason}");
        }
        _enemyListCastNodes.Clear();
        _enemyListCastFailures.Clear();
    }

    private void OnEnemyListFinalize(AddonEvent type, AddonArgs args)
    {
        var addonAddress = (nint)args.Addon;
        if (type != AddonEvent.PreFinalize || addonAddress == 0)
            return;
        if (_enemyListCastObservedAddon == addonAddress)
            ForgetEnemyListCastAddon();

        if (!_framework.IsInFrameworkUpdateThread)
        {
            LeakEnemyListCastNodes("Addon finalized off Framework thread.");
            return;
        }

        CleanupEnemyListCastNodes(
            SecondNodeCleanupReason.PreFinalize,
            addonAddress);
    }
}
