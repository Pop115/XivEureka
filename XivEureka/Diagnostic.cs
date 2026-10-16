using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XivEureka;

/// <summary>
/// Raw captures of what the game exposes, to find where to read a piece of information before
/// relying on it (e.g. the learned Logos actions). Written to the plugin config folder, under
/// <c>releves/</c>. A development tool: it does nothing useful in normal use.
/// </summary>
public static unsafe class Diagnostic
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Writes the capture of a game window (its values) and of the known Logos arrays.</summary>
    public static string Write(string configFolder, string name, AtkUnitBase* window, IReadOnlyDictionary<string, object>? extra = null)
    {
        var capture = new Dictionary<string, object> { ["date"] = DateTime.Now.ToString("s"), ["window"] = name };
        if (extra != null) foreach (var (key, value) in extra) capture[key] = value;
        if (window != null)
        {
            var values = new List<string>();
            foreach (var v in window->AtkValuesSpan) values.Add(Format(v));
            capture["visible"] = window->IsVisible;
            capture["values"] = values;
        }
        var arrays = &RaptureAtkModule.Instance()->AtkArrayDataHolder;
        foreach (var index in new[] { 137, 138 })
        {
            var a = arrays->GetNumberArrayData(index);
            capture[$"NumberArray {index}"] = a == null || a->IntArray == null ? "missing" : a->Span.ToArray();
        }
        var path = Path.Combine(configFolder, "releves", $"{name}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(capture, Indented));
        return path;
    }

    private static string Format(AtkValue v) => v.Type switch
    {
        AtkValueType.Bool => "b:" + v.Bool,
        AtkValueType.Int => "i:" + v.Int,
        AtkValueType.UInt => "u:" + v.UInt,
        AtkValueType.Int64 => "l:" + v.Int64,
        AtkValueType.Float => "f:" + v.Float,
        AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString => "s:" + v.String.ToString(),
        _ => "?" + (int)v.Type,
    };
}
