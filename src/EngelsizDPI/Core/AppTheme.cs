using Avalonia;
using Avalonia.Styling;

namespace EngelsizDPI.Core;

/// <summary>Tema seçimi: sistemle aynı (varsayılan), açık (kırık beyaz) ya da koyu (gece mavisi).</summary>
public static class AppTheme
{
    public const string System = "system", Light = "light", Dark = "dark";

    public static string Normalize(string? theme) => theme is Light or Dark ? theme : System;

    public static void Apply(string theme)
    {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = Normalize(theme) switch
        {
            Light => ThemeVariant.Light,
            Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
