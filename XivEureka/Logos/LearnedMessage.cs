using System;
using System.Collections.Generic;
using System.Linq;

namespace XivEureka.Logos;

/// <summary>
/// The system message the game prints when the Logos manipulator teaches an action (LogMessage
/// 9070, "Vous avez découvert l'action Logos “…”." / "The logos action “…” is realized."). Its
/// only parameter is the action's name, not its id, so the action is found back by name.
/// </summary>
public static class LearnedMessage
{
    public const uint Id = 9070;

    /// <summary>
    /// The action (Action sheet row) whose name is <paramref name="text"/>, ignoring case, spaces
    /// and quotes; the short name ("Soin" for "Évocation Logos : Soin") also matches. Null if none.
    /// </summary>
    public static uint? Match(string text, IEnumerable<(uint Action, string Name)> actions)
    {
        var wanted = Normalize(text);
        if (wanted.Length == 0) return null;
        var list = actions.ToList();
        foreach (var (action, name) in list)
            if (Normalize(name) == wanted) return action;
        foreach (var (action, name) in list)
            if (Normalize(GameTexts.Shorten(name)) == wanted) return action;
        return null;
    }

    private static string Normalize(string s) =>
        s.Trim().Trim('“', '”', '"', '«', '»', ' ').ToLowerInvariant();
}
