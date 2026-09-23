using CastBarTranslator.Translation;
using CastBarTranslator;
using Xunit;

namespace CastBarTranslator.Tests;

public sealed class TranslationServiceTests
{
    [Theory]
    [InlineData(GameLanguage.English, "Fire IV")]
    [InlineData(GameLanguage.Japanese, "ファイジャ")]
    [InlineData(GameLanguage.German, "Feuga")]
    [InlineData(GameLanguage.French, "Brasier IV")]
    [InlineData(GameLanguage.ChineseTraditional, "烈火四")]
    public void ConfiguredLanguageLoadsForSecondLine(
        GameLanguage language,
        string expectedName)
    {
        using var fixture = new TranslationFixture();
        fixture.Write(language, $"{{\"123\":\"{expectedName}\"}}");

        var service = fixture.CreateService();
        service.Reload(language);

        Assert.Equal(expectedName, service.GetActionName(123, language));
    }

    [Fact]
    public void MissingActionIdReturnsNoResult()
    {
        using var fixture = new TranslationFixture();
        fixture.Write(GameLanguage.Japanese, "{\"123\":\"ファイジャ\"}");

        var service = fixture.CreateService();
        service.Reload(GameLanguage.Japanese);

        Assert.Null(service.GetActionName(999, GameLanguage.Japanese));
    }

    [Fact]
    public void ReservedActionNamesAreRejected()
    {
        using var fixture = new TranslationFixture();
        fixture.Write(GameLanguage.Japanese, "{\"123\":\"_rsv_internal\"}");

        var service = fixture.CreateService();
        service.Reload(GameLanguage.Japanese);

        Assert.Null(service.GetActionName(123, GameLanguage.Japanese));
    }

    [Fact]
    public void ReloadReplacesPreviousData()
    {
        using var fixture = new TranslationFixture();
        fixture.Write(GameLanguage.Japanese, "{\"123\":\"旧名称\"}");

        var service = fixture.CreateService();
        service.Reload(GameLanguage.Japanese);
        Assert.Equal("旧名称", service.GetActionName(123, GameLanguage.Japanese));

        fixture.Write(GameLanguage.Japanese, "{\"123\":\"新名称\"}");
        service.Reload(GameLanguage.Japanese);

        Assert.Equal("新名称", service.GetActionName(123, GameLanguage.Japanese));
    }

    private sealed class TranslationFixture : IDisposable
    {
        public TranslationFixture()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "CastBarTranslator.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public TranslationService CreateService() => new(DirectoryPath);

        public void Write(GameLanguage language, string json)
        {
            File.WriteAllText(Path.Combine(DirectoryPath, GetFilename(language)), json);
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
                Directory.Delete(DirectoryPath, recursive: true);
        }

        private static string GetFilename(GameLanguage language) => language switch
        {
            GameLanguage.English => "actions_en.json",
            GameLanguage.Japanese => "actions_ja.json",
            GameLanguage.German => "actions_de.json",
            GameLanguage.French => "actions_fr.json",
            GameLanguage.ChineseTraditional => "actions_zhtw.json",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
        };
    }
}
