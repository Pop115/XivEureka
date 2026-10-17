using Dalamud.Game;

namespace XivEureka;

/// <summary>Plugin text language: "auto" follows the game client, or forced.</summary>
public enum TextLanguage
{
    Auto,
    French,
    English,
}

/// <summary>
/// The plugin's own texts, in French or English: both are written side by side where the text is
/// used (<see cref="T"/>), so a text and its translation never drift apart. French when the game
/// client is French, English otherwise (German and Japanese clients included), unless the setting
/// forces one. Names from the game (items, actions, monsters, weathers, FATEs) already come in the
/// client's language. French by default, as before the translation (the tests rely on it).
/// </summary>
public static class Loc
{
    public static bool English { get; private set; }

    /// <summary>The French or the English text.</summary>
    public static string T(string french, string english) => English ? english : french;

    /// <summary>Picks the language from the setting and the game client's language.</summary>
    public static void Apply(TextLanguage setting, ClientLanguage client) =>
        English = setting switch
        {
            TextLanguage.French => false,
            TextLanguage.English => true,
            _ => client != ClientLanguage.French,
        };

    /// <summary>For tests: switch language directly.</summary>
    public static void Set(bool english) => English = english;
}
