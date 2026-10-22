# Güvenlik

## Açık bildirme

Bir güvenlik açığı bulduysanız lütfen herkese açık bir issue açmayın. Bu deponun **Security** sekmesindeki
**Report a vulnerability** düğmesiyle gizli olarak bildirin.

## Uygulamanın yetkileri ve önlemler

EngelsizDPI, ağ paketlerini değiştiren WinDivert sürücüsünü yüklediği için **yönetici yetkisiyle** çalışır. İlk
kurulumdan sonra bu yetkiyi UAC sormadan, "en yüksek ayrıcalıklarla" çalışan bir zamanlanmış görev üzerinden alır.
Bu yüzden uygulamanın, sıradan bir programın kurcalayabileceği hiçbir şeyi yönetici olarak çalıştırmaması gerekir:

- **Yönetici olarak çalışan her dosya yalnızca yöneticilerin yazabildiği yerde durur:** exe, GoodbyeDPI ve
  WinDivert dosyaları, indirilen güncellemeler ve uygulamanın yerel kütüphaneleri (SkiaSharp vb.)
  `C:\Program Files\EngelsizDPI` altındadır. Tek dosyalık .NET uygulamaları bu kütüphaneleri varsayılan olarak
  `%TEMP%` altına çıkarır; EngelsizDPI çıkarma klasörünü bilerek Program Files altına yönlendirir.
- **Günlük klasörü** (`C:\ProgramData\EngelsizDPI`) yalnızca yöneticilere yazılabilir, kullanıcılara salt
  okunur yapılır.
- **Güncellemeler** yalnızca bu deponun GitHub Releases sayfasından, HTTPS ile indirilir ve SHA-256 özetiyle
  doğrulanır. Doğrulanamayan ya da sürüm numarası etiketle uyuşmayan dosya kurulmaz. Exe'ler GitHub Actions'ta,
  bu depodaki koddan derlenir; Actions adımları commit'e sabitlenmiştir.
- **GoodbyeDPI ve WinDivert dosyaları** [GoodbyeDPI 0.2.3rc3](https://github.com/ValdikSS/GoodbyeDPI/releases/tag/0.2.3rc3)
  resmi sürümünün `x86_64` dosyalarıyla birebir aynıdır:

  | Dosya | SHA-256 |
  |---|---|
  | `goodbyedpi.exe` | `8d412b094bb9c137ff25ba9a794d1122ecc84bb776debff6c249723a13cc31cd` |
  | `WinDivert.dll` | `6110bfa44667405179c3e15e12af1b62037e447ed59b054b19042032995e6c7e` |
  | `WinDivert64.sys` | `e69b5ba3f0cd6cfb2983e442636e7f0b342b61b15264b0328317d4559c82cf50` |

- **Bağlantılar ve dosyalar** (sürüm notları, sorun formu, günlük) Explorer üzerinden, normal kullanıcı
  yetkisiyle açılır; tarayıcı ya da Not Defteri yönetici olarak başlatılmaz.
- **Uzaktan gelen site listesi** yalnızca alan adı listesi ve test adresi içerir, doğrulanır. Test adresi
  paketin kendi alan adlarından biri olmak zorundadır.

## Ağ istekleri

Uygulama hiçbir API anahtarı ya da hesap bilgisi içermez ve kullanıcı verisi göndermez. Yaptığı istekler:

| Ne | Nereye | Ne zaman |
|---|---|---|
| Yeni sürüm var mı | `api.github.com` (olmazsa `github.com/.../releases/latest`) | Açılıştan ~15 sn sonra ve 6 saatte bir |
| Güncelleme indirme | `github.com/.../releases/download/...` | Yalnızca yeni sürüm varsa |
| Site listesi | `raw.githubusercontent.com` (olmazsa `cdn.jsdelivr.net`) | Açılıştan 5 sn sonra ve 6 saatte bir |
| Güvenli DNS sınaması | `77.88.8.8:1253` (Yandex DNS) | Bağlanırken ve sağlık denetiminde |
| İnternet var mı | `1.1.1.1:443`, `8.8.8.8:443` (yalnızca TCP bağlantısı) | Bağlanırken ve sağlık denetiminde |
| Site testleri | Etkin paketlerin test adresleri | Bağlanırken ve 5 dakikada bir |

GitHub istekleri kimliksizdir; sınırlar her kullanıcının kendi IP adresine uygulanır.
