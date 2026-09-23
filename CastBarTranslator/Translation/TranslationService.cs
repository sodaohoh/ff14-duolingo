using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace CastBarTranslator.Translation;

public sealed class TranslationService
{
    private static readonly IReadOnlyDictionary<GameLanguage, string> DataFilenames =
        new Dictionary<GameLanguage, string>
        {
            [GameLanguage.English] = "actions_en.json",
            [GameLanguage.Japanese] = "actions_ja.json",
            [GameLanguage.German] = "actions_de.json",
            [GameLanguage.French] = "actions_fr.json",
            [GameLanguage.ChineseTraditional] = "actions_zhtw.json",
        };

    private readonly string? _dataDirectory;
    private IReadOnlyDictionary<GameLanguage, Dictionary<uint, string>> _loadedLanguages =
        new Dictionary<GameLanguage, Dictionary<uint, string>>();
    private GameLanguage? _topLanguage;
    private GameLanguage? _bottomLanguage;

    public TranslationService(string? dataDirectory)
    {
        _dataDirectory = dataDirectory;
    }

    public bool IsLoaded => _topLanguage.HasValue && _bottomLanguage.HasValue;

    public (int TopCount, int BottomCount) Reload(
        GameLanguage topLanguage,
        GameLanguage bottomLanguage,
        params GameLanguage[] additionalLanguages)
    {
        _loadedLanguages = new Dictionary<GameLanguage, Dictionary<uint, string>>();
        _topLanguage = null;
        _bottomLanguage = null;

        var topMap = LoadLanguageData(topLanguage);

        var bottomMap = bottomLanguage == topLanguage
            ? topMap
            : LoadLanguageData(bottomLanguage);
        var loadedLanguages = new Dictionary<GameLanguage, Dictionary<uint, string>>
        {
            [topLanguage] = topMap,
            [bottomLanguage] = bottomMap,
        };

        foreach (var language in additionalLanguages)
        {
            if (!loadedLanguages.ContainsKey(language))
                loadedLanguages[language] = LoadLanguageData(language);
        }

        _loadedLanguages = loadedLanguages;
        _topLanguage = topLanguage;
        _bottomLanguage = bottomLanguage;

        return (topMap.Count, bottomMap.Count);
    }

    public string? GetActionName(uint actionId, GameLanguage language)
    {
        return _loadedLanguages.TryGetValue(language, out var languageMap) &&
               languageMap.TryGetValue(actionId, out var name)
            ? name
            : null;
    }

    private Dictionary<uint, string> LoadLanguageData(GameLanguage language)
    {
        if (string.IsNullOrEmpty(_dataDirectory))
            throw new InvalidOperationException("Unable to determine plugin directory.");

        if (!DataFilenames.TryGetValue(language, out var filename))
            throw new ArgumentOutOfRangeException(nameof(language), language, "No data file defined for language.");

        var path = Path.Combine(_dataDirectory, filename);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Data file not found: {path}", path);

        var json = File.ReadAllText(path);
        var source = JsonConvert.DeserializeObject<Dictionary<uint, string>>(json)
                     ?? throw new InvalidDataException($"Data file is empty or invalid: {path}");
        var filtered = new Dictionary<uint, string>(source.Count);

        foreach (var entry in source)
        {
            if (string.IsNullOrEmpty(entry.Value) ||
                entry.Value.StartsWith("_rsv_", StringComparison.Ordinal))
                continue;

            filtered[entry.Key] = entry.Value;
        }

        return filtered;
    }
}
