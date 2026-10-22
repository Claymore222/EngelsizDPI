# EngelsizDPI — Mimari ve Çalışma Mantığı

Bu belge, projeyi ilk kez açan bir geliştiricinin (insan ya da yapay zekâ) kodu okumadan önce bilmesi
gerekenleri anlatır: uygulama neyi, nasıl ve neden öyle yapıyor. Olası sorunlar ve çözümleri için
[SORUN-GIDERME.md](SORUN-GIDERME.md), çalışma kuralları için kökteki [AGENTS.md](../AGENTS.md) dosyasına bakın.

---

## 1. Ne yapıyor?

Türkiye'de erişime kapatılan sitelere (varsayılan olarak Discord ve Roblox) VPN kullanmadan erişim sağlayan,
sistem tepsisinde çalışan bir Windows uygulaması. Arka planda [GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI)
çalıştırır, DNS sorgularını güvenli bir sunucuya yönlendirir, siteleri kendisi test eder ve kendini kurar,
günceller, Windows açılışında başlatır.

Temel ilkeler:

- **Sistemi kirletmemek.** Kalıcı Windows servisi kurulmaz, Windows DNS ayarlarına dokunulmaz. GoodbyeDPI
  uygulamanın alt sürecidir; uygulama kapanınca (çökse bile) her şey eski hâline döner.
- **Yalnızca seçilen siteler.** Engel aşma, listedeki alan adlarına uygulanır (`--blacklist`). Oyunlar ve diğer
  trafik etkilenmez.
- **Kullanıcıya iş bırakmamak.** Tek exe indirilir; kendini kurar, UAC'yi yalnızca ilk seferde sorar, kendini
  günceller.

---

## 2. Engel nasıl çalışıyor, nasıl aşılıyor?

### Engelleme

Türk operatörleri engelli siteleri **SNI tabanlı DPI** (derin paket incelemesi) ile tespit eder. HTTPS
bağlantısının ilk paketi olan TLS ClientHello'da site adı (SNI) açık metin olarak gider. DPI cihazı bu adı
görür ve bağlantıya sahte bir TCP RST gönderir. Geliştirici hattında ölçüldüğü üzere RST yaklaşık **20 ms**
içinde gelir. `curl` ile `Recv failure: Connection was reset` (çıkış kodu 35) olarak görünür.

Bazı operatörler ayrıca **DNS'i zehirler**: 53. porttaki DNS trafiğini araya girip yanıtlar ve engelli alan
adı için yanlış IP döner. Bu yüzden yalnızca Windows'ta DNS'i 1.1.1.1 yapmak her zaman yetmez.

**IP engeli** ayrı bir şeydir: sunucunun IP adresine giden trafik tamamen düşürülür. `curl`'de zaman aşımı
(çıkış kodu 28) olarak görünür. DPI atlatma bunu **açamaz**; yalnızca VPN ya da proxy açar.

### Atlatma

