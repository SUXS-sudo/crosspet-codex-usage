using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexUsageViewer;

internal static class PetUsagePalette
{
    // Original design colors become semantic resources shared by controls and templates.
    static readonly Dictionary<string, double> Tones = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
    {
        ["#539169"] = -.32, ["#355E43"] = -.48, ["#23262A"] = -.73,
        ["#5C635C"] = -.42, ["#5C6F58"] = -.36, ["#697468"] = -.32,
        ["#828A7C"] = -.22, ["#2C5737"] = -.5, ["#303A30"] = -.65,
        ["#424B44"] = -.56, ["#526452"] = -.44, ["#687263"] = -.33,
        ["#697568"] = -.32, ["#DCE3DA"] = .79, ["#DFE8D7"] = .82,
        ["#E0EDDF"] = .88, ["#E3E7DC"] = .85, ["#E8EFE3"] = .88,
        ["#EAF0E4"] = .90, ["#EAF3E9"] = .94, ["#EDF3E8"] = .93,
        ["#EEF1E8"] = .92, ["#F6F6F1"] = .97, ["#FAFAF7"] = .985,
        ["#C7DDCD"] = .69, ["#C9D7C6"] = .70, ["#DDEBDD"] = .82,
        ["#E2EEDF"] = .87, ["#EBF2E9"] = .92, ["#F0F5EE"] = .95,
        ["#F2F4ED"] = .96
    };

    internal static string Key(string hex) => "PetColor" + hex.TrimStart('#').ToUpperInvariant();

    internal static void Apply(Window window, string accent)
    {
        Color color;
        try { color = (Color)ColorConverter.ConvertFromString(accent); }
        catch { color = Color.FromRgb(123, 92, 214); }
        foreach (var pair in Tones)
        {
            double t = pair.Value;
            byte Channel(byte c) => (byte)Math.Round(t >= 0 ? c + (255 - c) * t : c * (1 + t));
            window.Resources[Key(pair.Key)] = new SolidColorBrush(Color.FromRgb(Channel(color.R), Channel(color.G), Channel(color.B)));
        }
        Bind(window);
    }

    static readonly DependencyProperty[] Properties = {
        Control.BackgroundProperty, Control.ForegroundProperty, Control.BorderBrushProperty,
        Panel.BackgroundProperty, Border.BackgroundProperty, Border.BorderBrushProperty,
        TextBlock.ForegroundProperty, TextBlock.BackgroundProperty, DataGrid.AlternatingRowBackgroundProperty
    };

    static void Bind(DependencyObject node)
    {
        if (node is FrameworkElement element)
            foreach (var property in Properties)
            {
                // Preserve existing bindings, including the dynamic resources installed on prior passes.
                if (DependencyPropertyHelper.GetValueSource(element, property).IsExpression) continue;
                if (element.GetValue(property) is SolidColorBrush brush)
                {
                    var hex = "#" + brush.Color.R.ToString("X2") + brush.Color.G.ToString("X2") + brush.Color.B.ToString("X2");
                    if (brush.Color.A == 255 && Tones.ContainsKey(hex)) element.SetResourceReference(property, Key(hex));
                }
            }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Bind(VisualTreeHelper.GetChild(node, i));
    }
}
