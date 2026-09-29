using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EngelsizDPI.Core;

/// <summary>
/// WinDivert sürücüsüne hiçbir paket yakalamayan ("false" filtreli) bir handle tutar.
///
/// Neden: WinDivert, son handle kapandığında sürücüyü kaldırır ve bu birkaç saniye sürer. O arada yeniden
/// açılmaya çalışılırsa hata 1058 (servis devre dışı) ya da 1072 (silinmek üzere) alınır; GoodbyeDPI bu
/// durumda 20 saniye bekleyip kapanır. Otomatik bul ve ayar değişikliklerinde GoodbyeDPI art arda yeniden
/// başlatıldığı için bu boşluğa düşüyordu. Biz handle'ı tuttuğumuz sürece sürücü yüklü kalır.
///
/// Ayrıca sürücü hiç açılamıyorsa gerçek Windows hata kodunu anında öğrenmemizi sağlar (GoodbyeDPI'ın
/// hata çıktısı tamponlandığı için ancak 20 saniye sonra görünür).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WinDivertKeepAlive : IDisposable
{
    private const int LayerNetwork = 0;
    private const ulong FlagSniff = 0x1, FlagRecvOnly = 0x4;

    /// <summary>Sürücü kaldırılırken alınan, kısa süre sonra kendiliğinden düzelen hatalar.</summary>
    private static readonly int[] TransientErrors = [1058, 1060, 1072];

    private IntPtr _lib;
    private IntPtr _handle;
    private unsafe delegate* unmanaged<IntPtr, int> _close;

    public bool IsOpen => _handle != IntPtr.Zero;

    /// <summary>
    /// Handle zaten açıksa hemen döner. Değilse sürücüyü açmayı dener; geçici hatalarda <paramref name="timeout"/>
    /// boyunca yeniden dener. Başarıda null, başarısızlıkta Windows hata kodunu döner.
    /// </summary>
    public async Task<int?> EnsureOpenAsync(string engineDir, TimeSpan timeout, CancellationToken ct = default)
    {
        if (IsOpen) return null;

        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var error = TryOpen(engineDir);
            if (error is null) return null;
            if (!TransientErrors.Contains(error.Value) || DateTime.UtcNow >= deadline) return error;
            Log.Write($"WinDivert henüz hazır değil (hata {error}), yeniden deneniyor");
            await Task.Delay(500, ct);
        }
    }

    private unsafe int? TryOpen(string engineDir)
    {
        if (_lib == IntPtr.Zero)
        {
            _lib = NativeLibrary.Load(Path.Combine(engineDir, "WinDivert.dll"));
            _close = (delegate* unmanaged<IntPtr, int>)NativeLibrary.GetExport(_lib, "WinDivertClose");
        }
        var open = (delegate* unmanaged<byte*, int, short, ulong, IntPtr>)NativeLibrary.GetExport(_lib, "WinDivertOpen");

        IntPtr handle;
        fixed (byte* filter = "false\0"u8)
        {
            Marshal.SetLastSystemError(0);
            handle = open(filter, LayerNetwork, 0, FlagSniff | FlagRecvOnly);
        }
        var error = Marshal.GetLastSystemError();

        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return error == 0 ? -1 : error;
        _handle = handle;
        return null;
    }

    public unsafe void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            _close(_handle);
            _handle = IntPtr.Zero;
        }
        if (_lib != IntPtr.Zero)
        {
            NativeLibrary.Free(_lib);
            _lib = IntPtr.Zero;
        }
    }

    /// <summary>WinDivert hata kodları için kullanıcıya gösterilecek açıklama.</summary>
    public static string Describe(int code) => code switch
    {
        2 or 3 => "WinDivert sürücü dosyası bulunamadı. Antivirüs programınız silmiş olabilir; " +
                  $"\"{AppPaths.EngineDir}\" klasörünü antivirüs istisnalarına ekleyin.",
        5 => "Yönetici izni yok. EngelsizDPI'ı yönetici olarak çalıştırın.",
        577 => "Windows sürücü imzasını doğrulayamadı. Bir antivirüs (ör. Kaspersky) WinDivert'i engelliyor olabilir.",
        654 => "Önceki WinDivert sürücüsü tam kaldırılamadı. Bilgisayarı yeniden başlatıp tekrar deneyin.",
        1058 or 1060 or 1072 => "Önceki WinDivert sürücüsü hâlâ kaldırılıyor. Birkaç saniye sonra tekrar deneyin; " +
                                "düzelmezse bilgisayarı yeniden başlatın.",
        1275 => "WinDivert sürücüsü engellendi. Başka sürümde bir WinDivert yüklü olabilir (bilgisayarı yeniden " +
                "başlatın) ya da antivirüs / Windows 'Bellek bütünlüğü' ayarı sürücüyü engelliyor olabilir.",
        1753 => "Windows 'Temel Filtreleme Altyapısı' (BFE) servisi çalışmıyor.",
        _ => $"WinDivert sürücüsü açılamadı: {new Win32Exception(code).Message}",
    };
}