GoodbyeDPI, [WinDivert](https://github.com/basil00/WinDivert) çekirdek sürücüsüyle giden paketleri yakalar ve:

- ClientHello'yu parçalara böler; SNI tek pakette görünmez.
- DPI cihazına ulaşan ama sunucuya ulaşmayan **sahte paketler** gönderir: düşük TTL (`--set-ttl`), yanlış
  sağlama toplamı (`--wrong-chksum`) ya da yanlış sıra numarası (`--wrong-seq`). DPI cihazı sahte paketi
  gerçek sanar ve oturumu yanlış izler.
- `--dns-addr/--dns-port` ile 53. porttaki UDP DNS sorgularını **Yandex DNS'in 1253 portuna**
  (`77.88.8.8`, IPv6 `2a02:6b8::feed:0ff`) yönlendirir. Operatör standart dışı portu ele geçirmez.

Trafik başka bir sunucudan geçmez, bu yüzden hız kaybı olmaz.

### Yöntemler (profiller)

`Core/Profiles.cs`. Argümanlar [GoodbyeDPI-Turkey](https://github.com/cagritaskn/GoodbyeDPI-Turkey)'in test
edilmiş `.cmd` dosyalarından alınmıştır. Site listesi ve DNS argümanlarını motor ayrıca ekler.

| Id | Ad | Argümanlar | Not |
|---|---|---|---|
| `standard` | Standart | `-5 --set-ttl 5` | Varsayılan. Türk Telekom, Vodafone, Türknet |
| `ttl3` | Alternatif 1 | `--set-ttl 3` | Superonline |
| `mode5` | Alternatif 2 | `-5` | Otomatik TTL |
| `mode9` | Alternatif 3 | `-9` | `-q` içerir: QUIC/HTTP3'ü **tüm siteler için** engeller |
| `mode7` | Alternatif 4 | `-7` | Yalnızca yanlış sağlama toplamı |

GoodbyeDPI'ın hazır modları: `-5` = `-f 2 -e 2 --auto-ttl --reverse-frag --max-payload`,
`-7` = `-f 2 -e 2 --wrong-chksum --reverse-frag --max-payload`,
`-9` = `-f 2 -e 2 --wrong-seq --wrong-chksum --reverse-frag --max-payload -q`.

### Site listesi eşleşmesi

`--blacklist hosts.txt`, listedeki alan adını ve **onun alt alan adlarını** kapsar (`discord.com` →
`cdn.discord.com`). Farklı bir alan adını kapsamaz (`discord.com` listede olsa da `discordapp.net` ayrıca
yazılmalı). Kodda aynı mantık `SiteScanner.IsCovered` içinde de var.

### Kapsam dışı

GoodbyeDPI yalnızca TCP üzerindeki TLS/HTTP'yi ve DNS'i işler. Discord sesli sohbet ve Roblox oyun içi
bağlantılar UDP'dir; şu an bunlara dokunulmuyor (şimdilik operatörler bunları engellemiyor). Gerekirse çözüm,
`IBypassEngine` arkasına eklenecek bir zapret (`winws`) motorudur.

---

## 3. Teknoloji

| Parça | Seçim |
|---|---|
| Dil / çalışma zamanı | C# / .NET 10, `net10.0`, `Nullable` ve `ImplicitUsings` açık |
| Arayüz | Avalonia 12.1.3 (Fluent tema, Inter yazı tipi), derlenmiş bağlamalar varsayılan |
| MVVM | CommunityToolkit.Mvvm 8.4.2 (`[ObservableProperty]`, `[RelayCommand]`) |
| DPI motoru | GoodbyeDPI v0.2.3rc3 + WinDivert 2.2 (`src/EngelsizDPI/Engine/win-x64/`, exe'ye gömülü) |
| Test | xunit.v3 4.0.1, Microsoft Testing Platform (`global.json`) |
| Yayın | Tek dosya, self-contained `win-x64` exe (yalnızca `-r` verilince) |
| CI | GitHub Actions: `v*` etiketi → test → publish → SHA-256 → Release |

---

## 4. Depo yapısı

```
EngelsizDPI.slnx
global.json                       Test çalıştırıcısı: Microsoft.Testing.Platform
packs/packs.json                  Hazır site paketleri (uzaktan güncellenir, exe'ye de gömülür)
.github/workflows/release.yml     Sürüm yayını
.github/ISSUE_TEMPLATE/site-onerisi.yml   "Herkese öner" düğmesinin açtığı form
src/EngelsizDPI/
  Program.cs                      Giriş noktası: yetki, kurulum, tek kopya, bekleyen güncelleme
  App.axaml(.cs)                  Tema kaynakları, tepsi ikonu, uygulama yaşam döngüsü
  app.manifest                    asInvoker (UAC'yi manifest değil Elevation istiyor)
  Core/
    Elevation.cs                  Yönetici yetkisi: Launch görevi ya da UAC
    AutoStart.cs                  Zamanlanmış görevler (Launch, Autostart)
    Installer.cs                  Kendini kurma, kaldırma, kısayollar, Uninstall kaydı
    StartupHealth.cs              Bozuk sürüm tespiti: üst üste açılamayan sürümden geri dönüş
    GoodbyeDpiEngine.cs           Motor: dosyaları çıkarır, goodbyedpi.exe'yi başlatır, çakışmaları bulur
    WinDivertKeepAlive.cs         Sürücüye boş bir handle tutar (1058 hatası çözümü)
    KillOnCloseJob.cs             Job Object: uygulama ölünce goodbyedpi da ölür
    IBypassEngine.cs              Motor arayüzü + EngineFactory
    ConnectivityTester.cs         Sitenin gerçekten açılıp açılmadığını ölçer
    SecureDns.cs                  Güvenli DNS sunucusu sabitleri, sunucuyu sınama, internet var mı kontrolü
    Profiles.cs                   Yöntemler
    SitePacks.cs                  Paket kataloğu: ayrıştırma, doğrulama, uzaktan yenileme
    SiteScanner.cs                Eklenen sitenin kullandığı engelli alan adlarını bulur
    UpdateService.cs              GitHub Releases'ten kendini güncelleme
    AppSettings.cs                Ayarlar + AppPaths (tüm dosya yolları)
    AppTheme.cs, Log.cs, Shell.cs
  ViewModels/MainViewModel.cs     Bağlantı durumu, site listesi, katalog, ayarlar, otomatik bul
  ViewModels/UpdateViewModel.cs   Güncelleme bandı ve tepsi öğesi
  Views/MainWindow.axaml(.cs)     Tek pencere; kapatınca tepsiye gizlenir
  Views/Converters.cs             Tepsi renkleri (Palette) ve dönüştürücüler
tests/EngelsizDPI.Tests/          Installer, SitePack, UpdateService testleri
tools/Screenshot/                 Arayüzü ekransız render edip PNG kaydeder
```

---

## 5. Açılış akışı ve yetki modeli

GoodbyeDPI bir çekirdek sürücüsü yüklediği için **yönetici yetkisi** şarttır. Her açılışta UAC sormamak için
uygulama normal kullanıcı olarak başlar (`app.manifest`: `asInvoker`) ve yetkiyi kendisi alır.

```
Kullanıcı exe'yi açar (asInvoker, yönetici değil)
 └─ Program.Main → (--from-task argümanı ayıklanır) → Elevation.Elevate(args, fromTask)
     ├─ Argüman yok + (kurulu kopyanın kendisi ya da kurulu sürüm güncel ve bozuk değil)
     │  + "EngelsizDPI\Launch-<SID>" görevi var
     │    → schtasks /run → görev exe'yi --from-task ile yönetici olarak başlatır (UAC YOK)
     ├─ --from-task ile gelip yine yönetici değilse (hesap artık yönetici değil)
     │    → görev tekrar çalıştırılmaz (sonsuz döngü olurdu), UAC sorulur
     └─ Aksi halde → runas ile UAC sorulur (ilk kurulumda bir kez)
     (bu süreç çıkar)

Yönetici süreç → Program.Main
 1. --uninstall  → RunUninstall (çalışan kopyayı Exit olayıyla kapatır, her şeyi siler)
 2. Installer.Decide():
      RunHere          → kurulu kopyadan ya da geliştirme ortamından çalışıyoruz, devam
      LaunchInstalled  → kurulu sürüm aynı/yeni: kurulu kopyayı başlat (açıksa öne getir), çık
      InstallAndLaunch → kendini Program Files'a kopyala, kurulu kopyayı --installed ile başlat, çık
 3. Tek kopya mutex'i (--updated ile 20 sn bekler; başka kopya varsa Show olayını tetikleyip çıkar)
 4. Bekleyen güncelleme varsa (ve Otomatik güncelle açıksa) uygula, yeni sürümü başlat, çık
 5. Kurulu kopyaysa StartupHealth.BeginStartup: bu sürüm üst üste 2 kez sağlıklı açılamadıysa ve .old
    duruyorsa önceki sürüme dön, bu sürümü atla, çık (bkz. §10.1)
 6. Installer.RefreshInstalledState(): Uninstall kaydı, kısayol, Launch görevi tazelenir, eski ortak görevler
    kullanıcıya özel ada taşınır, eski klasörler silinir
 7. Show/Exit olay dinleyicileri → Avalonia başlar (App.OnFrameworkInitializationCompleted)
 8. 10 sn sorunsuz çalışınca açılış sağlıklı sayılır, ardından güncelleme denetimleri başlar
```

**Zamanlanmış görevler** (`AutoStart.cs`, XML ile oluşturulur çünkü `schtasks` varsayılanları pilde başlatmayı
engeller ve görevi 72 saat sonra sonlandırır):

| Görev | Tetikleyici | Argüman | Amaç |
|---|---|---|---|
| `EngelsizDPI\Launch-<SID>` | Yok | `--from-task` | UAC'siz yönetici başlatma; kurulumda ve her açılışta yeniden oluşturulur |
| `EngelsizDPI\Autostart-<SID>` | Oturum açılışı (beklemesiz) | `--tray` | "Windows açılınca başlat" ayarı |
| `EngelsizDPI\Launch`, `EngelsizDPI\Autostart` | — | — | v1.2.0–v1.3.x'in ortak görevleri; taşınır/silinir |
| `EngelsizDPI` (kök) | — | — | v1.1.0 ve öncesinin görevi; taşınır |

**Görevler kullanıcıya özeldir** (`<SID>` = görevi oluşturan Windows kullanıcısının SID'i). `InteractiveToken`
görevi yalnızca sahibinin oturumunda çalışır; ortak ad kullanılsaydı aynı bilgisayardaki ikinci kullanıcı
birincinin görevini ezer, kendi açılışında pencere hiç gelmezdi. Eski ortak görevler, yalnızca o kullanıcıya
aitse (görev XML'indeki `UserId` SID ya da `DOMAIN\kullanıcı`) taşınır. Kaldırma, `\EngelsizDPI\`
klasöründeki **bütün** kullanıcıların görevlerini siler.

Hepsi `RunLevel=HighestAvailable`, `LogonType=InteractiveToken`, `MultipleInstancesPolicy=Parallel`.
**Parallel önemlidir:** uygulama tepside çalışırken görev "çalışıyor" sayılır. `IgnoreNew` olsaydı ikinci açılış
hiç başlamaz, pencere öne gelmezdi.

**Komut satırı argümanları:**

| Argüman | Anlam |
|---|---|
| `--tray` | Pencereyi gösterme (oturum açılışı) |
| `--connect` | Ayardan bağımsız olarak açılışta bağlan (güncelleme öncesi bağlıysa) |
| `--updated` | Güncelleme sonrası başlatıldı; eski kopyanın kapanmasını 20 sn bekle |
| `--installed` | Kurulumdan hemen sonra; "kuruldu" bilgi bandını göster |
| `--uninstall [--quiet]` | Kaldır (Windows "Yüklü uygulamalar" bunu çağırır) |
| `--from-task` | Launch görevi başlattı (Main en başta ayıklar; yalnızca döngü koruması için) |

**Adlandırılmış nesneler:** `Local\EngelsizDPI.Instance` (mutex), `Local\EngelsizDPI.Show` (ikinci açılış
pencereyi öne getirir), `Local\EngelsizDPI.Exit` (kurulum/kaldırma çalışan kopyayı kapatır). Exit olayı mutex
alındıktan sonra `Reset()` edilir; yoksa kimsenin dinlemediği eski sinyal, yeni açılan kopyayı hemen kapatırdı.

---

## 6. Dosya yolları ve kurulum

Tüm yollar `Core/AppSettings.cs` içindeki `AppPaths`, `Installer` ve `UpdateService` sınıflarındadır.

| Ne | Yol | Kim yazabilir |
|---|---|---|
| Kurulu exe | `C:\Program Files\EngelsizDPI\EngelsizDPI.exe` | Yalnızca yönetici |
| Motor dosyaları (kuruluyken) | `C:\Program Files\EngelsizDPI\engine\` | Yalnızca yönetici |
| İndirilen güncellemeler (kuruluyken) | `C:\Program Files\EngelsizDPI\updates\` | Yalnızca yönetici |
| Motor dosyaları (kurulu değilken) | `C:\ProgramData\EngelsizDPI\engine\` | — |
| Günlük | `C:\ProgramData\EngelsizDPI\logs\engelsizdpi.log` (1 MB'da `.1`'e döner) | — |
| Ayarlar | `%AppData%\EngelsizDPI\settings.json` | Kullanıcı |
| Paket önbelleği | `%AppData%\EngelsizDPI\packs.json` | Kullanıcı |
| Açılış durumu / atlanan sürüm | `C:\Program Files\EngelsizDPI\startup-state`, `skipped-version` | Yalnızca yönetici |
| Kısayollar | Ortak Başlat menüsü ve ortak masaüstü: `EngelsizDPI.lnk` | — |
| Kaldırma kaydı | `HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\EngelsizDPI` | — |

**Güvenlik kararı:** Uygulama UAC sormadan yönetici olarak başlayabildiği için, yönetici olarak çalıştırılan
hiçbir exe/dll kullanıcının yazabildiği bir klasörde durmamalı. Aksi halde herhangi bir program oraya kendi
dosyasını koyup yönetici yetkisi kazanabilirdi. Bu yüzden motor ve güncellemeler kuruluyken Program Files
altındadır. v1.1.0 bunları `%LocalAppData%` ve `ProgramData\engine` altında tutuyordu; `RefreshInstalledState`
bu eski klasörleri her açılışta silmeyi dener. `WinDivert64.sys` yeniden başlatmaya kadar kilitli kalabildiği
için her klasör ayrı denenir.

**Kurulum** (`Installer.Install`): exe Program Files'a kopyalanır (önceki exe `.old` olarak yeniden
adlandırılır ve geri dönüş için saklanır), kısayollar `WScript.Shell` COM ile oluşturulur, Uninstall kaydı
yazılır, Launch görevi kurulur. Kurulu sürüm indirilen exe'den **yeni** olduğu hâlde kurulum yapılıyorsa (yalnızca
kurulu sürüm bozuk sayıldığında olur), o sürüm atlananlara yazılır.

**Kaldırma** (`Installer.Uninstall`): görevler, kısayollar, kayıt, `ProgramData\EngelsizDPI`,
`%AppData%\EngelsizDPI` silinir. Çalışan exe kendini silemediği için kurulum klasörünü birkaç saniye sonra silen
gizli bir `cmd` bırakılır.

---

## 7. Motor (GoodbyeDpiEngine)

`StartAsync(EngineOptions)` sırası:

1. Çalışan goodbyedpi varsa durdurulur.
2. Gömülü dosyalar (`goodbyedpi.exe`, `WinDivert.dll`, `WinDivert64.sys`) EngineDir'e çıkarılır. Yalnızca
   diskteki kopya farklıysa yazılır; kilitli `.sys` mevcut kopyayla kullanılır.
3. Etkin alan adları `hosts.txt`'ye yazılır.
4. **WinDivertKeepAlive** sürücüyü açar (aşağıya bakın). Açılamazsa goodbyedpi hiç başlatılmaz; Windows hata
   kodu `WinDivertKeepAlive.Describe` ile Türkçe açıklamaya çevrilip gösterilir.
5. `goodbyedpi.exe <profil> [--dns-addr ...] --blacklist hosts.txt` alt süreç olarak, penceresiz başlatılır ve
   Job Object'e bağlanır.
6. 1,5 sn beklenir; süreç kapandıysa çıktısındaki son hata satırıyla birlikte hata fırlatılır.

**WinDivertKeepAlive, projenin en önemli dersidir.** WinDivert, son handle kapanınca sürücüyü kaldırır ve bu
birkaç saniye sürer. O arada goodbyedpi yeniden başlatılırsa `Error opening filter: 1058` (ya da 1060, 1072)
alır. Hata alan goodbyedpi `die()` içinde **20 saniye bekler** ve bu sırada servis handle'ını tuttuğu için
sürücü "silinmek üzere" durumunda takılı kalır. Otomatik bul ve ayar değişiklikleri goodbyedpi'ı art arda
yeniden başlattığından uygulama bu kısır döngüye düşüyordu ("Açılmadı", ardından "koptu").

Çözüm: uygulama, hiçbir paket yakalamayan (`"false"` filtreli, `SNIFF | RECV_ONLY`) bir WinDivert handle'ını
uygulama ömrü boyunca açık tutar. Sürücü hep yüklü kalır. İlk açılışta geçici hatalar (1058/1060/1072) 15 sn
boyunca yeniden denenir. Yan faydası: goodbyedpi'ın çıktısı pipe'ta tamponlandığı için hata ancak süreç
kapanınca görünür, ama sürücü hatası bu sayede anında öğrenilir.

**Diğer ayrıntılar:**

- `KillOnCloseJob`: `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. Uygulama çökerse goodbyedpi arkada kalmaz.
- Çıktı okunmazsa pipe dolup süreç kilitlenir; bu yüzden stdout/stderr asenkron okunur ve `[goodbyedpi]`
  önekiyle günlüğe yazılır.
- `UnexpectedExit`: goodbyedpi uygulama durdurmadan kapanırsa arayüz "Bağlantı koptu" der.
- **Çakışmalar** (`DetectConflicts`): eski `GoodbyeDPI` servisi (GoodbyeDPI-Turkey/DNSChanger),
  `DNSChangerApp_Startup` görevi, başka bir `goodbyedpi.exe`. "Düzelt" bunları kaldırır. Sürücüyü biz
  tutuyorsak `sc stop WinDivert` **yapılmaz**, çünkü kullanımdaki sürücüyü durdurmak bağlantıyı koparır.

---

## 8. Bağlantı, test ve durumlar (MainViewModel)

`ConnectCoreAsync(profile)`:

1. `AppSettings.BuildHostList(catalog)` = etkin paketlerin alan adları + özel siteler (tekrarsız). Liste boşsa
   motor durdurulur.
2. Durum `Busy`, etkin satırlar "Test ediliyor".
3. Motor başlatılır, ardından `ConnectivityTester.TestPacksAsync` her paketin `test` adresine paralel istek atar.
   Yanıt kodu önemsizdir (403/429 da erişim demektir). Bağlantı sıfırlama, zaman aşımı ya da sertifika hatası
   "kapalı" sayılır. Her testte yeni `SocketsHttpHandler` kullanılır ki eski bağlantı yeniden kullanılmasın.
4. Hepsi açıksa `On` ("Bağlı"), bazıları kapalıysa `Warning` ("Kısmen açık · Otomatik bul'u deneyin"),
   istisna olursa `Error` ("Bağlanamadı" + açıklama).

**Durumlar:** `Off`, `Busy`, `On`, `Warning`, `Error`. Arayüz renkleri `StateOn`, `StateBusy`, `StateError`
bayraklarıyla stil sınıfları üzerinden seçilir. `IsConnected` = `On` ya da `Warning`.

**Eşzamanlılık:** Tüm motor işlemleri `_gate` (`SemaphoreSlim(1)`) ile sıraya girer.
`SaveAndReconnect()` ayarı kaydeder; bağlıysa ya da bağlanıyorsa **tek bir** yeniden bağlanmayı sıraya alır
(`_reconnectQueued`). Gate alındığında hâlâ bağlıysa en güncel listeyle bağlanır. Böylece art arda yapılan
değişiklikler kaybolmaz. Eskiden "bağlanıyor" sırasında gelen ikinci değişiklik yok sayılıyor, anahtar kapalı
göründüğü hâlde site açık kalıyordu.

**Hazır paketler paneli** açıkken paket değişiklikleri yalnızca kaydedilir (`_catalogDirty`); "Tamam"a basınca
tek seferde yeniden bağlanılır. Her tıklamada motor yeniden başlamaz.

**Güvenli DNS sınaması:** Bağlanmadan önce `SecureDns.IsReachableAsync` Yandex'in 1253 portuna doğrudan (IP
ile, DNS'e ihtiyaç duymadan) gerçek bir A sorgusu gönderir; sonuç 1 dakika önbellekte tutulur. Yanıt yoksa ve
internet varsa (`IsOnlineAsync`: 1.1.1.1/8.8.8.8:443'e TCP) motor bu bağlantıda **DNS yönlendirmesi olmadan**
başlatılır ve bilgi bandı gösterilir. Yönlendirme açık kalsaydı, bütün DNS sorguları ölü sunucuya gideceği için
hiçbir site açılmazdı. Kullanıcının ayarı değişmez; sunucu düzelince sağlık denetimi geri geçer. İnternet hiç
yokken karar verilmez.

**Sağlık denetimi** (`HealthLoopAsync`, `HealthCheckAsync`): Bağlıyken 5 dakikada bir, ağ adresi değişince
(10 sn oturma payıyla) ve uykudan uyanınca (zamanlayıcı 1 dakikadan uzun atladıysa) sessizce çalışır. Kullanıcı
o anda bir işlem yapıyorsa (`_gate` meşgul) o tur atlanır.

1. İnternet yoksa hiçbir şey yapılmaz.
2. Güvenli DNS'in durumu motorun başlatıldığı durumdan farklıysa (düştü ya da düzeldi) yeniden bağlanılır.
3. Yalnızca **daha önce açık olan** paketler yeniden test edilir. Biri kapandıysa aynı yöntemle yeniden
   bağlanılır, yine kapalıysa Otomatik bul çalıştırılır. Baştan beri açılmayan paketler yok sayılır; ölü bir özel
   site her 5 dakikada motoru yeniden başlatmasın.

Tepsi ikonu "Kısmen açık" durumunda da hata rengine döner; pencere kapalıyken kullanıcı bunu yalnızca oradan
görür.

**Otomatik bul:** Bütün profilleri sırayla dener, en çok paketi açanı seçer, tamamı açılınca durur. Seçimi
kaydeder; döngü tam başarı olmadan bittiyse motoru en iyi profille yeniden başlatır.

**Açılışta bağlanma:** `StartupAsync`, "Açılışta otomatik bağlan" açıksa ya da `--connect` verildiyse ağ
bağdaştırıcısının hazır olmasını 30 sn'ye kadar bekleyip bağlanır.

---

## 9. Site paketleri

### packs.json

```json
{
  "revision": 3,
  "packs": [
    { "id": "discord", "name": "Discord", "category": "Sohbet", "default": true,
      "test": "https://discord.com/api/v9/gateway",
      "domains": ["discord.com", "discord.gg", "..."] }
  ]
}
```

- `revision`: Uygulama yalnızca elindekinden **büyük** revizyonu kabul eder. Dosya değişince mutlaka artırılır.
- `test`: https olmalı ve **engelli alan adının kendisine** işaret etmeli; açık bir adres seçilirse test her
  zaman "açık" der.
- `default`: true ise yeni kullanıcılarda açık gelir.
- Doğrulama (`SitePacks.Parse`): en fazla 200 paket, paket başına en fazla 100 alan adı, tekrar eden id yok,
  geçerli DNS adları. Bozuk JSON reddedilir, eldeki liste kullanılmaya devam eder.

### Akış

- Açılışta: `%AppData%` önbelleği ile exe'ye gömülü kopyadan **revizyonu büyük olan** yüklenir.
- Açılıştan 5 sn sonra ve 6 saatte bir: `raw.githubusercontent.com/<repo>/main/packs/packs.json`, olmazsa
  `cdn.jsdelivr.net/gh/<repo>@main/...`. Daha yeniyse önbelleğe yazılır ve `SitePacks.Changed` tetiklenir.
  Bağlıysa ve host listesi değiştiyse yeniden bağlanılır.
- **Yeni site eklemek sürüm gerektirmez:** `packs.json`'ı değiştir, revizyonu artır, `main`'e push et.
  jsDelivr önbelleği birkaç saat gecikebilir.
- Gömülü kopya derleme anında `packs/packs.json`'dan alınır. `Bundled_copy_matches_repo_file` testi
  bunu doğrular.

### Kullanıcı seçimleri (AppSettings)

Yalnızca **varsayılandan farklı** seçim saklanır: `EnabledPacks` (varsayılanı kapalı olup açılanlar) ve
`DisabledPacks` (varsayılanı açık olup kapatılanlar). Böylece uzaktan eklenen yeni bir paket, kullanıcının
seçimlerini bozmadan kendi varsayılanıyla gelir. Katalogdan kaldırılan bir paketin id'si ayarlarda kalsa da
yok sayılır. `DisabledPacks == null`, bu alan eklenmeden önce kaydedilmiş eski ayar dosyası demektir; `Load`
onu eski anlamına göre dönüştürür.

### Arayüzde

- Ana "Siteler" listesi yalnızca **açık paketleri ve özel siteleri** gösterir; çarpı ile çıkarılır (hazır paket
  kapanır, özel site silinir).
- "Hazır paketler" paneli: arama kutulu, kaydırılabilir katalog, anahtarlarla açılıp kapatılır.
- "Kendi siteni ekle": alan adı `NormalizeDomain` ile temizlenir (`https://www.X.com/yol` → `x.com`). Bağlıysa
  `SiteScanner` sitenin HTML'indeki mutlak URL'lerden kayıtlı alan adlarını çıkarır (en fazla 30). DNS'te
  çözülen ama HTTPS bağlantısı kurulamayanları önerir. "Herkese öner", GitHub'da doldurulmuş issue formu açar.

---

## 10. Güncelleme

`UpdateService` + `UpdateViewModel`.

1. **Denetim:** `GET api.github.com/repos/<repo>/releases/latest` (20 sn zaman aşımı). Etiket `vX.Y.Z` mevcut sürümden büyükse ve
   `EngelsizDPI.exe` varlığı ile SHA-256 (GitHub'ın `digest` alanı ya da `EngelsizDPI.exe.sha256` dosyası)
   bulunuyorsa güncelleme var demektir. Doğrulanamayan dosya asla kurulmaz.
   **Yedek yol:** API hata verirse (kimliksiz istek sınırı IP başına saatte 60; aynı IP'yi paylaşan çok
   kullanıcıda dolabilir) `github.com/<repo>/releases/latest` adresinin `.../releases/tag/vX.Y.Z` yönlendirmesinden
   sürüm okunur. İndirme ve `.sha256` adresleri `releases/download/<etiket>/` kalıbından üretilir
   (`ParseLatestRedirect`). Bu yolda GitHub'ın `digest` alanı olmadığı için `.sha256` dosyası şarttır; CI her
   sürümde yükler.
2. **Ne zaman:** Açılıştan 15 sn sonra ve 6 saatte bir arka planda; ayrıca başlığın sağ üstündeki durum
   düğmesiyle elle.
3. **Kullanıcıya sorma kuralı:**
   - Elle denetim **asla indirmez**. "vX mevcut" bandı çıkar: *Şimdi yükle* / *Sonra* / *Neler yeni?*.
     "Sonra" denirse tepsi menüsünde "vX güncellemesini yükle" kalır.
   - Arka plan denetimi: "Otomatik güncelle" açıksa sessizce indirir ve "kurulmaya hazır" der. Kurulum bir
     sonraki açılışta ya da kullanıcı "Şimdi kur" deyince yapılır. Kapalıysa yalnızca haber verir.
   - Uygulama hiçbir zaman kullanıcı bir şey yapmadan çalışırken yeniden başlamaz (Discord görüşmesi kopmasın).
4. **İndirme:** `updates\EngelsizDPI-X.Y.Z.exe.part`'a akış hâlinde yazılır, hash doğrulanır.
   **Sürüm koruması:** indirilen exe'nin FileVersion'ı mevcut sürümden büyük değilse reddedilir. Etiket
   yükseltilip `<Version>` unutulursa sonsuz güncelleme döngüsü olmasın diye.
5. **Uygulama:** Motor durdurulur, çalışan exe `.old` olarak yeniden adlandırılır (Windows çalışan exe'nin
   silinmesine değil yeniden adlandırılmasına izin verir), yeni exe kopyalanır, `--updated` (+ bağlıysa
   `--connect`, pencere gizliyse `--tray`) ile başlatılır, eski süreç kapanır. Kopyalama başarısız olursa eski
   exe geri konur. `.old` ve eski indirmeler `CleanupAsync` ile silinir.
6. **Atlanan sürüm:** `StartupHealth`'in bozuk saydığı sürüm (`skipped-version`) denetimde ve bekleyen
   güncellemelerde yok sayılır. Ondan daha yeni bir sürüm çıkınca normal güncellenir.
7. `CanSelfUpdate` yalnızca **tek dosyalık yayınlanmış** exe'de doğrudur (`Assembly.Location` boş).
   `dotnet run` ile çalışırken bin klasörüne yazılmaz. `CanCheck` için `Repo` gerekir; derlemede
   `GitHubRepo` MSBuild özelliğinden `AssemblyMetadata` olarak gömülür (varsayılan `Claymore222/EngelsizDPI`,
   CI'da `$GITHUB_REPOSITORY`).

### 10.1 Bozuk sürümden kurtulma (StartupHealth)

Yayınlanan bir sürüm açılışta çöküyorsa kendini güncelleyemez. Bu yüzden kurulu kopya her açılışta
`Program Files\EngelsizDPI\startup-state` dosyasına `sürüm deneme-sayısı` yazar. Uygulama 10 sn sorunsuz
çalışınca ya da düzgün kapanınca (tepsiden Çıkış, güncelleme için yeniden başlatma) dosya silinir. Dosyadaki
sayı, üst üste sağlıklı bitmeyen açılışları gösterir.

- **Otomatik geri dönüş:** Sayı 2'ye ulaşmışsa (üçüncü açılış denemesinde) ve güncellemeden kalan
  `EngelsizDPI.exe.old` daha eski bir sürümse, `.old` exe'nin yerine konur. Bozuk sürüm atlananlara yazılır ve
  önceki sürüm başlatılır. `.old`, bu yüzden açılış sağlıklı sayılana kadar silinmez (`CleanupAsync`
  güncelleme denetimleri başlarken, yani sağlıklı açılıştan sonra çalışır).
- **Elle kurtulma:** Kullanıcı Releases'ten **eski** bir exe indirip açarsa, kurulu sürüm bozuk sayıldığı için
  `Installer.Decide` `InstallAndLaunch` der ve eski sürüm onun üzerine kurulur. Normalde eski exe kurulu
  (yeni) kopyayı açar.
- **Sınır:** Uygulama `Program.Main`'in bu noktasına gelmeden çöküyorsa (eksik çalışma zamanı, statik
  başlatma hatası) sayaç artmaz; o durumda tek çare daha yüksek numaralı bir düzeltme yayınlamaktır.

---

## 11. Arayüz

- **Tek pencere** (`MainWindow`), genişlik ~460. Kapatma düğmesi pencereyi gizler (`AllowClose` false); çıkış
  tepsi menüsünden yapılır (`ShutdownMode.OnExplicitShutdown`).
- **Bölümler:** başlık (logo + güncelleme durumu düğmesi), bilgi/güncelleme/çakışma bantları, durum kartı
  (büyük anahtar), Siteler, Yöntem (+ Otomatik bul), Ayarlar (Tema, Windows açılınca başlat, Açılışta otomatik
  bağlan, Güvenli DNS, Otomatik güncelle), alt bilgi (sürüm, Günlük).
- **Tepsi:** durum rengine boyanmış kalkan ikonu (`Views/Converters.cs` → `Palette`). Menü: Bağlan/Bağlantıyı
  kes, Göster, güncelleme (varsa), Çıkış.
- **Tema:** `App.axaml` → `ThemeDictionaries` (Dark/Light). Pencerede sabit renk yoktur; her şey
  `DynamicResource` ile gelir. Sistem / Açık / Koyu seçimi `AppTheme.Apply`.

| Anahtar | Koyu | Açık |
|---|---|---|
| Zemin `BgBrush` | `#0F1624` | `#F7F4EF` |
| Kart `CardBrush` | `#172032` | `#FFFDFA` |
| Vurgu / bağlı `AccentBrush` | `#E86F3A` (kiremit) | `#C24E1C` |
| Bağlanıyor `BusyBrush` | `#9CC3F0` | `#2F6FB5` |
| Hata `ErrorBrush` | `#EF6B85` | `#BF3651` |
| Pasif metin `MutedBrush` | `#93A0B8` | `#6E665C` |

**Tasarım kuralları:** Yeşil yok (proje sahibi "yapay/AI duruyor" buluyor), neon ve parıltı yok, Windows 11
Ayarlar sadeliği. Tüm metinler Türkçe.

- **Linkler** `Shell.OpenUrl` ile `explorer.exe` üzerinden açılır. Uygulama yönetici olduğu için doğrudan
  başlatılan tarayıcı da yönetici olurdu. Günlük Notepad ile açılır.
- **Ekran görüntüsü aracı** (`tools/Screenshot`): Avalonia Headless ile pencereyi farklı durumlarda (bağlı,
  hata, ekleme paneli, katalog, güncelleme bantları, açık tema) PNG olarak kaydeder. Motor çalıştırılmaz,
  yönetici gerekmez. Arayüz değişikliğinden sonra mutlaka çalıştırıp bakın.

---

## 12. Ayarlar (`settings.json`)

| Alan | Varsayılan | Anlam |
|---|---|---|
| `ProfileId` | `standard` | Seçili yöntem |
| `EnabledPacks` / `DisabledPacks` | `[]` | Varsayılandan farklı paket seçimleri |
| `CustomDomains` | `[]` | Kullanıcının eklediği alan adları |
| `DnsRedirect` | true | Güvenli DNS (Yandex:1253) |
| `ConnectOnLaunch` | true | Açılışta bağlan |
| `AutoUpdate` | true | Arka planda indir, sonraki açılışta kur |
| `Theme` | `system` | `system` / `light` / `dark` |

"Windows açılınca başlat" ayarda değil, `EngelsizDPI\Autostart` görevinin varlığında tutulur. Bozuk ayar
dosyası varsayılanlarla açılır.

---

## 13. Yayın süreci

1. Değişiklik `main`'e commit edilir (testler geçmeli).
2. `vX.Y.Z` etiketi gönderilir (öncekinden büyük olmalı).
3. `release.yml`: test → `dotnet publish -c Release -r win-x64 -p:Version=X.Y.Z -p:GitHubRepo=<repo>` →
   `EngelsizDPI.exe.sha256` → GitHub Release (`EngelsizDPI.exe` + `.sha256`, otomatik sürüm notları).
4. Kullanıcılardaki uygulamalar en geç 6 saat içinde görür.

`csproj`'daki `<Version>1.0.0</Version>` yalnızca yerel derleme için bir yer tutucudur; gerçek sürüm etiketten
gelir. Release taslak (draft) ya da ön sürüm (pre-release) olmamalı; `/releases/latest` onları görmez.
Yalnızca `packs.json` değişikliği için sürüm gerekmez.

---

## 14. Geliştirme

```bash
dotnet build src/EngelsizDPI
dotnet test --project tests/EngelsizDPI.Tests                 # MTP: --project şart
dotnet run --project tools/Screenshot -- <klasör>             # arayüz PNG'leri, yönetici gerekmez
dotnet run --project src/EngelsizDPI                          # UAC ister, motoru gerçekten çalıştırır
dotnet publish src/EngelsizDPI -c Release -r win-x64 -o publish   # tek dosya exe
```

- Tek dosya/self-contained ayarları yalnızca `-r` verilince uygulanır. Aksi hâlde test projesi uygulama
  projesine başvuramaz (NETSDK1151).
- Proje klasörü taşınamıyor ya da yeniden adlandırılamıyorsa `dotnet build-server shutdown` çalıştırın.
- **Test kapsamı:** kurulum kararı (`Installer.Decide`, bozuk kurulu sürüm dahil), açılış sağlığı sayacı ve
  atlanan sürüm (`StartupHealth`), DNS sorgu paketi ve yanıt doğrulama, yerel sahte sunucuyla DNS sınaması
  (`SecureDns`), sürüm yönlendirmesi ayrıştırma (`ParseLatestRedirect`), sürüm ayrıştırma, release ayrıştırma, checksum, exe
  değiştirme (açıkken dahil), paket ayrıştırma ve doğrulama, paket seçimi saklama, host listesi, tarayıcı
  alan adı çıkarma, `IsCovered`, `NormalizeDomain`. Motor, görevler ve arayüz akışları için otomatik test
  **yoktur**; bunlar elle denenir.
- Bağlantıyı yönetici olmadan ölçmek için `ConnectivityTester.CanReachAsync`'i çağıran küçük bir konsol
  programı ya da `curl -sv https://alanadi/` yeterlidir.
