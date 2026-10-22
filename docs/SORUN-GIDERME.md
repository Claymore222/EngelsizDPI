# EngelsizDPI — Olası Sorunlar ve Çözümleri

Bu belge, ileride çıkabilecek sorunları **belirti → neden → çözüm** biçiminde toplar. Bir kısmı yaşanmış ve
çözülmüş, bir kısmı dış etkenlerle (operatör, antivirüs, Windows, GitHub) her an çıkabilecek, bir kısmı da
kodda bilinen ama henüz düzeltilmemiş zayıflıklardır. Her başlıkta hangisi olduğu yazıyor.

Önce mimariyi okuyun: [MIMARI.md](MIMARI.md).

---

## 0. Teşhis araçları

Bir sorun bildirildiğinde ilk bakılacak yerler:

| Araç | Ne söyler |
|---|---|
| `C:\ProgramData\EngelsizDPI\logs\engelsizdpi.log` (uygulamada alt bilgideki **Günlük**) | Başlatılan goodbyedpi komutu, `[goodbyedpi]` çıktısı, WinDivert hata kodları, her bağlantıdaki test sonuçları (`Test (Standart): Discord=açık, ...`), site listesi ve güncelleme hataları |
| `curl -sv -o NUL --max-time 7 https://ALANADI/` | `Recv failure: Connection was reset` (çıkış 35) = SNI/DPI engeli, **bu uygulama açabilir**. Çıkış 28 (zaman aşımı) = büyük ihtimalle IP engeli, **açamaz**. Çıkış 6 = DNS çözülemedi. `HTTP/` satırı = açık |
| `sc query WinDivert` | Sürücü durumu. `STOP_PENDING` ya da "marked for deletion" = 1058 döngüsü (bkz. 1.1) |
| `sc query GoodbyeDPI` | Eski GoodbyeDPI-Turkey/DNSChanger servisi kurulu mu |
| `schtasks /query /fo csv /nh \| findstr EngelsizDPI` | Görevler. Kullanıcı başına `Launch-<SID>` (UAC'siz başlatma) ve açılışta başlatma açıksa `Autostart-<SID>`. SID için: `whoami /user` |
| `schtasks /query /tn "EngelsizDPI\Launch-<SID>" /xml` | Görevin exe'si, argümanı (`--from-task`), sahibi (`UserId`), çalışma düzeyi |
| `C:\Program Files\EngelsizDPI\startup-state` | Varsa: `sürüm sayı` = o sürümün üst üste sağlıklı bitmeyen açılışları (bkz. 5.2) |
| `C:\Program Files\EngelsizDPI\skipped-version` | Bozuk sayılıp geri dönülen sürüm; güncellemede atlanır |
| `tasklist \| findstr /i "goodbyedpi EngelsizDPI"` | Süreçler çalışıyor mu, birden fazla goodbyedpi var mı |
| `%AppData%\EngelsizDPI\settings.json` | Seçili yöntem, paketler, ayarlar |
| `%AppData%\EngelsizDPI\packs.json` | İndirilmiş site listesi (`revision`) |

> **Not:** Test yaparken tarayıcının kendi VPN'i (Opera vb.), sistem VPN'i ya da proxy kapalı olmalı. Yoksa
> engelli siteler zaten açılır ve sonuç yanıltır.

---

## 1. Motor ve WinDivert sürücüsü

### 1.1 "Açılmadı", birkaç saniye sonra "Bağlantı koptu" / günlükte `Error opening filter: 1058` — *ÇÖZÜLDÜ, geri gelirse*

- **Neden:** WinDivert son handle kapanınca sürücüyü kaldırır. Kaldırma bitmeden goodbyedpi yeniden açılırsa
  1058/1060/1072 hatası alır. Hatalı goodbyedpi 20 sn bekleyip kapanırken servis handle'ını tuttuğu için sürücü
  "silinmek üzere" durumunda takılı kalır: kısır döngü.
- **Mevcut çözüm:** `WinDivertKeepAlive` uygulama ömrü boyunca boş bir handle tutar. Sürücü açılamazsa goodbyedpi
  hiç başlatılmaz.
- **Geri gelirse kontrol edin:** `_driver.Dispose()` motor durdurulurken (`StopAsync`) değil, **yalnızca**
  uygulama kapanırken çağrılmalı. "Düzelt" (`RemoveConflictsAsync`), sürücüyü biz tutarken `sc stop WinDivert`
  yapmamalı.
- **Kullanıcıya:** Uygulamadan çıkın, 10 sn bekleyin ve tekrar açın. Olmazsa bilgisayarı yeniden başlatın.

### 1.2 "WinDivert sürücü dosyası bulunamadı" (hata 2/3) ya da exe kayboluyor — *dış etken*

- **Neden:** Antivirüs (Windows Defender dahil) `goodbyedpi.exe`, `WinDivert64.sys` ya da `EngelsizDPI.exe`'yi
  karantinaya alıyor. GoodbyeDPI ve WinDivert bazı motorlarda "HackTool / RiskWare" olarak işaretleniyor.
- **Çözüm:** `C:\Program Files\EngelsizDPI` ve `C:\ProgramData\EngelsizDPI` klasörlerini istisnalara ekleyin,
  karantinadan geri yükleyin. Motor dosyaları her bağlanmada exe'den yeniden çıkarıldığı için silinen dosya
  kendiliğinden geri gelir; asıl sorun antivirüsün tekrar silmesidir.
- **Kalıcı iyileştirme fikri:** Yanlış pozitifleri antivirüs firmalarına bildirmek, exe'yi kod imzalama
  sertifikasıyla imzalamak.

### 1.3 Hata 577 ya da 1275: "Sürücü engellendi" — *dış etken*

- **Neden:** Antivirüs (özellikle Kaspersky) sürücü yüklemeyi engelliyor; Windows "Bellek bütünlüğü" (HVCI) ya
  da kurumsal sürücü engelleme listesi; ya da başka bir programın yüklediği **farklı sürümde** WinDivert
  bellekte (zapret, başka bir GoodbyeDPI, bazı oyun hızlandırıcılar).
- **Çözüm:** Başka WinDivert kullanan aracı kapatıp bilgisayarı yeniden başlatın. Antivirüs istisnası ekleyin.
  Sorun sürüyorsa günlükteki kodu `WinDivertKeepAlive.Describe` açıklamasıyla karşılaştırın.

### 1.4 Hata 654: "Önceki sürücü tam kaldırılamadı" — *dış etken*

- **Çözüm:** Bilgisayarı yeniden başlatmak tek güvenilir çözüm.

### 1.5 Hata 1753: BFE servisi çalışmıyor — *dış etken*

- **Neden:** "Temel Filtreleme Altyapısı" (Base Filtering Engine) kapatılmış. Bazı "Windows hızlandırma"
  araçları bunu yapıyor.
- **Çözüm:** `services.msc` → Base Filtering Engine → Otomatik + Başlat.

### 1.6 GoodbyeDPI / WinDivert sürümünü yükseltirken — *bilinen risk*

- **Risk:** `ExtractEngineFiles`, kilitli `WinDivert64.sys`'nin üzerine yazamazsa eski kopyayı kullanır. Yeni
  `WinDivert.dll` ile bellekteki eski sürücü uyuşmazsa sürücü açılmaz; bu, yeniden başlatmaya kadar sürer.
- **Çözüm önerisi:** Motor dosyalarını sürüm adlı alt klasöre çıkarın (`engine\2.2.2\`, `engine\2.3.0\`). Yeni
  sürüm yeni klasörden yüklensin, eski klasör sonraki açılışlarda silinsin. Yükseltmeden sonra mutlaka
  1058/çakışma senaryolarını elle deneyin.
- Yeni dosyalar `src/EngelsizDPI/Engine/win-x64/` altına konur. `THIRD-PARTY-NOTICES.md` ve
  `GoodbyeDPI-LICENSE.txt` güncellenmeli.

### 1.7 Eski GoodbyeDPI servisi / DNSChanger ile çakışma — *çözüldü, uyarı gösteriliyor*

- **Belirti:** Uygulamada pembe çakışma bandı. İki araç aynı anda çalışınca biri diğerini bozar.
- **Çözüm:** Banttaki **Düzelt** düğmesi servisi ve görevi siler, diğer goodbyedpi süreçlerini kapatır.
  Elle: `sc stop GoodbyeDPI && sc delete GoodbyeDPI`, `schtasks /delete /tn DNSChangerApp_Startup /f`.

### 1.8 Oyun hileye karşı sistemleri (anti-cheat) — *olası, doğrulanmadı*

- **Risk:** Bazı çekirdek düzeyi anti-cheat sistemleri (FACEIT, Vanguard vb.) WinDivert gibi paket
  yakalayan sürücüleri sevmeyebilir; oyun açılmayabilir ya da uyarı verebilir.
- **Çözüm:** O oyunu oynarken bağlantıyı kesmek. `WinDivertKeepAlive` handle'ı ancak uygulama kapanınca
  bırakıldığı için tam temizlik için **tepsiden Çıkış** gerekir. İhtiyaç olursa "Bağlantıyı kes"in sürücüyü de
  bırakması seçenek olarak eklenebilir (1.1'i bozmamaya dikkat).

---

## 2. Operatör tarafındaki değişiklikler

### 2.1 Siteler "Kısmen açık" ya da hiç açılmıyor, Otomatik bul da çözmüyor — *her an olabilir*

- **Neden:** Operatör DPI yöntemini değiştirdi (yeni bir parçalama/TTL davranışı, farklı cihaz).
- **Çözüm adımları:**
  1. Günlükte `Test (...)` satırlarına bakın: hangi profillerde hangi paket açıldı.
  2. Tarayıcıda deneyin. Uygulamanın testi başarısız ama tarayıcı açıyorsa sorun testtedir (bkz. 4.2).
  3. Güncel GoodbyeDPI-Turkey `.cmd` dosyalarındaki ve GoodbyeDPI tartışmalarındaki yeni argümanları
     `Profiles.All`'a yeni bir profil olarak ekleyin (id değişmesin, eski kullanıcıların ayarı bozulmasın).
  4. GoodbyeDPI yetmiyorsa daha güçlü motor: **zapret (`winws`)**. `IBypassEngine`'i uygulayan yeni bir sınıf
     olarak eklenir. `EngineFactory` ya da profil başına motor seçimi gerekir.

### 2.2 Site zaman aşımına düşüyor (curl çıkış kodu 28) — *uygulamanın çözemeyeceği durum*

- **Neden:** IP engeli. Örnek: NordVPN, Surfshark ve OpenVPN siteleri geliştirici hattında böyle.
- **Çözüm:** DPI atlatma çözmez. Kullanıcıya dürüstçe VPN gerektiği söylenir. Böyle siteler hazır pakete
  **eklenmez**.

### 2.3 Discord sesli sohbet ya da Roblox oyun içi bağlantı gitmiyor — *ileride olası*

- **Neden:** Bunlar UDP. GoodbyeDPI UDP'ye (DNS ve `-9` profilindeki QUIC engeli dışında) dokunmaz.
- **Çözüm:** zapret (`winws`) motoru: UDP/QUIC için desteği var (`--dpi-desync=fake` + `--filter-udp`).
  `IBypassEngine` arkasına eklenmeli.

### 2.4 Siteye ait yeni bir alan adı engellendi — *her an olabilir*

- **Belirti:** Site açılıyor ama resimler, giriş ya da güncelleme çalışmıyor. Örnek: Discord yeni bir CDN alan
  adına geçti.
- **Çözüm:** Tarayıcı geliştirici araçlarında (Ağ sekmesi) başarısız istekleri bulun ya da uygulamadaki
  "Kendi siteni ekle" taramasını kullanın. Alan adını `packs/packs.json`'daki paketin `domains` listesine
  ekleyin, `revision`'ı artırın ve push edin. Sürüm gerekmez.

### 2.5 Operatör DNS'i farklı yoldan zehirliyor ya da Yandex 1253 engellendi — *olası*

- **Belirti:** Bağlıyken **bütün** internet çalışmıyor ya da çok yavaş. Bağlantıyı kesince düzeliyor.
- **Neden:** "Güvenli DNS" açıkken goodbyedpi tüm 53. port DNS sorgularını `77.88.8.8:1253`'e yönlendirir.
  Bu sunucuya ulaşılamazsa hiçbir alan adı çözülmez.
- **Hemen:** Ayarlar → Güvenli DNS kapat.
- **Kalıcı:** `GoodbyeDpiEngine.cs` içindeki `DnsV4/DnsV6/DnsPort` sabitlerini değiştirin. Standart dışı port
  sunan bir alternatif bulun ve **önce kullanıcı hattında doğrulayın**. Örneğin AdGuard DNS'in 5353 portu
  aday olabilir; doğrulanmadı. Daha iyi çözüm: bağlanırken DNS sunucusunu test etmek, yanıt vermiyorsa
  yönlendirmeyi kapatıp kullanıcıyı uyarmak.

---

## 3. Yetki, kurulum ve başlangıç

### 3.1 Her açılışta UAC soruyor — *kontrol edilecek*

- **Neden olasılıkları:**
  - `EngelsizDPI\Launch-<SID>` görevi yok ya da bozuk.
  - Kurulu exe, açılan exe'den eski (`IsInstalledAndCurrent` false). Örneğin kullanıcı İndirilenler'deki yeni
    exe'yi açıyor ve kurulum başarısız oluyor.
  - Uygulama argümanla açılıyor. Görev argüman taşımadığı için argümanlı açılışta UAC'ye düşülür (tasarım gereği).
  - Hesap artık yönetici değil: görev yönetici yetkisi veremez, uygulama bilerek UAC'ye düşer (bkz. 3.4).
- **Çözüm:** `schtasks /query /fo csv /nh | findstr EngelsizDPI` ile kontrol edin. Uygulamayı bir kez
  yönetici olarak açmak `RefreshInstalledState` ile görevi yeniden oluşturur.

### 3.2 Windows açılışında başlamıyor ya da Discord'dan sonra başlıyor — *şu an çalışıyor*

- **Mevcut durum:** `EngelsizDPI\Autostart` oturum açılışında beklemesiz `--tray` ile başlıyor. Geliştirici
  makinesinde Discord'dan önce devreye girdiği doğrulandı.
- **Olası nedenler:** Görev silinmiş, "Windows açılınca başlat" kapalı, pilde (eski görev ayarları), kurulum
  klasörü taşınmış.
- **Discord yine de önce açılırsa:** Discord'u yeniden başlatmak yeter. Kalıcı çözüm olarak motoru oturum
  açılmadan önce başlatan bir **Windows servisi** mimarisi düşünülebilir; şimdilik gerek görülmedi.

### 3.3 Aynı bilgisayarda birden fazla Windows kullanıcısı — *ÇÖZÜLDÜ (v1.3.1)*

- **Eski sorun:** Görevler sabit adlıydı (`EngelsizDPI\Launch`) ve en son oluşturan kullanıcının oturumunda
  çalışıyordu. İkinci kullanıcıya pencere gelmiyor, ikinci kullanıcı UAC ile açınca da birincisi bozuluyordu.
- **Çözüm:** Görev adlarında kullanıcının SID'i var (`Launch-<SID>`, `Autostart-<SID>`). Her kullanıcı ilk
  açılışta bir kez UAC görür ve kendi görevini oluşturur. Eski ortak görevler yalnızca sahibine aitse taşınır.
  Kaldırma bütün kullanıcıların görevlerini siler.
- **Hâlâ geçerli sınırlar:** İki kullanıcı **aynı anda** oturum açıp ikisinde de bağlanırsa iki goodbyedpi
  çalışır. GoodbyeDPI bütün bilgisayarın trafiğini işlediği için çakışma bandı çıkabilir ve "Düzelt" diğer
  oturumdaki goodbyedpi'ı kapatır. Ayarlar (`%AppData%`) kullanıcıya özeldir. Kaldırma yalnızca kaldıran
  kullanıcının ayarlarını siler.

### 3.4 Yönetici olmayan hesap ya da sonradan yetkisi alınmış hesap — *ÇÖZÜLDÜ (v1.3.1)*

- **Eski risk:** `HighestAvailable` görevi, hesap yönetici değilse yetkisiz çalışır. Exe yönetici olmadığını
  görüp yine görevi çalıştırınca sonsuz süreç başlatma döngüsü oluşabiliyordu.
- **Çözüm:** Launch görevi exe'yi `--from-task` ile başlatır. `Program.Main` bu argümanı görüp hâlâ yönetici
  değilse görevi tekrar çalıştırmaz, UAC ister ve günlüğe "Başlatma görevi yönetici yetkisi vermedi" yazar.
  Yönetici olmayan hesapta uygulama her açılışta yönetici parolası ister; WinDivert için bu kaçınılmaz.
- **Bu mantığı değiştirirken:** `--from-task` her yerde en başta ayıklanır; `HandleInstall` ve güncelleme
  yeniden başlatmaları bu argümanı taşımamalı.

### 3.5 "EngelsizDPI şu anda açık ve kapatılamadı" — *nadir*

- **Neden:** Kurulum ya da kaldırma, çalışan eski kopyayı `Local\EngelsizDPI.Exit` olayıyla kapatmaya
  çalıştı, 15 sn'de kapanmadı. Eski kopya donmuş olabilir ya da farklı oturumda çalışıyor olabilir (`Local\`
  nesneler oturuma özeldir).
- **Çözüm:** Görev Yöneticisi'nden `EngelsizDPI.exe` ve `goodbyedpi.exe`'yi sonlandırıp tekrar deneyin.

### 3.6 Kaldırma sonrası artıklar — *tasarım gereği küçük artıklar olabilir*

- `WinDivert` sürücü servisi son handle kapanınca kendiliğinden kalkar. Kalırsa `sc delete WinDivert` +
  yeniden başlatma.
- Kurulum klasörü, `cmd` gecikmeli silmesiyle kaldırılır. Antivirüs bunu engellerse klasör elle silinir.
- Kaldırma yalnızca **kaldıran kullanıcının** `%AppData%\EngelsizDPI` klasörünü siler.

### 3.7 ARM64 Windows ve eski Windows sürümleri — *desteklenmiyor*

- Yalnızca `win-x64` derleniyor. WinDivert x64 sürücüsü ARM64 Windows'ta yüklenmez (çekirdek sürücüleri
  emüle edilmez). .NET 10 en az Windows 10 ister.
- ARM64 için WinDivert'in ARM64 desteği ya da başka bir motor gerekir; şu an kapsam dışı.

---

## 4. Bağlantı testi ve arayüz durumu

### 4.1 Anahtar "Bağlı" ama site açılmıyor — *bilinen eksik*

- **Neden:** Test yalnızca bağlanırken yapılır. Sonradan operatör davranışı değişirse, bilgisayar uykudan
  uyanır ya da ağ değişirse uygulama bunu fark etmez.
- **Geçici:** Anahtarı kapatıp açmak (yeniden test eder) ya da Otomatik bul.
- **İyileştirme önerisi:** Arka planda periyodik ve ağ değişikliğinde (`NetworkChange.NetworkAddressChanged`,
  güç olayları) yeniden test etmek. Kapalıysa sessizce yeniden bağlanmak, olmazsa tepside uyarmak.

### 4.2 Site tarayıcıda açılıyor ama uygulama "kapalı" diyor (ya da tersi) — *olası*

- **Nedenler:** Paketin `test` adresi yanlış (açık bir adres seçilmişse test hep "açık" der; ölü bir adres
  seçilmişse hep "kapalı"). Sistem saati yanlış (TLS hatası). Kurumsal proxy. Sunucu bot korumasıyla
  bağlantıyı kesiyor.
- **Çözüm:** `test` adresi, engelli alan adının kendisinde ve her zaman yanıt veren bir uç nokta olmalı (Discord
  için `/api/v9/gateway`). Yanıt kodu önemsizdir; yalnızca TLS + HTTP yanıtı gerekir.

### 4.3 Art arda hızlı değişikliklerde liste ile motor uyuşmuyor — *ÇÖZÜLDÜ (v1.3.0)*

- **Belirti (eski):** İki paketi hızlıca kapatınca biri kapalı göründüğü hâlde açık kalıyordu.
- **Çözüm:** `SaveAndReconnect` artık yeniden bağlanma sürerken gelen değişikliği sıraya alıyor
  (`_reconnectQueued`). Bu mantığı değiştirirken: "bağlanıyor" (`Busy`) sırasında `IsConnected` false olduğu
  için koşul `SwitchOn` ile kontrol edilmeli.

### 4.4 Otomatik bul çok uzun sürüyor — *tasarım gereği*

- Her profil: motor yeniden başlatma + 1,5 sn bekleme + en fazla 8 sn test. 5 profil ~1 dakikayı bulabilir.
  Profil eklerken bu süre de uzar.

### 4.5 Tepsi ikonu kayboldu — *olası*

- **Neden:** `explorer.exe` yeniden başlatıldığında bazı tepsi uygulamalarının ikonu kaybolur.
- **Çözüm:** Uygulamayı Başlat menüsünden tekrar açmak mevcut kopyanın penceresini öne getirir (Show olayı).
  Kalıcı çözüm gerekiyorsa Avalonia'nın `TaskbarCreated` mesajını işleyip işlemediğini kontrol edin.

---

## 5. Güncelleme

### 5.1 "Sunucuya ulaşılamadı" / güncelleme hiç gelmiyor — *olası*

- **Nedenler:**
  - **GitHub API sınırı:** Kimliksiz istekler IP başına saatte 60. Türkiye'de CGNAT ile çok kullanıcı aynı IP'yi
    paylaşabilir ve 403 alınır.
  - `api.github.com` erişime kapatılmış ya da yavaş.
  - Release taslak ya da ön sürüm olarak yayınlanmış (`/releases/latest` görmez).
  - Varlık adı tam olarak `EngelsizDPI.exe` değil ya da `.sha256`/digest yok. Doğrulanamayan güncelleme
    bilerek yok sayılır.
- **Çözüm önerisi (sınır için):** API yerine `https://github.com/<repo>/releases/latest` adresine istek atıp
  yönlendirilen `.../tag/vX.Y.Z` adresinden sürümü okumak (rate limit yok). İndirme adresi
  `.../releases/download/vX.Y.Z/EngelsizDPI.exe` ve `.sha256` sabit kalıptan üretilebilir.

### 5.2 Bozuk bir sürüm yayınlandı — *v1.3.1'den itibaren kendiliğinden toparlanır, yine de dikkat*

- **Ne olur:** `StartupHealth` (bkz. MIMARI §10.1) kurulu sürümün açılışlarını sayar:
  - Güncellemeden sonra yeni sürüm üst üste 2 kez sağlıklı açılamazsa, **üçüncü denemede** saklanan önceki
    sürüme (`EngelsizDPI.exe.old`) kendiliğinden döner ve bozuk sürümü atlar. Kullanıcı 1–2 kez "açılmadı"
    görür, sonra eski sürüm açılır.
  - `.old` yoksa (ör. sağlıklı açılış sayılıp silinmişse ve sorun sonradan başladıysa): kullanıcı Releases'ten
    **eski** bir exe indirip açabilir. Kurulu sürüm bozuk sayıldığı için eski sürüm onun üzerine kurulur.
  - Günlükte `üst üste açılamadı; vX sürümüne geri dönülüyor` ya da `Kurulu vX açılamıyordu` satırları görünür.
- **Yine de yapılacak:** Düzeltmeyi **her zaman daha büyük numarayla** yayınlayın (`v1.4.1`). Atlanan sürüm
  yalnızca kendisi atlanır; daha yeni sürümler normal gelir.
- **Kurtaramadığı durum:** Uygulama `Program.Main`'de sayaca gelmeden çöküyorsa (eksik çalışma zamanı, statik
  başlatma hatası) geri dönüş tetiklenmez. O zaman tek çare yeni numarayla düzeltme ve elle indirmedir.
- **Sağlıklı sayılma şartı:** 10 sn sorunsuz çalışmak ya da tepsiden düzgün çıkış. Uygulama 10 sn dolmadan
  üst üste iki kez zorla kapatılırsa (Görev Yöneticisi, ani kapatma) ve `.old` hâlâ duruyorsa yanlışlıkla geri
  dönülebilir. Zararı küçüktür: atlanan sürümden yeni bir sürüm çıkınca yine güncellenir.
- **Önlem:** Etiket atmadan önce testler + ekran görüntüsü aracı + mümkünse yerel `dotnet publish` exe'sini
  gerçekten açmak.

### 5.3 Aynı güncelleme sonsuza kadar tekrar kuruluyor — *ÖNLENDİ*

- **Neden olurdu:** Etiket (ör. `v1.0.1`) yükseltilip exe'nin sürümü değişmezse (`-p:Version` verilmeden elle
  derleme).
