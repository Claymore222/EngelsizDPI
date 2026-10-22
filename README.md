# EngelsizDPI

**Discord ve Roblox'a VPN'siz erişim · GoodbyeDPI + Güvenli DNS**

Türkiye'de erişime kapatılan **Discord**, **Roblox** ve eklediğiniz diğer sitelere VPN kullanmadan, internet hızınızı düşürmeden erişmenizi sağlayan, sistem tepsisinde çalışan bir Windows uygulaması. Arka planda [GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI) motorunu ve güvenli DNS yönlendirmesini kullanır; siyah komut pencereleri ve `.cmd` dosyalarıyla uğraşmanıza gerek kalmaz.

- **Tek tık:** Büyük düğmeye basın; uygulama bağlanır ve siteleri kendisi test eder.
- **Otomatik bul:** Varsayılan yöntem sizin operatörünüzde çalışmazsa ilk bağlantıda bütün yöntemleri kendiliğinden sırayla dener ve çalışanı seçer.
- **Kendini kontrol eder:** Bağlıyken siteleri arada bir, ağ değişince ve uykudan uyanınca yeniden test eder; kapanan olursa kendiliğinden yeniden bağlanır.
- **Sadece seçtiğiniz siteler:** Engel aşma yalnızca listedeki sitelere uygulanır. Oyunlar ve diğer uygulamalar etkilenmez.
- **Güvenli DNS:** DNS sorguları operatörün araya giremediği Yandex DNS'e (port 1253) yönlendirilir. Windows DNS ayarlarınıza dokunulmaz.
- **Sistemi kirletmez:** Kalıcı bir servis kurulmaz. Uygulamayı kapattığınızda her şey eski hâline döner.
- **Tepside çalışır:** Pencereyi kapatınca arka planda çalışmaya devam eder. İsterseniz Windows açılışında otomatik başlar.

## İndirme ve kurulum

1. [Releases](../../releases/latest) sayfasından `EngelsizDPI.exe` dosyasını indirin.
2. Çift tıklayın. Windows "bilgisayarınızı korudu" uyarısı gösterirse **Ek bilgi → Yine de çalıştır** deyin (uygulama imzasız olduğu için yalnızca ilk seferde çıkar). Yönetici izni sorulduğunda **Evet** deyin. Bu izin yalnızca ilk kurulumda bir kez sorulur; sonraki açılışlarda ve Windows açılışında uygulama izin istemeden başlar.
3. Uygulama kendini `C:\Program Files\EngelsizDPI` klasörüne kurar, Başlat menüsüne ve masaüstüne kısayol ekler ve açılır. İndirdiğiniz dosyayı artık silebilirsiniz.
4. Ortadaki düğmeye basın.

**Kaldırmak için:** Windows Ayarlar → Uygulamalar → Yüklü uygulamalar → EngelsizDPI → Kaldır.

> **Antivirüs uyarısı:** WinDivert sürücüsü ağ paketlerini değiştirdiği için bazı antivirüsler (özellikle Kaspersky) yanlış alarm verebilir. Böyle bir durumda `C:\ProgramData\EngelsizDPI` ve `C:\Program Files\EngelsizDPI` klasörlerini antivirüs istisnalarına ekleyin.

## Siteler

Uygulamadaki hazır site listesi bu depodaki [`packs/packs.json`](packs/packs.json) dosyasından gelir ve uygulama güncellenmeden, birkaç saat içinde herkese ulaşır. GitHub'a erişilemezse jsDelivr üzerinden, o da olmazsa uygulamanın içindeki yedek listeden okunur.

**Hazır paketler:** Site listesinin altındaki *Hazır paketler* düğmesi bütün paketleri arama kutusuyla birlikte açar; istediğinizi açın, *Tamam* deyin. Ana listede yalnızca seçtiğiniz siteler görünür.

