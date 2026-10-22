# Bu projede çalışacaklar için

EngelsizDPI, Türkiye'de engelli sitelere (Discord, Roblox…) VPN'siz erişim sağlayan bir Windows tepsi uygulaması:
C# / .NET 10 + Avalonia 12, arka planda GoodbyeDPI + WinDivert ve Yandex DNS (port 1253) yönlendirmesi.

**İşe başlamadan önce okuyun:**

- [docs/MIMARI.md](docs/MIMARI.md): Nasıl çalıştığı, açılış/yetki akışı, motor, paketler, güncelleme, arayüz,
  yayın süreci.
- [docs/SORUN-GIDERME.md](docs/SORUN-GIDERME.md): Teşhis araçları, olası sorunlar ve çözümleri, bilinen
  zayıflıklar.

## Kurallar

1. **Commit kimliği:** Depo yerel git ayarındaki kimlik kullanılır (`Claymore222` + GitHub noreply adresi).
   Değiştirmeyin; global git kimliğini kullanmayın. Gerçek ad ya da e-posta hiçbir dosyaya, commit'e, PR'a yazılmaz.
2. **Yapay zekâ imzası yok:** Commit mesajlarına, PR açıklamalarına ve dosyalara `Co-Authored-By`, "Generated
   with …" gibi hiçbir yapay zekâ/araç imzası eklenmez.
3. **Push ve etiketleri proje sahibi atar** (GitHub Desktop ile). Açıkça istenmedikçe yalnızca yerel commit
   yapın; `push`, `tag`, `force-push` yapmayın.
4. **Arayüz dili Türkçe**, sade ve günlük dil. Kod yorumları da Türkçe ve mevcut yoğunlukta.
5. **Renkler** yalnızca `App.axaml` tema kaynaklarından (`DynamicResource`), Dark ve Light için ayrı.
   **Yeşil kullanılmaz**, neon ya da parıltı yok. Vurgu rengi kiremit (`#E86F3A` / açık temada `#C24E1C`).
6. **Sistemi kirletme:** Kalıcı servis kurma, Windows DNS ayarını değiştirme. Yönetici olarak çalışacak
   exe/dll'ler yalnızca Program Files altında durur (UAC'siz yönetici başlatma var, bkz. MIMARI §6).
7. **Kullanıcıya sormadan** güncelleme kurma ya da uygulamayı yeniden başlatma yok.
8. **Site paketleri:** Eklemeden önce engeli kullanıcı hattında `curl -sv` ile doğrulayın (RST = eklenebilir,
   zaman aşımı = IP engeli, eklenmez). Yetişkin, arkadaşlık ve siyasi siteler hazır listeye girmez. Her
   değişiklikte `revision` +1. Paket id'leri değiştirilmez.
9. **WinDivertKeepAlive'a dokunurken dikkat:** Sürücü handle'ı motor yeniden başlatılırken kapatılmamalı
   (1058 döngüsü, bkz. SORUN-GIDERME §1.1).
10. Değişiklikten sonra testler geçmeli. Arayüz değiştiyse ekran görüntüsü aracıyla iki temada bakılmalı.
    Yapılmayan gerçek cihaz testini "test edildi" diye raporlamayın.

## Komutlar

```bash
dotnet build src/EngelsizDPI
dotnet test --project tests/EngelsizDPI.Tests
dotnet run --project tools/Screenshot -- <çıktı-klasörü>     # yönetici gerekmez
dotnet publish src/EngelsizDPI -c Release -r win-x64 -o publish
```

**Yayın:** `vX.Y.Z` etiketi (öncekinden büyük) → GitHub Actions exe + `.sha256` yayınlar → kurulu uygulamalar
kendini günceller. Yalnızca `packs/packs.json` değiştiyse sürüm gerekmez; `main`'e push yeterli.
