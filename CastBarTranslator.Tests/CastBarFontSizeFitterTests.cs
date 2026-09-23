using CastBarTranslator.Presentation;
using Xunit;

namespace CastBarTranslator.Tests;

public sealed class CastBarFontSizeFitterTests
{
    [Fact]
    public void KeepsNativeSizeWhenWidestLineFits()
    {
        var result = CastBarFontSizeFitter.SelectFontSize(16, 200, _ => 150);

        Assert.Equal((byte)16, result);
    }

    [Fact]
    public void ShrinksUntilWidestLineFits()
    {
        var result = CastBarFontSizeFitter.SelectFontSize(
            16,
            100,
            fontSize => (ushort)(fontSize * 10));

        Assert.Equal((byte)10, result);
    }

    [Fact]
    public void StopsAtMinimumWhenTextStillDoesNotFit()
    {
        var minimum = CastBarFontSizeFitter.GetMinimumFontSize(16);
        var result = CastBarFontSizeFitter.SelectFontSize(16, 1, _ => ushort.MaxValue);

        Assert.Equal(minimum, result);
        Assert.Equal((byte)10, minimum);
    }

    [Fact]
    public void StartsNextCalculationFromNativeSize()
    {
        var firstSizes = new List<byte>();
        var secondSizes = new List<byte>();

        CastBarFontSizeFitter.SelectFontSize(
            16,
            100,
            fontSize =>
            {
                firstSizes.Add(fontSize);
                return (ushort)(fontSize * 10);
            });
        CastBarFontSizeFitter.SelectFontSize(
            16,
            200,
            fontSize =>
            {
                secondSizes.Add(fontSize);
                return 150;
            });

        Assert.Equal((byte)16, firstSizes[0]);
        Assert.Equal((byte)16, secondSizes[0]);
    }
}