- **Mevcut önlem:** `DownloadAsync`, exe'nin FileVersion'ı mevcuttan büyük değilse reddeder. CI sürümü
  etiketten verir; **elle exe yüklemeyin**, hep CI'ı kullanın.

### 5.4 Güncelleme sonrası uygulama açılmadı — *nadir*

- **Neden:** Yeni süreç `--updated` ile 20 sn mutex bekler. Eski süreç kapanmazsa yeni süreç pencereyi göstermek
  için Show sinyali gönderip çıkar.
- **Çözüm:** Görev Yöneticisi'nden süreçleri sonlandırıp tekrar açın. `EngelsizDPI.exe.old` dosyası
  `CleanupAsync` ile silinir; kalırsa elle silinebilir.

### 5.5 Depo adı değişti, silindi ya da özel yapıldı — *KRİTİK, dikkat*

- Depo adı (`Claymore222/EngelsizDPI`) **her exe'nin içine gömülüdür**. Depo silinir ya da özel yapılırsa
  kullanıcılardaki tüm kopyalar bir daha güncelleme ve site listesi alamaz. GitHub yeniden adlandırmalarda
  yönlendirme yapar ama buna güvenmeyin.
- **Kural:** Depoyu silmeyin, özel yapmayın, eski Release'leri silmeyin.

