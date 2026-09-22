namespace CastBarTranslator.Presentation;

public static class CastBarTextFormatter
{
    public static string? Format(string? topName, string? bottomName)
    {
        if (string.IsNullOrEmpty(topName) || string.IsNullOrEmpty(bottomName))
            return null;

        if (topName == bottomName)
            return null;

        return $"{topName}\n{bottomName}";
    }
}
