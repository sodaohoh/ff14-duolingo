using System.Numerics;
using CastBarTranslator.Features;
using Xunit;

namespace CastBarTranslator.Tests;

public sealed class EnemyListOverlayLayoutMathTests
{
    [Fact]
    public void IdentityTransformCentersFifteenLocalUnitsBySevenPointFive()
    {
        var y = Calculate(
            rootTransformM21: 0,
            rootTransformM22: 1,
            new Vector2(0, 0),
            new Vector2(40, 0),
            new Vector2(0, 15),
            new Vector2(40, 15));

        Assert.Equal(92.5d, y, 5);
    }

    [Fact]
    public void UniformRootScaleCentersEighteenScreenUnitsOfText()
    {
        var y = Calculate(
            rootTransformM21: 0,
            rootTransformM22: 1.2f,
            new Vector2(0, 0),
            new Vector2(40, 0),
            new Vector2(0, 15),
            new Vector2(40, 15));

        Assert.Equal(100d, y * 1.2d + 9d, 5);
    }

    [Fact]
    public void NonUniformRootAndNodeScalesUseCombinedVerticalTransform()
    {
        var y = Calculate(
            rootTransformM21: 0,
            rootTransformM22: 1.5f,
            new Vector2(0, 0),
            new Vector2(30, 0),
            new Vector2(0, 7.5f),
            new Vector2(30, 7.5f));

        Assert.Equal(100d, y * 1.5d + 5.625d, 5);
    }

    [Fact]
    public void RotationShearAndOriginOffsetAffectVisibleTextCenter()
    {
        var y = Calculate(
            rootTransformM21: -0.5f,
            rootTransformM22: 1,
            new Vector2(29, 16),
            new Vector2(53, 34),
            new Vector2(20, 28),
            new Vector2(44, 46));

        Assert.Equal(50.75d, y, 5);
    }

    [Fact]
    public void RootVerticalShearAccountsForFixedLocalX()
    {
        var y = Calculate(
            rootTransformM21: -0.5f,
            rootTransformM22: 1,
            new Vector2(0, 0),
            new Vector2(40, 0),
            new Vector2(0, 15),
            new Vector2(40, 15),
            targetX: 10);

        Assert.Equal(77.5d, y, 5);
    }
    private static double Calculate(
        float rootTransformM21,
        float rootTransformM22,
        Vector2 textTopLeftOffset,
        Vector2 textTopRightOffset,
        Vector2 textBottomLeftOffset,
        Vector2 textBottomRightOffset,
        float targetX = 0) =>
        CastBarFeature.CalculateEnemyListOverlayRootLocalY(
            rootScreenOriginY: 0,
            rootTransformM21,
            rootTransformM22,
            targetX,
            gapCenterScreenY: 100,
            textTopLeftOffset,
            textTopRightOffset,
            textBottomLeftOffset,
            textBottomRightOffset);
}
