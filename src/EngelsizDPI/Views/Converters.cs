using Avalonia.Data.Converters;
using Avalonia.Media;
using EngelsizDPI.ViewModels;

namespace EngelsizDPI.Views;

public static class Palette
{
    public static readonly Color Off = Color.Parse("#3A3F4B");
    public static readonly Color Busy = Color.Parse("#F59E0B");
    public static readonly Color On = Color.Parse("#22C55E");
    public static readonly Color Warning = Color.Parse("#EAB308");
    public static readonly Color Error = Color.Parse("#EF4444");

    public static Color For(ConnectionState state) => state switch
    {
        ConnectionState.Busy => Busy,
        ConnectionState.On => On,
        ConnectionState.Warning => Warning,
        ConnectionState.Error => Error,
        _ => Off,
    };
}

public static class Converters
{
    public static readonly FuncValueConverter<ConnectionState, IBrush> StateBrush =
        new(s => new SolidColorBrush(Palette.For(s)));

    /// <summary>Güç düğmesinin arkasındaki renkli parıltı.</summary>
    public static readonly FuncValueConverter<ConnectionState, BoxShadows> StateGlow =
        new(s => s == ConnectionState.Off
            ? new BoxShadows(new BoxShadow { Blur = 24, Color = Color.FromArgb(90, 0, 0, 0) })
            : new BoxShadows(new BoxShadow { Blur = 56, Spread = 2, Color = Color.FromArgb(110, Palette.For(s).R, Palette.For(s).G, Palette.For(s).B) }));

    public static readonly FuncValueConverter<bool?, IBrush> ReachableBrush =
        new(r => r switch
        {
            true => new SolidColorBrush(Palette.On),
            false => new SolidColorBrush(Palette.Error),
            _ => Brushes.Transparent,
        });

    public static readonly FuncValueConverter<bool?, string> ReachableText =
        new(r => r switch { true => "Erişilebilir", false => "Açılmadı", _ => "" });
}
