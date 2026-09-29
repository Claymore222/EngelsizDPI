using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using EngelsizDPI.ViewModels;

namespace EngelsizDPI.Views;

/// <summary>
/// Durum renkleri. Bilerek yeşil yok: bağlı = kiremit (vurgu rengi), bağlı değil = soluk gri-mavi,
/// bağlanıyor = açık mavi, hata = pembe-kırmızı (kiremitten ayırt edilsin diye).
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

    public static (Color Background, Color Border) Card(ConnectionState state) => state switch
    {
        ConnectionState.On or ConnectionState.Warning => (Color.Parse("#1F2033"), Color.Parse("#673320")),
        ConnectionState.Busy => (Color.Parse("#172032"), Color.Parse("#2E4668")),
        ConnectionState.Error => (Color.Parse("#231C2A"), Color.Parse("#5C2A3A")),
        _ => (Color.Parse("#172032"), Color.Parse("#26324A")),
    };
}

public static class Converters
{
    private static IBrush Brush(Color c) => new SolidColorBrush(c);

    public static readonly FuncValueConverter<ConnectionState, IBrush> StateBrush = new(s => Brush(Palette.For(s)));
    public static readonly FuncValueConverter<ConnectionState, IBrush> StateCardBackground = new(s => Brush(Palette.Card(s).Background));
    public static readonly FuncValueConverter<ConnectionState, IBrush> StateCardBorder = new(s => Brush(Palette.Card(s).Border));

    public static readonly FuncValueConverter<ConnectionState, Geometry?> StateIcon = new(s =>
        Application.Current?.FindResource(s switch
        {
            ConnectionState.On or ConnectionState.Warning => "ShieldIcon",
            ConnectionState.Busy => "ShieldSyncIcon",
            ConnectionState.Error => "ShieldAlertIcon",
            _ => "ShieldOutlineIcon",
        }) as Geometry);

    /// <summary>Büyük anahtarın zemini: bağlıyken kiremit, bağlanırken mavi, kapalıyken boş.</summary>
    public static readonly FuncValueConverter<ConnectionState, IBrush> SwitchFill = new(s => s switch
    {
        ConnectionState.On or ConnectionState.Warning => Brush(Palette.Accent),
        ConnectionState.Busy => Brush(Color.Parse("#2E4668")),
        _ => Brushes.Transparent,
    });

    public static readonly FuncValueConverter<ConnectionState, IBrush> SwitchKnob = new(s => s switch
    {
        ConnectionState.On or ConnectionState.Warning => Brush(Color.Parse("#2A0E04")),
        ConnectionState.Busy => Brush(Palette.Busy),
        _ => Brush(Palette.Muted),
    });

    public static readonly FuncValueConverter<ConnectionState, IBrush> SwitchStroke = new(s =>
        s is ConnectionState.Off or ConnectionState.Error ? Brush(Palette.Muted) : Brushes.Transparent);

    public static readonly FuncValueConverter<bool, Avalonia.Layout.HorizontalAlignment> KnobAlignment = new(on =>
        on ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Left);

    public static readonly FuncValueConverter<PackStatus, string> PackStatusText = new(s => s switch
    {
        PackStatus.Testing => "Test ediliyor",
        PackStatus.Open => "Açık",
        PackStatus.Blocked => "Açılmadı",
        _ => "",
    });

    public static readonly FuncValueConverter<PackStatus, IBrush> PackStatusBrush = new(s => s switch
    {
        PackStatus.Testing => Brush(Palette.Busy),
        PackStatus.Open => Brush(Palette.Accent),
        PackStatus.Blocked => Brush(Palette.Error),
        _ => Brushes.Transparent,
    });

    public static readonly FuncValueConverter<bool, IBrush> EnabledNameBrush = new(on =>
        Brush(on ? Color.Parse("#E8ECF3") : Palette.Muted));
}