---

## 6. Site paketleri

### 6.1 `packs.json` değişti ama kullanıcılara gelmiyor

- **Kontrol listesi:**
  - `revision` artırıldı mı? Eşit ya da küçükse uygulama indirmez.
  - JSON geçerli mi, doğrulamadan geçiyor mu? Geçmezse uzak liste sessizce reddedilir. `dotnet test` çalıştırın:
    `Repo_packs_json_is_valid_and_contains_defaults`.
  - `main` dalına push edildi mi? Uygulama `main`'den okur.
  - jsDelivr önbelleği (raw.githubusercontent erişilemezse) birkaç saat gecikebilir.
  - Uygulama 6 saatte bir yeniler; hemen görmek için yeniden açın.
- Günlükte `Site listesi güncellendi (revizyon N, M paket)` ya da `Site listesi alınamadı (...)` satırı görünür.

### 6.2 raw.githubusercontent.com erişime kapandı — *olası (bazı ülkelerde zaman zaman oluyor)*

- Yedek jsDelivr devreye girer. İkisi de kapanırsa uygulama önbellekteki ya da gömülü listeyle çalışmaya devam
  eder. Çözüm olarak `SitePacks.RefreshAsync`'teki URL listesine üçüncü bir yansı eklenebilir.

### 6.3 Paket ekleme politikası

