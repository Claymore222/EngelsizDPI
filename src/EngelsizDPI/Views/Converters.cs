using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using EngelsizDPI.ViewModels;

namespace EngelsizDPI.Views;

/// <summary>
/// Tepsi ikonunun durum renkleri (koyu tema tonları; tepsi Windows'un kendi zemininde durur). Pencere içindeki
/// renkler App.axaml'daki tema kaynaklarından gelir. Bilerek yeşil yok.
/// </summary>
public static class Palette
{
    public static readonly Color Accent = Color.Parse("#E86F3A");
    public static readonly Color Muted = Color.Parse("#93A0B8");
    public static readonly Color Dim = Color.Parse("#5E6A82");
    public static readonly Color Busy = Color.Parse("#9CC3F0");
    public static readonly Color Error = Color.Parse("#EF6B85");

    public static Color For(ConnectionState state) => state switch
    {
        ConnectionState.On or ConnectionState.Warning => Accent,
        ConnectionState.Busy => Busy,
        ConnectionState.Error => Error,
        _ => Muted,
    };
}

public static class Converters
{
    public static readonly FuncValueConverter<ConnectionState, Geometry?> StateIcon = new(s =>
        Application.Current?.FindResource(s switch
        {
            ConnectionState.On or ConnectionState.Warning => "ShieldIcon",
            ConnectionState.Busy => "ShieldSyncIcon",
            ConnectionState.Error => "ShieldAlertIcon",
            _ => "ShieldOutlineIcon",
        }) as Geometry);

    public static readonly FuncValueConverter<bool, Avalonia.Layout.HorizontalAlignment> KnobAlignment = new(on =>
        on ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Left);

    public static readonly FuncValueConverter<PackStatus, string> PackStatusText = new(s => s switch
    {
        PackStatus.Testing => "Test ediliyor",
        PackStatus.Open => "Açık",
        PackStatus.Blocked => "Açılmadı",
        _ => "",
    });
}
