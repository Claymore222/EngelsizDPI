# EngelsizDPI

**Discord ve Roblox'a VPN'siz erişim · GoodbyeDPI + Güvenli DNS**

Türkiye'de erişime kapatılan **Discord**, **Roblox** ve eklediğiniz diğer sitelere VPN kullanmadan, internet hızınızı düşürmeden erişmenizi sağlayan, sistem tepsisinde çalışan bir Windows uygulaması. Arka planda [GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI) motorunu ve güvenli DNS yönlendirmesini kullanır; siyah komut pencereleri ve `.cmd` dosyalarıyla uğraşmanıza gerek kalmaz.

- **Tek tık:** Büyük düğmeye basın; uygulama bağlanır ve siteleri kendisi test eder.
- **Otomatik bul:** Bir yöntem sizin operatörünüzde çalışmazsa bütün yöntemleri sırayla dener ve çalışanı seçer.
- **Sadece seçtiğiniz siteler:** Engel aşma yalnızca listedeki sitelere uygulanır. Oyunlar ve diğer uygulamalar etkilenmez.
- **Güvenli DNS:** DNS sorguları operatörün araya giremediği Yandex DNS'e (port 1253) yönlendirilir. Windows DNS ayarlarınıza dokunulmaz.
- **Sistemi kirletmez:** Kalıcı bir servis kurulmaz. Uygulamayı kapattığınızda her şey eski hâline döner.
- **Tepside çalışır:** Pencereyi kapatınca arka planda çalışmaya devam eder. İsterseniz Windows açılışında otomatik başlar.

## İndirme

1. [Releases](../../releases) sayfasından en son `EngelsizDPI.exe` dosyasını indirin.
2. Çift tıklayıp açın. Yönetici izni sorulduğunda **Evet** deyin.
3. Ortadaki düğmeye basın.

Kurulum gerekmez, tek bir exe dosyasıdır.

> **Antivirüs uyarısı:** WinDivert sürücüsü ağ paketlerini değiştirdiği için bazı antivirüsler (özellikle Kaspersky) yanlış alarm verebilir. Böyle bir durumda `C:\ProgramData\EngelsizDPI` klasörünü antivirüs istisnalarına ekleyin.

## Güncellemeler

EngelsizDPI kendini günceller, yeni exe indirmeniz gerekmez. Uygulama açıldıktan kısa süre sonra ve ardından 6 saatte bir GitHub Releases'e bakar. Yeni sürüm varsa arka planda indirir ve SHA-256 ile doğrular. Kurulum iki durumda yapılır:

- Bir sonraki açılışta, siz fark etmeden, ya da
- Penceredeki **Yeniden başlat** düğmesine bastığınızda (bağlantı birkaç saniye kesilir ve kendiliğinden geri gelir).

Otomatik yüklemeyi Ayarlar'dan kapatabilirsiniz. Kapalıyken yeni sürüm yalnızca bildirilir.

## Nasıl çalışır?

Operatörler engelli siteleri, bağlantının başındaki TLS ClientHello paketinde görünen site adına (SNI) bakarak tespit eder (DPI, derin paket incelemesi). EngelsizDPI, bu işi [GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI) motoruyla yapar: paketleri böler ve DPI sistemini yanıltan sahte paketler gönderir. Böylece site adı fark edilmez. Trafik başka bir sunucudan geçmediği için hız kaybı olmaz.

**Güvenli DNS yönlendirmesi** açıkken DNS sorguları Yandex DNS'in 1253 portuna yönlendirilir. Bazı operatörler 53. porttaki DNS trafiğine araya girerek kendileri cevap verdiği için, sadece DNS'i 1.1.1.1 yapmak her zaman yeterli olmaz.

### Yöntemler

| Yöntem | GoodbyeDPI parametreleri | Ne zaman |
|---|---|---|
| Standart | `-5 --set-ttl 5` | Çoğu operatör |
| Alternatif 1 | `--set-ttl 3` | Superonline |
| Alternatif 2 | `-5` | Bazı siteler yavaş açılıyorsa |
| Alternatif 3 | `-9` | Superonline fiber |
| Alternatif 4 | `-7` | Diğerleri çalışmazsa |

Hangisinin çalışacağını bilmiyorsanız **Otomatik bul** düğmesini kullanın.

## Eski GoodbyeDPI / DNSChanger kullanıcıları

Bilgisayarınızda GoodbyeDPI-Turkey servisi ya da DNSChanger kuruluysa EngelsizDPI bunu fark eder ve bir uyarı gösterir. **Düzelt** düğmesi eski servisi ve başlangıç görevini kaldırır. İki araç aynı anda çalışırsa birbirini bozar.

## Sık sorulanlar

**Discord tarayıcıda açılıyor ama uygulaması açılmıyor.**
"Otomatik bul"u deneyin. Olmazsa Discord'u tamamen kapatıp (tepsiden de) yeniden açın.

**"GoodbyeDPI hemen kapandı" hatası.**
Genellikle bir antivirüs WinDivert'i engelliyor ya da başka bir DPI aracı çalışıyor demektir. Yukarıdaki antivirüs notuna bakın.

**Ayarlar nerede saklanıyor?**
`%AppData%\EngelsizDPI\settings.json`. Motor dosyaları ise `C:\ProgramData\EngelsizDPI\engine` klasöründe.

## Geliştirme

.NET 10 SDK gerekir.

```bash
dotnet build src/EngelsizDPI
dotnet run --project src/EngelsizDPI          # yönetici izni ister
dotnet publish src/EngelsizDPI -c Release -r win-x64 -o publish   # tek dosya exe
dotnet test --project tests/EngelsizDPI.Tests
```

Güncelleme denetimi yalnızca GitHub Actions'ta derlenen sürümlerde açıktır (depo adı orada otomatik yazılır). Yerelde denemek isterseniz `-p:GitHubRepo=kullanici/depo` parametresini verin.

Arayüzün ekran görüntüleri yönetici izni olmadan alınabilir:

```bash
dotnet run --project tools/Screenshot -- ./screenshots
```

Yeni bir sürüm yayınlamak için bir etiket göndermeniz yeterli. GitHub Actions testleri çalıştırır, exe'yi ve `EngelsizDPI.exe.sha256` dosyasını derleyip Releases sayfasına yükler. Kullanıcılardaki uygulamalar yeni sürümü birkaç saat içinde kendiliğinden alır:

```bash
git tag v1.0.0
git push origin v1.0.0
```

### Proje yapısı

```
src/EngelsizDPI/
  Core/          Motor (GoodbyeDPI), bağlantı testi, ayarlar, başlangıç görevi
  ViewModels/    Arayüz mantığı
  Views/         Avalonia arayüzü
  Engine/win-x64 exe'ye gömülen GoodbyeDPI + WinDivert dosyaları
```

Motor, `IBypassEngine` arayüzünün arkasındadır. macOS desteği [byedpi](https://github.com/hufrea/byedpi) tabanlı ikinci bir motorla eklenebilir. Arayüz (Avalonia) zaten macOS'ta çalışıyor.

## Lisans ve teşekkür

Apache-2.0. [GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI) (ValdikSS), [GoodbyeDPI-Turkey](https://github.com/cagritaskn/GoodbyeDPI-Turkey) (cagritaskn) ve [WinDivert](https://github.com/basil00/WinDivert) (basil00) projelerine dayanır. Ayrıntılar için [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) dosyasına bakın.

> Bu yazılım bilgiye erişim özgürlüğü amacıyla geliştirilmiştir. Kullanımından doğan sorumluluk kullanıcıya aittir.
