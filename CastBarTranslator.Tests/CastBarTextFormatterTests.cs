using CastBarTranslator.Presentation;
using Xunit;

namespace CastBarTranslator.Tests;

public sealed class CastBarTextFormatterTests
{
    [Fact]
    public void DistinctNamesProduceTwoLines()
    {
        Assert.Equal("ファイジャ\n火焰 IV", CastBarTextFormatter.Format("ファイジャ", "火焰 IV"));
    }

    [Fact]
    public void MissingTopNameProducesNoReplacement()
    {
        Assert.Null(CastBarTextFormatter.Format(null, "火焰 IV"));
        Assert.Null(CastBarTextFormatter.Format(string.Empty, "火焰 IV"));
    }

    [Fact]
    public void MissingBottomNameProducesNoReplacement()
    {
        Assert.Null(CastBarTextFormatter.Format("ファイジャ", null));
        Assert.Null(CastBarTextFormatter.Format("ファイジャ", string.Empty));
    }

    [Fact]
    public void IdenticalNamesProduceNoReplacement()
    {
        Assert.Null(CastBarTextFormatter.Format("ファイジャ", "ファイジャ"));
    }
}
