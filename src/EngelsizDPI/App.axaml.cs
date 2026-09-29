using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EngelsizDPI.Core;
using EngelsizDPI.ViewModels;
using EngelsizDPI.Views;

namespace EngelsizDPI;

public partial class App : Application
{
    private IBypassEngine? _engine;
    private MainViewModel? _vm;
    private MainWindow? _window;
    private TrayIcon? _tray;
    private NativeMenuItem? _toggleItem;
    private NativeMenuItem? _updateItem;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private bool _exiting;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try { _engine = EngineFactory.Create(); }
            catch (PlatformNotSupportedException) { _engine = null; }

            _vm = new MainViewModel(_engine, AppSettings.Load());
            _vm.PropertyChanged += OnViewModelChanged;
            _window = new MainWindow { DataContext = _vm };

            CreateTrayIcon();
            if (!Program.StartInTray) _window.Show();

            Program.ShowRequested += () => Dispatcher.UIThread.Post(ShowWindow);
            Program.ExitRequested += () => Dispatcher.UIThread.Post(() => _ = ExitAsync());
            if (Program.JustInstalled)
                _vm.Notice = "EngelsizDPI kuruldu. Artık Başlat menüsünden ve masaüstünden açabilirsiniz; indirdiğiniz dosyayı silebilirsiniz.";
            desktop.Exit += (_, _) => _engine?.Dispose();

            _vm.Update.PropertyChanged += OnUpdateChanged;
            _vm.Update.RestartRequested += path => _ = RestartForUpdateAsync(path);
            _vm.Update.StartBackgroundChecks();

            _ = _vm.StartupAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTrayIcon()
    {
        _toggleItem = new NativeMenuItem(_vm!.ToggleLabel);
        _toggleItem.Click += (_, _) => _vm.ToggleCommand.Execute(null);

        var showItem = new NativeMenuItem("Göster");
        showItem.Click += (_, _) => ShowWindow();

        _updateItem = new NativeMenuItem("Güncelle") { IsVisible = false };
        _updateItem.Click += (_, _) => _vm.Update.UpdateNowCommand.Execute(null);

        var exitItem = new NativeMenuItem("Çıkış");
        exitItem.Click += async (_, _) => await ExitAsync();

        _tray = new TrayIcon
        {
            ToolTipText = "EngelsizDPI — " + _vm.StatusText,
            Icon = CreateStatusIcon(_vm.State),
            Menu = [_toggleItem, showItem, _updateItem, new NativeMenuItemSeparator(), exitItem],
            IsVisible = true,
        };
        _tray.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(this, [_tray]);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_tray is null || _vm is null) return;
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.State):
                _tray.Icon = CreateStatusIcon(_vm.State);
                break;
            case nameof(MainViewModel.StatusText):
                _tray.ToolTipText = "EngelsizDPI — " + _vm.StatusText;
                break;
            case nameof(MainViewModel.ToggleLabel):
                _toggleItem!.Header = _vm.ToggleLabel;
                break;
        }
    }

    private void OnUpdateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_updateItem is null || _vm is null) return;
        _updateItem.IsVisible = _vm.Update.IsAvailable;
        _updateItem.Header = $"{_vm.Update.BannerText} — {_vm.Update.ActionLabel}";
    }

    /// <summary>Motoru durdurur, exe'yi yeni sürümle değiştirir ve yeni sürümü aynı durumda başlatır.</summary>
    private async Task RestartForUpdateAsync(string newExe)
    {
        if (_vm is null || _exiting) return;

        var args = new List<string>();
        if (_vm.IsConnected) args.Add("--connect");
        if (_window is not { IsVisible: true }) args.Add("--tray");

        _exiting = true;
        await _vm.DisconnectAsync();
        try
        {
            UpdateService.ApplyAndRestart(newExe, args);
        }
        catch (Exception e)
        {
            _exiting = false;
            _vm.Update.ReportApplyFailure(e);
            if (args.Contains("--connect")) await _vm.ConnectAsync();
            return;
        }

        if (_window is not null)
        {
            _window.AllowClose = true;
            _window.Close();
        }
        if (_tray is not null) _tray.IsVisible = false;
        _desktop?.Shutdown();
    }

    private void ShowWindow()
    {
        if (_window is null) return;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        if (_vm is not null) await _vm.DisconnectAsync();
        if (_window is not null)
        {
            _window.AllowClose = true;
            _window.Close();
        }
        if (_tray is not null) _tray.IsVisible = false;
        _desktop?.Shutdown();
    }

    /// <summary>Tepsi ikonu: durum rengine boyanmış kalkan.</summary>
    private static WindowIcon CreateStatusIcon(ConnectionState state)
    {
        const int size = 64;
        var bitmap = new RenderTargetBitmap(new PixelSize(size, size));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            var shield = (Geometry)Current!.FindResource("ShieldIcon")!;
            using (ctx.PushTransform(Matrix.CreateScale(size / 24.0, size / 24.0)))
            {
                ctx.DrawGeometry(new SolidColorBrush(Palette.For(state) == Palette.Off ? Color.Parse("#9CA3AF") : Palette.For(state)), null, shield);
            }
        }

        return new WindowIcon(bitmap);
    }
}
