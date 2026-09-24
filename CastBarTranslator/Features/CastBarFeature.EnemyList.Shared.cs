using System;
using Vector2 = System.Numerics.Vector2;
using FFXIVClientStructs.FFXIV.Client.UI;
using Matrix2x2 = FFXIVClientStructs.FFXIV.Common.Math.Matrix2x2;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CastBarTranslator.Features;

public sealed unsafe partial class CastBarFeature
{
    private const double TransformDeterminantRelativeTolerance = 1e-6;
    private const float EnemyListOverlayFontScale = 10f / 12f;
    private const float MinPitchToMedianRatio = 0.5f;
    private const float MaxPitchToMedianRatio = 1.5f;
    private const int MinimumEnemyListPitchSamples = 3;
    private const int MinEnemyListOverlayFontSize = 8;
    private const int MaxEnemyListOverlayFontSize = 32;
    private bool ApplyEnemyListUpperLayout(
        AtkTextNode* pluginNode,
        float x,
        float y,
        ushort height)
    {
        var node = &pluginNode->AtkResNode;
        var changed = false;
        if (node->X != x || node->Y != y)
        {
            node->SetPositionFloat(x, y);
            changed = true;
        }
        if (node->Height != height)
        {
            node->Height = height;
            changed = true;
        }

        return changed;
    }
    private static bool TryGetEnemyListOverlayRowNodes(
        AtkUnitBase* addon,
        AtkResNode* root,
        AtkUldManager* addonManager,
        int slot,
        out AtkComponentNode* rowOwner,
        out AtkResNode* castBarNode,
        out AtkTextNode* nativeNode,
        out string failure)
    {
        rowOwner = null;
        castBarNode = null;
        nativeNode = null;
        if (!TryGetEnemyListOverlayRowOwner(
                addon,
                root,
                addonManager,
                slot,
                out rowOwner,
                out var componentManager,
                out failure))
        {
            return false;
        }
        if (componentManager->NodeListCount <= 16)
        {
            failure = "row-node-list-too-short";
            return false;
        }

        var castBarCandidate = componentManager->NodeList[12];
        if (castBarCandidate == null ||
            !IsUniqueEnemyListNode(componentManager, (nint)castBarCandidate))
        {
            failure = "node12-not-unique";
            return false;
        }
        if (castBarCandidate->Type is not (NodeType.Image or NodeType.NineGrid))
        {
            failure = "node12-not-cast-bar-graphic";
            return false;
        }
        if (castBarCandidate->Width == 0 || castBarCandidate->Height == 0)
        {
            failure = "node12-empty-bounds";
            return false;
        }

        var textCandidate = componentManager->NodeList[16];
        if (textCandidate == null ||
            !IsUniqueEnemyListNode(componentManager, (nint)textCandidate))
        {
            failure = "node16-not-unique";
            return false;
        }
        if (textCandidate->Type != NodeType.Text)
        {
            failure = "node16-not-text";
            return false;
        }

        nativeNode = (AtkTextNode*)textCandidate;
        if (nativeNode->AtkResNode.Width == 0 || nativeNode->FontSize == 0)
        {
            nativeNode = null;
            failure = "node16-empty-width-or-font";
            return false;
        }

        castBarNode = castBarCandidate;
        failure = string.Empty;
        return true;
    }
    private static bool TryGetEnemyListOverlayRowOwner(
        AtkUnitBase* addon,
        AtkResNode* root,
        AtkUldManager* addonManager,
        int slot,
        out AtkComponentNode* rowOwner,
        out AtkUldManager* componentManager,
        out string failure)
    {
        rowOwner = null;
        componentManager = null;
        if (addon == null || root == null || addon->RootNode != root)
        {
            failure = "invalid-addon-root";
            return false;
        }
        if (slot < 0 || slot >= MaxEnemyListOverlaySlots)
        {
            failure = "slot-out-of-range";
            return false;
        }
        if (!IsValidEnemyListUldManager(addonManager))
        {
            failure = "addon-uld-invalid";
            return false;
        }

        var enemyList = (AddonEnemyList*)addon;
        var component = *(&enemyList->EnemyOneComponent)[slot];
        if (component == null)
        {
            failure = "row-component-null";
            return false;
        }

        var rowManager = &component->AtkComponentBase.UldManager;
        if (!IsValidEnemyListUldManager(rowManager))
        {
            failure = "row-uld-invalid";
            return false;
        }

        var owner = component->AtkComponentBase.OwnerNode;
        if (owner == null ||
            (!IsUniqueEnemyListNode(rowManager, (nint)owner) &&
             !IsUniqueEnemyListNode(addonManager, (nint)owner)))
        {
            failure = "row-owner-not-unique";
            return false;
        }

        rowOwner = owner;
        componentManager = rowManager;
        failure = string.Empty;
        return true;
    }
    private static bool TryMeasureEnemyRowPitchScreen(
        AtkUnitBase* addon,
        AtkResNode* root,
        AtkUldManager* addonManager,
        out float rowPitchScreen,
        out int sampleCount,
        out string failure)
    {
        rowPitchScreen = float.NaN;
        sampleCount = 0;

        Span<Vector2> ownerOrigins = stackalloc Vector2[MaxEnemyListOverlaySlots];
        Span<bool> hasOwnerOrigin = stackalloc bool[MaxEnemyListOverlaySlots];
        for (var slot = 0; slot < MaxEnemyListOverlaySlots; slot++)
        {
            if (!TryGetEnemyListOverlayRowOwner(
                    addon,
                    root,
                    addonManager,
                    slot,
                    out var rowOwner,
                    out _,
                    out _) ||
                !TryNodeLocalPointToScreen(
                    &rowOwner->AtkResNode,
                    Vector2.Zero,
                    out var ownerOrigin,
                    out _))
            {
                continue;
            }

            ownerOrigins[slot] = ownerOrigin;
            hasOwnerOrigin[slot] = true;
        }

        Span<float> pitches = stackalloc float[MaxEnemyListOverlaySlots - 1];
        var validCount = 0;
        for (var slot = 0; slot < MaxEnemyListOverlaySlots - 1; slot++)
        {
            if (!hasOwnerOrigin[slot] || !hasOwnerOrigin[slot + 1])
                continue;

            var pitch = ownerOrigins[slot + 1].Y - ownerOrigins[slot].Y;
            if (!float.IsFinite(pitch) || pitch <= 0)
                continue;
            pitches[validCount++] = pitch;
        }

        if (validCount < MinimumEnemyListPitchSamples)
        {
            failure = "row-pitch-insufficient-adjacent-samples";
            return false;
        }

        pitches[..validCount].Sort();
        var initialMedian = CalculateMedian(pitches[..validCount]);
        var minimumPitch = initialMedian * MinPitchToMedianRatio;
        var maximumPitch = initialMedian * MaxPitchToMedianRatio;
        if (!float.IsFinite(initialMedian) || initialMedian <= 0 ||
            !float.IsFinite(minimumPitch) || !float.IsFinite(maximumPitch))
        {
            failure = "row-pitch-median-invalid";
            return false;
        }

        var consistentCount = 0;
        for (var index = 0; index < validCount; index++)
        {
            var pitch = pitches[index];
            if (pitch < minimumPitch || pitch > maximumPitch)
                continue;
            pitches[consistentCount++] = pitch;
        }

        if (consistentCount < MinimumEnemyListPitchSamples)
        {
            failure = "row-pitch-inconsistent-adjacent-samples";
            return false;
        }

        rowPitchScreen = CalculateMedian(pitches[..consistentCount]);
        if (!float.IsFinite(rowPitchScreen) || rowPitchScreen <= 0)
        {
            rowPitchScreen = float.NaN;
            failure = "row-pitch-final-median-invalid";
            return false;
        }

        sampleCount = consistentCount;
        failure = string.Empty;
        return true;
    }
    private static float CalculateMedian(Span<float> values)
    {
        values.Sort();
        var middle = values.Length / 2;
        return values.Length % 2 == 0
            ? (float)(((double)values[middle - 1] + values[middle]) * 0.5d)
            : values[middle];
    }
    private static bool TryGetDerivedEnemyListOverlayFontSize(
        byte nativeFontSize,
        out byte derivedFontSize,
        out string failure)
    {
        derivedFontSize = 0;
        if (nativeFontSize == 0)
        {
            failure = "native-font-size-zero";
            return false;
        }

        var scaledFontSize = MathF.Round(
            nativeFontSize * EnemyListOverlayFontScale,
            MidpointRounding.AwayFromZero);
        if (!float.IsFinite(scaledFontSize))
        {
            failure = "derived-font-size-non-finite";
            return false;
        }

        derivedFontSize = (byte)Math.Clamp(
            (int)scaledFontSize,
            MinEnemyListOverlayFontSize,
            MaxEnemyListOverlayFontSize);
        failure = string.Empty;
        return true;
    }
    private static bool TryCalculateEnemyListOverlayAutoLayout(
        AtkResNode* root,
        AtkResNode* castBarNode,
        AtkTextNode* nativeNode,
        AtkTextNode* pluginNode,
        float rowPitchScreen,
        int rowPitchSampleCount,
        Vector2 nativeRightScreen,
        float targetX,
        ushort pluginWidth,
        byte derivedPluginFontSize,
        ushort measuredTextWidth,
        ushort measuredTextHeight,
        out float pluginRootLocalY,
        out string failure)
    {
        pluginRootLocalY = 0;
        if (root == null || castBarNode == null || nativeNode == null || pluginNode == null)
        {
            failure = "auto-layout-node-null";
            return false;
        }
        if (rowPitchSampleCount < MinimumEnemyListPitchSamples ||
            !float.IsFinite(rowPitchScreen) || rowPitchScreen <= 0)
        {
            failure = "auto-layout-row-pitch-invalid";
            return false;
        }
        if (pluginWidth == 0 || measuredTextWidth == 0 ||
            measuredTextHeight == 0 || derivedPluginFontSize == 0)
        {
            failure = "auto-layout-measurement-invalid";
            return false;
        }
        if (castBarNode->Type is not (NodeType.Image or NodeType.NineGrid) ||
            castBarNode->Width == 0 || castBarNode->Height == 0 ||
            nativeNode->AtkResNode.Type != NodeType.Text ||
            nativeNode->AtkResNode.Width == 0 || nativeNode->FontSize == 0)
        {
            failure = "auto-layout-native-node-invalid";
            return false;
        }

        var barWidth = castBarNode->Width;
        var barHeight = castBarNode->Height;
        if (!TryNodeLocalPointToScreen(
                castBarNode,
                Vector2.Zero,
                out var barTopLeft,
                out failure) ||
            !TryNodeLocalPointToScreen(
                castBarNode,
                new Vector2(barWidth, 0),
                out var barTopRight,
                out failure) ||
            !TryNodeLocalPointToScreen(
                castBarNode,
                new Vector2(0, barHeight),
                out var barBottomLeft,
                out failure) ||
            !TryNodeLocalPointToScreen(
                castBarNode,
                new Vector2(barWidth, barHeight),
                out var barBottomRight,
                out failure))
        {
            failure = $"node12-{failure}";
            return false;
        }

        var currentBarTopScreenY = MathF.Min(
            MathF.Min(barTopLeft.Y, barTopRight.Y),
            MathF.Min(barBottomLeft.Y, barBottomRight.Y));
        var currentBarBottomScreenY = MathF.Max(
            MathF.Max(barTopLeft.Y, barTopRight.Y),
            MathF.Max(barBottomLeft.Y, barBottomRight.Y));

        var nativeResNode = &nativeNode->AtkResNode;
        if (!TryNodeLocalPointToScreen(
                nativeResNode,
                Vector2.Zero,
                out var nativeTextTopLeft,
                out failure) ||
            !TryNodeLocalPointToScreen(
                nativeResNode,
                new Vector2(nativeResNode->Width, 0),
                out var nativeTextTopRight,
                out failure))
        {
            failure = $"node16-{failure}";
            return false;
        }

        var nativeCastTopScreenY = MathF.Min(nativeTextTopLeft.Y, nativeTextTopRight.Y);
        var previousVirtualBarBottomScreenY = currentBarBottomScreenY - rowPitchScreen;
        var gapTop = previousVirtualBarBottomScreenY;
        var gapBottom = nativeCastTopScreenY;
        var gapHeight = gapBottom - gapTop;
        if (!float.IsFinite(currentBarTopScreenY) ||
            !float.IsFinite(currentBarBottomScreenY) ||
            !float.IsFinite(previousVirtualBarBottomScreenY) ||
            !float.IsFinite(nativeCastTopScreenY) ||
            !float.IsFinite(gapHeight) ||
            gapHeight < 0)
        {
            failure = "auto-layout-gap-invalid";
            return false;
        }

        var gapCenterScreenY = ((double)gapTop + gapBottom) * 0.5d;
        if (!double.IsFinite(gapCenterScreenY) ||
            Math.Abs(gapCenterScreenY) > float.MaxValue)
        {
            failure = "auto-layout-center-non-finite";
            return false;
        }

        if (!TryGetNodeScreenTransform(
                root,
                out var rootTransform,
                out var rootScreenOrigin,
                out failure))
        {
            failure = $"addon-root-{failure}";
            return false;
        }

        var largestRootCoefficient = Math.Max(
            Math.Max(Math.Abs((double)rootTransform.M11), Math.Abs((double)rootTransform.M12)),
            Math.Max(Math.Abs((double)rootTransform.M21), Math.Abs((double)rootTransform.M22)));
        if (largestRootCoefficient == 0 ||
            Math.Abs((double)rootTransform.M22) <=
            largestRootCoefficient * TransformDeterminantRelativeTolerance)
        {
            failure = "addon-root-vertical-transform-unusable";
            return false;
        }

        if (!TryGetEnemyListOverlayTextBoundsInParent(
                nativeResNode,
                pluginWidth,
                measuredTextWidth,
                measuredTextHeight,
                out var textTopLeftParentOffset,
                out var textTopRightParentOffset,
                out var textBottomLeftParentOffset,
                out var textBottomRightParentOffset,
                out failure))
        {
            failure = $"node16-{failure}";
            return false;
        }

        // Native-parent offsets are root-local for the plugin because it copies this node transform.
        var pluginRootLocalYDouble = CalculateEnemyListOverlayRootLocalY(
            rootScreenOrigin.Y,
            rootTransform.M21,
            rootTransform.M22,
            targetX,
            gapCenterScreenY,
            textTopLeftParentOffset,
            textTopRightParentOffset,
            textBottomLeftParentOffset,
            textBottomRightParentOffset);
        if (!double.IsFinite(pluginRootLocalYDouble) ||
            Math.Abs(pluginRootLocalYDouble) > float.MaxValue)
        {
            failure = "addon-root-local-y-non-finite";
            return false;
        }

        if (!TryNodeLocalPointToScreen(
                root,
                new Vector2(targetX, (float)pluginRootLocalYDouble),
                out var pluginPositionScreen,
                out failure) ||
            !TryScreenPointToNodeLocal(
                root,
                pluginPositionScreen,
                out var pluginRootLocal,
                out failure))
        {
            failure = $"addon-root-{failure}";
            return false;
        }

        var rootLocalXTolerance = Math.Max(1d, Math.Abs((double)targetX)) *
                                  TransformDeterminantRelativeTolerance * 10d;
        if (Math.Abs((double)pluginRootLocal.X - targetX) > rootLocalXTolerance)
        {
            failure = "addon-root-local-x-inversion-mismatch";
            return false;
        }

        var expectedPluginRightLocalX = targetX + pluginWidth;
        if (!TryNodeLocalPointToScreen(
                root,
                new Vector2(expectedPluginRightLocalX, pluginRootLocal.Y),
                out var expectedPluginRightScreen,
                out failure))
        {
            failure = $"addon-root-{failure}";
            return false;
        }

        var projectedRightEdgeDeltaX = expectedPluginRightScreen.X - nativeRightScreen.X;
        if (!float.IsFinite(projectedRightEdgeDeltaX))
        {
            failure = "projected-right-edge-delta-non-finite";
            return false;
        }

        pluginRootLocalY = pluginRootLocal.Y;

        failure = string.Empty;
        return true;
    }
    private static bool TryGetEnemyListOverlayTextBoundsInParent(
        AtkResNode* nativeNode,
        ushort nodeWidth,
        ushort measuredTextWidth,
        ushort measuredTextHeight,
        out Vector2 topLeftOffset,
        out Vector2 topRightOffset,
        out Vector2 bottomLeftOffset,
        out Vector2 bottomRightOffset,
        out string failure)
    {
        topLeftOffset = default;
        topRightOffset = default;
        bottomLeftOffset = default;
        bottomRightOffset = default;
        if (nativeNode == null || nodeWidth == 0 ||
            measuredTextWidth == 0 || measuredTextHeight == 0)
        {
            failure = "text-bounds-input-invalid";
            return false;
        }

        var parent = nativeNode->ParentNode;
        if (parent == null)
        {
            failure = "node-parent-null";
            return false;
        }

        // Map native text bounds through the parent; new plugin ScreenX/ScreenY may be stale.
        var visibleTextWidth = Math.Min(nodeWidth, measuredTextWidth);
        var textLeftX = (float)(nodeWidth - visibleTextWidth);
        var textRightX = (float)nodeWidth;
        if (!TryNodeLocalPointToParentLocal(
                nativeNode,
                parent,
                new Vector2(textLeftX, 0),
                out var topLeftInParent,
                out failure) ||
            !TryNodeLocalPointToParentLocal(
                nativeNode,
                parent,
                new Vector2(textRightX, 0),
                out var topRightInParent,
                out failure) ||
            !TryNodeLocalPointToParentLocal(
                nativeNode,
                parent,
                new Vector2(textLeftX, measuredTextHeight),
                out var bottomLeftInParent,
                out failure) ||
            !TryNodeLocalPointToParentLocal(
                nativeNode,
                parent,
                new Vector2(textRightX, measuredTextHeight),
                out var bottomRightInParent,
                out failure))
        {
            return false;
        }

        if (!TryCreateFiniteVector2(
                (double)topLeftInParent.X - nativeNode->X,
                (double)topLeftInParent.Y - nativeNode->Y,
                out topLeftOffset) ||
            !TryCreateFiniteVector2(
                (double)topRightInParent.X - nativeNode->X,
                (double)topRightInParent.Y - nativeNode->Y,
                out topRightOffset) ||
            !TryCreateFiniteVector2(
                (double)bottomLeftInParent.X - nativeNode->X,
                (double)bottomLeftInParent.Y - nativeNode->Y,
                out bottomLeftOffset) ||
            !TryCreateFiniteVector2(
                (double)bottomRightInParent.X - nativeNode->X,
                (double)bottomRightInParent.Y - nativeNode->Y,
                out bottomRightOffset))
        {
            failure = "text-bounds-offset-non-finite";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private static bool TryNodeLocalPointToParentLocal(
        AtkResNode* node,
        AtkResNode* parent,
        Vector2 localPoint,
        out Vector2 parentLocalPoint,
        out string failure)
    {
        parentLocalPoint = default;
        if (node == null || parent == null)
        {
            failure = "node-or-parent-null";
            return false;
        }

        if (!TryNodeLocalPointToScreen(node, localPoint, out var screenPoint, out failure))
            return false;

        return TryScreenPointToNodeLocal(parent, screenPoint, out parentLocalPoint, out failure);
    }

    internal static double CalculateEnemyListOverlayRootLocalY(
        float rootScreenOriginY,
        float rootTransformM21,
        float rootTransformM22,
        float targetX,
        double gapCenterScreenY,
        Vector2 textTopLeftOffset,
        Vector2 textTopRightOffset,
        Vector2 textBottomLeftOffset,
        Vector2 textBottomRightOffset)
    {
        // Project all transformed corners to screen Y, then solve root-local Y at fixed X.
        var topLeftScreenOffsetY =
            -(double)textTopLeftOffset.X * rootTransformM21 +
            (double)textTopLeftOffset.Y * rootTransformM22;
        var topRightScreenOffsetY =
            -(double)textTopRightOffset.X * rootTransformM21 +
            (double)textTopRightOffset.Y * rootTransformM22;
        var bottomLeftScreenOffsetY =
            -(double)textBottomLeftOffset.X * rootTransformM21 +
            (double)textBottomLeftOffset.Y * rootTransformM22;
        var bottomRightScreenOffsetY =
            -(double)textBottomRightOffset.X * rootTransformM21 +
            (double)textBottomRightOffset.Y * rootTransformM22;
        var visualCenterScreenOffsetY =
            (Math.Min(
                 Math.Min(topLeftScreenOffsetY, topRightScreenOffsetY),
                 Math.Min(bottomLeftScreenOffsetY, bottomRightScreenOffsetY)) +
             Math.Max(
                 Math.Max(topLeftScreenOffsetY, topRightScreenOffsetY),
                 Math.Max(bottomLeftScreenOffsetY, bottomRightScreenOffsetY))) *
            0.5d;

        return (gapCenterScreenY - rootScreenOriginY +
                (double)targetX * rootTransformM21 - visualCenterScreenOffsetY) /
               rootTransformM22;
    }

    private static bool TryCalculateEnemyListOverlayX(
        AtkResNode* root,
        AtkTextNode* nativeNode,
        out Vector2 nativeRightScreen,
        out Vector2 nativeRightRootLocal,
        out float pluginX,
        out string failure)
    {
        nativeRightScreen = default;
        nativeRightRootLocal = default;
        pluginX = 0;
        if (nativeNode == null)
        {
            failure = "node16-null";
            return false;
        }

        var nativeResNode = &nativeNode->AtkResNode;
        if (!TryNodeLocalPointToScreen(
                nativeResNode,
                new Vector2(nativeResNode->Width, 0),
                out nativeRightScreen,
                out failure))
        {
            failure = $"node16-{failure}";
            return false;
        }
        if (!TryScreenPointToNodeLocal(
                root,
                nativeRightScreen,
                out nativeRightRootLocal,
                out failure))
        {
            failure = $"addon-root-{failure}";
            return false;
        }

        if (nativeResNode->Width == 0)
        {
            failure = "node16-width-zero";
            return false;
        }
        pluginX = nativeRightRootLocal.X - nativeResNode->Width;
        if (!float.IsFinite(pluginX))
        {
            failure = "plugin-x-non-finite";
            return false;
        }

        failure = string.Empty;
        return true;
    }
    private static bool TryNodeLocalPointToScreen(
        AtkResNode* node,
        Vector2 localPoint,
        out Vector2 screenPoint,
        out string failure)
    {
        screenPoint = default;
        if (!float.IsFinite(localPoint.X) || !float.IsFinite(localPoint.Y))
        {
            failure = "local-point-non-finite";
            return false;
        }
        if (!TryGetNodeScreenTransform(node, out var transform, out var screenOrigin, out failure))
            return false;

        // AtkResNode screen-space convention: x'=x*M11-y*M12, y'=-x*M21+y*M22.
        var screenX = (double)screenOrigin.X +
                      (double)localPoint.X * transform.M11 -
                      (double)localPoint.Y * transform.M12;
        var screenY = (double)screenOrigin.Y -
                      (double)localPoint.X * transform.M21 +
                      (double)localPoint.Y * transform.M22;
        if (!TryCreateFiniteVector2(screenX, screenY, out screenPoint))
        {
            failure = "screen-point-non-finite";
            return false;
        }

        failure = string.Empty;
        return true;
    }
    private static bool TryScreenPointToNodeLocal(
        AtkResNode* node,
        Vector2 screenPoint,
        out Vector2 localPoint,
        out string failure)
    {
        localPoint = default;
        if (!float.IsFinite(screenPoint.X) || !float.IsFinite(screenPoint.Y))
        {
            failure = "screen-point-non-finite";
            return false;
        }
        if (!TryGetNodeScreenTransform(node, out var transform, out var screenOrigin, out failure))
            return false;

        // Inverse of [[M11,-M12],[-M21,M22]] under the UI screen-Y convention above.
        var determinant =
            (double)transform.M11 * transform.M22 -
            (double)transform.M12 * transform.M21;
        var deltaX = (double)screenPoint.X - screenOrigin.X;
        var deltaY = (double)screenPoint.Y - screenOrigin.Y;
        var localX = (deltaX * transform.M22 + deltaY * transform.M12) / determinant;
        var localY = (deltaX * transform.M21 + deltaY * transform.M11) / determinant;
        if (!TryCreateFiniteVector2(localX, localY, out localPoint))
        {
            failure = "local-point-non-finite";
            return false;
        }

        failure = string.Empty;
        return true;
    }
    private static bool TryGetNodeScreenTransform(
        AtkResNode* node,
        out Matrix2x2 transform,
        out Vector2 screenOrigin,
        out string failure)
    {
        transform = default;
        screenOrigin = default;
        if (node == null)
        {
            failure = "node-null";
            return false;
        }

        transform = node->Transform;
        screenOrigin = new Vector2(node->ScreenX, node->ScreenY);
        if (!float.IsFinite(screenOrigin.X) || !float.IsFinite(screenOrigin.Y) ||
            !float.IsFinite(transform.M11) || !float.IsFinite(transform.M12) ||
            !float.IsFinite(transform.M21) || !float.IsFinite(transform.M22))
        {
            failure = "transform-non-finite";
            return false;
        }

        var largestCoefficient = Math.Max(
            Math.Max(Math.Abs((double)transform.M11), Math.Abs((double)transform.M12)),
            Math.Max(Math.Abs((double)transform.M21), Math.Abs((double)transform.M22)));
        var determinant =
            (double)transform.M11 * transform.M22 -
            (double)transform.M12 * transform.M21;
        if (largestCoefficient == 0 ||
            !double.IsFinite(determinant) ||
            Math.Abs(determinant) <=
            largestCoefficient * largestCoefficient * TransformDeterminantRelativeTolerance)
        {
            failure = "transform-near-singular";
            return false;
        }

        failure = string.Empty;
        return true;
    }
    private static bool TryCreateFiniteVector2(double x, double y, out Vector2 value)
    {
        value = default;
        if (!double.IsFinite(x) || !double.IsFinite(y) ||
            Math.Abs(x) > float.MaxValue || Math.Abs(y) > float.MaxValue)
        {
            return false;
        }

        var result = new Vector2((float)x, (float)y);
        if (!float.IsFinite(result.X) || !float.IsFinite(result.Y))
            return false;

        value = result;
        return true;
    }
    private static bool IsUniqueEnemyListNode(AtkUldManager* manager, nint nodePointer)
    {
        if (nodePointer == 0 ||
            manager == null ||
            manager->NodeListCount > MaxSecondNodeChildChain ||
            manager->NodeList == null)
        {
            return false;
        }

        var matches = 0;
        for (var index = 0; index < manager->NodeListCount; index++)
        {
            if ((nint)manager->NodeList[index] == nodePointer)
                matches++;
        }

        return matches == 1;
    }
    private static bool IsValidEnemyListUldManager(AtkUldManager* manager)
    {
        if (manager == null ||
            manager->NodeListCount > MaxSecondNodeChildChain ||
            (manager->NodeListCount > 0 && manager->NodeList == null) ||
            manager->Objects == null ||
            manager->Objects->NodeCount < 0 ||
            manager->Objects->NodeCount > MaxSecondNodeChildChain ||
            (manager->Objects->NodeCount > 0 && manager->Objects->NodeList == null))
        {
            return false;
        }

        return true;
    }
    private static bool ValidateLiveEnemyListOverlay(
        AtkUnitBase* addon,
        AtkResNode* root,
        AtkUldManager* manager,
        SecondNodeState state)
    {
        if (state.Lifetime != SecondNodeLifetime.Alive ||
            !state.Attached ||
            !state.OwnershipVerified ||
            state.ParentIsAddonRoot == false ||
            state.AddonPointer != (nint)addon ||
            state.ParentPointer != (nint)root ||
            state.UldManagerPointer != (nint)manager ||
            (nint)addon->RootNode != (nint)root ||
            manager == null ||
            manager->NodeList == null ||
            manager->NodeListCount > MaxSecondNodeChildChain ||
            manager->Objects == null ||
            manager->Objects->NodeCount < 0 ||
            manager->Objects->NodeCount > MaxSecondNodeChildChain ||
            (manager->Objects->NodeCount > 0 && manager->Objects->NodeList == null))
        {
            return false;
        }

        var nodePointerCount = 0;
        var nodeIdCount = 0;
        for (var index = 0; index < manager->NodeListCount; index++)
        {
            var node = manager->NodeList[index];
            if ((nint)node == state.PluginNodePointer)
                nodePointerCount++;
            if (node != null && node->NodeId == state.NodeId)
                nodeIdCount++;
        }

        var objectPointerCount = 0;
        var objectIdCount = 0;
        var objects = manager->Objects;
        for (var index = 0; index < objects->NodeCount; index++)
        {
            var node = objects->NodeList[index];
            if ((nint)node == state.PluginNodePointer)
                objectPointerCount++;
            if (node != null && node->NodeId == state.NodeId)
                objectIdCount++;
        }

        if (nodePointerCount != 1 || nodeIdCount != 1 ||
            objectPointerCount != 1 || objectIdCount != 1)
        {
            return false;
        }

        var plugin = (AtkTextNode*)state.PluginNodePointer;
        return plugin->AtkResNode.NodeId == state.NodeId &&
               plugin->AtkResNode.ParentNode == root &&
               TryScanEnemyListRoot(root, state.PluginNodePointer, out _);
    }
    private static bool TryScanEnemyListRoot(
        AtkResNode* root,
        nint pluginPointer,
        out int visitedCount)
    {
        visitedCount = 0;
        if (root == null || root->ChildCount > MaxSecondNodeChildChain)
            return false;

        var pluginCount = 0;
        var child = root->ChildNode;
        while (child != null && visitedCount < MaxSecondNodeChildChain)
        {
            visitedCount++;
            if ((nint)child == pluginPointer)
                pluginCount++;
            child = child->PrevSiblingNode;
        }

        return child == null &&
               visitedCount == root->ChildCount &&
               pluginCount == (pluginPointer == 0 ? 0 : 1);
    }
    private static TextFlags GetEnemyListTextFlowFlags(
        TextFlags flags,
        bool useEllipsis)
    {
        flags &= ~(TextFlags.MultiLine | TextFlags.WordWrap | TextFlags.AutoAdjustNodeSize);
        return useEllipsis
            ? (flags & ~TextFlags.OverflowHidden) | TextFlags.Ellipsis
            : (flags & ~TextFlags.Ellipsis) | TextFlags.OverflowHidden;
    }
}