**Kendi siteniz için:** *Kendi siteni ekle*'ye basıp alan adını yazın. Bağlıyken EngelsizDPI o sitenin kullandığı diğer adresleri (resim ve dosya sunucuları) tarar, engelli görünenleri size önerir.

**Herkes için önermek:** Siteyi ekledikten sonra *Herkese öner*'e basın. GitHub'da içi doldurulmuş bir [öneri formu](../../issues/new?template=site-onerisi.yml) açılır. Onaylanan siteler `packs.json`'a eklenir.

`packs.json` biçimi:

```json
{ "id": "discord", "name": "Discord", "category": "Sohbet", "default": true,
  "test": "https://discord.com/api/v9/gateway",
  "domains": ["discord.com", "discord.gg", "discordapp.com"] }
```

Listeyi değiştirdiğinizde en üstteki `revision` sayısını bir artırın; uygulamalar yalnızca daha yeni revizyonu indirir. Alan adları alt alan adlarını da kapsar (`discord.com` → `cdn.discord.com`).

## Güncellemeler

EngelsizDPI kendini günceller, yeni exe indirmeniz gerekmez. Uygulama açıldıktan kısa süre sonra ve ardından 6 saatte bir GitHub Releases'e bakar; pencerenin sağ üstündeki durum yazısına tıklayarak elle de denetleyebilirsiniz. İndirilen her güncelleme SHA-256 ile doğrulanır.

- **Elle denetlediğinizde** hiçbir şey indirilmez; "vX mevcut" bandı çıkar. *Şimdi yükle* derseniz kurulur, *Sonra* derseniz istediğiniz zaman tepsi menüsünden kurabilirsiniz.
- **Arka planda** yeni sürüm bulunursa ve *Otomatik güncelle* açıksa sessizce indirilir ve bir sonraki açılışta kurulur (ya da *Şimdi kur* ile hemen). Kapalıysa yalnızca haber verilir.

Uygulama siz bir şeye basmadan çalışırken asla yeniden başlamaz.

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

**Bir site açılmıyor ya da bir sorun var.**
Uygulamanın altındaki **Sorun bildir** düğmesine basın. GitHub'da uygulamanın durumuyla doldurulmuş bir form açılır; ne olduğunu yazıp gönderin. Eklediğiniz sitelerin adları forma yazılmaz.

**Discord tarayıcıda açılıyor ama uygulaması açılmıyor.**
"Otomatik bul"u deneyin. Olmazsa Discord'u tamamen kapatıp (tepsiden de) yeniden açın.

**"GoodbyeDPI hemen kapandı" hatası.**
Genellikle bir antivirüs WinDivert'i engelliyor ya da başka bir DPI aracı çalışıyor demektir. Yukarıdaki antivirüs notuna bakın.

**Ayarlar nerede saklanıyor?**
`%AppData%\EngelsizDPI\settings.json`. Motor dosyaları `C:\Program Files\EngelsizDPI\engine`, günlük `C:\ProgramData\EngelsizDPI\logs` klasöründe.

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

Ayrıntılı mimari için [docs/MIMARI.md](docs/MIMARI.md), olası sorunlar ve çözümleri için [docs/SORUN-GIDERME.md](docs/SORUN-GIDERME.md) dosyasına bakın.

Motor, `IBypassEngine` arayüzünün arkasındadır. macOS desteği [byedpi](https://github.com/hufrea/byedpi) tabanlı ikinci bir motorla eklenebilir. Arayüz (Avalonia) zaten macOS'ta çalışıyor.

## Lisans ve teşekkür

Apache-2.0. [GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI) (ValdikSS), [GoodbyeDPI-Turkey](https://github.com/cagritaskn/GoodbyeDPI-Turkey) (cagritaskn) ve [WinDivert](https://github.com/basil00/WinDivert) (basil00) projelerine dayanır. Ayrıntılar için [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) dosyasına bakın.

> Bu yazılım bilgiye erişim özgürlüğü amacıyla geliştirilmiştir. Kullanımından doğan sorumluluk kullanıcıya aittir.