- Eklemeden önce **kullanıcı hattında** doğrulayın: `curl -sv https://ALANADI/` → `Connection was reset`
  (çıkış kodu 35). Zaman aşımı (28) olanları eklemeyin (bkz. 2.2).
- Engelli olmayan site eklemeyin; kullanıcı kafası karışır. Örnek: `voanews.com` açık olduğu hâlde
  "Amerika'nın Sesi" paketi eklenmiş, kullanıcı itiraz edince kaldırılmıştı.
- Yetişkin, arkadaşlık ve siyasi içerikli siteler hazır listeye konmaz; isteyen "Kendi siteni ekle" ile ekler.
- Paket id'leri değiştirilmez: kullanıcı ayarlarında id ile saklanıyor.
- Yeni paketler `"default": false` gelir. Yalnızca çok geniş kitleyi ilgilendirenler (Discord, Roblox) varsayılan
  açıktır.

---

## 7. Geliştirme ortamı

| Belirti | Çözüm |
|---|---|
| Test adımı `NETSDK1151` ile düşüyor | Tek dosya/self-contained ayarı koşulsuz yapılmış. Yalnızca `RuntimeIdentifier` verildiğinde uygulanmalı (csproj'daki koşullu `PropertyGroup`) |
| `dotnet test` çalışmıyor ya da test bulmuyor | `global.json` Microsoft Testing Platform kullanır: `dotnet test --project tests/EngelsizDPI.Tests` |
| Klasör taşınamıyor ("kullanımda") | `dotnet build-server shutdown` |
| `dotnet run` ile güncelleme denetimi çalışmıyor | Normal; `CanSelfUpdate` yalnızca yayınlanmış tek dosya exe'de doğru. Denetim için `Repo` dolu olmalı |
| Statik alan başlatma sırası hataları | `UpdateService` içinde `Http`, `CurrentVersion`'dan **sonra** tanımlı olmalı (C# statik alanları yazılış sırasıyla başlatır) |
| `unsafe` + `await` derleme hatası (CS4004) | `unsafe`'i sınıfa değil, yalnızca ilgili üyelere koyun (`WinDivertKeepAlive` örneği) |
| Arayüzde renk temaya uymuyor | Pencerede sabit renk kullanmayın. `App.axaml` → `ThemeDictionaries` içine Dark ve Light için anahtar ekleyip `DynamicResource` ile bağlayın |
| Satır sonu uyarıları (LF → CRLF) | `.gitattributes` `text=auto`; zararsız |
