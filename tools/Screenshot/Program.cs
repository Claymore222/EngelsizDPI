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

void Shot(string name, Action<MainViewModel> setup)
{
    var vm = new MainViewModel(null, new AppSettings { CustomDomains = ["reddit.com"] });
    setup(vm);
    var w = new MainWindow { DataContext = vm, Width = 400, Height = 900 };
    w.Show();
    Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using (var fs = File.Create(Path.Combine(outDir, name + ".png"))) w.CaptureRenderedFrame()!.Save(fs);
    w.AllowClose = true; w.Close();
}

Shot("off", vm => { vm.State = ConnectionState.Off; vm.StatusText = "Bağlı değil"; vm.StatusDetail = "Başlamak için düğmeye dokunun"; });
Shot("on", vm => { vm.State = ConnectionState.On; vm.StatusText = "Aktif"; vm.StatusDetail = "Standart · tüm siteler erişilebilir"; foreach (var p in vm.Packs) p.Reachable = true; });
Shot("warn", vm => { vm.State = ConnectionState.Warning; vm.StatusText = "Kısmen aktif"; vm.StatusDetail = "Roblox açılmadı. \"Otomatik bul\"u deneyin."; vm.Packs[0].Reachable = true; vm.Packs[1].Reachable = false; vm.ConflictText = "Eski bir GoodbyeDPI servisi kurulu (GoodbyeDPI-Turkey veya DNSChanger)."; });
Shot("update", vm => { vm.State = ConnectionState.On; vm.StatusText = "Aktif"; vm.StatusDetail = "Standart · tüm siteler erişilebilir"; vm.Update.IsAvailable = true; vm.Update.IsDownloading = true; vm.Update.Progress = 45; vm.Update.BannerText = "Yeni sürüm hazır: v1.1.0"; });
Shot("installed", vm => { vm.State = ConnectionState.Off; vm.StatusText = "Bağlı değil"; vm.StatusDetail = "Başlamak için düğmeye dokunun"; vm.Notice = "EngelsizDPI kuruldu. Artık Başlat menüsünden ve masaüstünden açabilirsiniz; indirdiğiniz dosyayı silebilirsiniz."; });
