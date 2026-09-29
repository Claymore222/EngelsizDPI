// Arayüzü ekran olmadan render edip PNG olarak kaydeder (geliştirme aracı; motor çalıştırılmaz).
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using EngelsizDPI;
using EngelsizDPI.Core;
using EngelsizDPI.ViewModels;
using EngelsizDPI.Views;

var outDir = args.Length > 0 ? args[0] : ".";
AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

void Shot(string name, Action<MainViewModel> setup, int height = 1180, string theme = AppTheme.Dark)
{
    AppTheme.Apply(theme);
    var vm = new MainViewModel(null, new AppSettings { DisabledPacks = [], CustomDomains = ["reddit.com"] });
    setup(vm);
    var w = new MainWindow { DataContext = vm, Width = 460, Height = height };
    w.Show();
    Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using (var fs = File.Create(Path.Combine(outDir, name + ".png")))
#pragma warning disable CS0618 // Geliştirme aracı; PNG varsayılanı yeterli.
        w.CaptureRenderedFrame()!.Save(fs);
#pragma warning restore CS0618
    w.AllowClose = true;
    w.Close();
}

void Set(MainViewModel vm, ConnectionState state, string text, string detail, params PackStatus[] statuses)
{
    vm.State = state;
    vm.StatusText = text;
    vm.StatusDetail = detail;
    for (var i = 0; i < statuses.Length && i < vm.Packs.Count; i++) vm.Packs[i].Status = statuses[i];
}

Shot("on", vm => Set(vm, ConnectionState.On, "Bağlı", "Standart yöntem · 3 site açık", PackStatus.Open, PackStatus.Open, PackStatus.None, PackStatus.Open));
Shot("off", vm => Set(vm, ConnectionState.Off, "Bağlı değil", "Başlatmak için anahtarı açın"));
Shot("busy", vm => { Set(vm, ConnectionState.Busy, "Bağlanıyor…", "Siteler test ediliyor", PackStatus.Testing, PackStatus.Testing, PackStatus.None, PackStatus.Testing); vm.IsBusy = true; });
Shot("error", vm => { Set(vm, ConnectionState.Error, "Bağlanamadı", "WinDivert sürücüsü engellendi. Antivirüs WinDivert'i engelliyor olabilir. (hata kodu 1275)"); vm.ConflictText = "Eski bir GoodbyeDPI servisi kurulu (GoodbyeDPI-Turkey veya DNSChanger)."; });
Shot("add", vm =>
{
    Set(vm, ConnectionState.On, "Bağlı", "Standart yöntem · 3 site açık", PackStatus.Open, PackStatus.Open, PackStatus.None, PackStatus.Open);
    vm.IsAddOpen = true;
    vm.LastAddedDomain = "reddit.com";
    vm.AddMessage = "reddit.com şu adresleri de kullanıyor ve engelli görünüyor:";
    vm.ScanResults.Add(new ScanSuggestion("redd.it"));
    vm.ScanResults.Add(new ScanSuggestion("redditstatic.com"));
    vm.Notice = "EngelsizDPI kuruldu. Artık Başlat menüsünden ve masaüstünden açabilirsiniz; indirdiğiniz dosyayı silebilirsiniz.";
}, 1400);
Shot("light-on", vm => Set(vm, ConnectionState.On, "Bağlı", "Standart yöntem · 3 site açık", PackStatus.Open, PackStatus.Open, PackStatus.None, PackStatus.Open), theme: AppTheme.Light);
Shot("light-busy", vm => { Set(vm, ConnectionState.Busy, "Bağlanıyor…", "Siteler test ediliyor", PackStatus.Testing, PackStatus.Testing, PackStatus.None, PackStatus.Testing); vm.IsBusy = true; }, theme: AppTheme.Light);
Shot("light-error", vm => { Set(vm, ConnectionState.Error, "Bağlanamadı", "WinDivert sürücüsü engellendi. (hata kodu 1275)"); vm.ConflictText = "Eski bir GoodbyeDPI servisi kurulu."; }, theme: AppTheme.Light);
Shot("light-off", vm => Set(vm, ConnectionState.Off, "Bağlı değil", "Başlatmak için anahtarı açın"), theme: AppTheme.Light);
