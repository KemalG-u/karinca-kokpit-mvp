> 🚧 **Durum: aktif geliştirme.** Yazarın kendi kullanımında çalışır; arayüzler değişebilir.

# karinca-kokpit-mvp

Bir "kokpit"in küçük Windows prototipi: ekranda duran kapsülden **dairesel seçim çarkı** açılır (nişancı oyunlarındaki silah seçme çarkı gibi); istediğin araç veya klasörü tek fare hareketi ve tıklamayla seçersin. Büyük bir fikrin ilk, bilerek at-ve-unut denemesidir: asıl işe başlamadan önce kurulum ve "bunu nereye koymuştum" yükünü azaltmak.

Arayüz Türkçedir. İngilizce ana belge: [README.md](README.md).

## Ne yapar

- Ekranda küçük bir kapsül durur. Üzerine gelip tıklayarak ya da `Ctrl+Alt+K` ile çarkı açarsın; `Esc`, ortaya tıklamak veya fareyle uzaklaşmak kapatır.
- Dilimler: **Notlarım** (çalışma klasöründe not dosyası hazırlar, Not Defteri'nde açar), **Araçlarım** (eklediğin programların ikinci çarkı; çoksa sayfalanır), **Çalışma alanı**, **Araç ekle**, **Düzenle** (yönetim paneli). **Dikte** dilimi var ama henüz bağlı değil; yanlışlıkla çalışmaz.
- Kendi programını veya kısayolunu eklersin; araç **uyutulabilir** (gizlenir, hazırlığı korunur) ve geri getirilir. Araçlar yalnız senin açık tıklamanla başlar; kabuk komutu birleştirilmez.
- Koyu ve "kâğıt" açık tema, kapsül konumu hatırlanır, klavye gezintisi (oklar/Tab, Enter, Esc).
- Yalnız yerel: ağ yok, hesap yok, kurulu programları taramaz. Durum tek JSON dosyasıdır (`data/kokpit.json`), atomik yazılır ve yedeklenir; bozuk dosya silinmez, ayrılır. Not dosyası `CreateNew` ile açılır; mevcut yazı ezilmez. Çıkış yalnız kokpiti kapatır, başlattığı programlara dokunmaz.

## Hızlı başlangıç

Gereksinim: Windows 10/11 ve yerleşik .NET Framework 4 C# derleyicisi (`csc.exe`, C# 5). Hiçbir şey indirilmez.

```bat
build.cmd
build\KarincaCark.exe
```

Yerleşik kontroller (çıkış kodu 0 = hepsi geçti): `--check` (26 çekirdek), `--wheel-check` (32 çark), `--context-check` (10 pencere yaşam döngüsü), `--ui-check` (17 panel/olay), `--render-wheel "<tam klasör>"` (çarkı PNG'ye çizer).

## Sınırlar

- **Prototiptir, ürün değil.** Teknoloji (WinForms, C# 5) ilk bakış hızı için seçildi, kalıcı karar değildir; fikrin sonraki çalışması burada yayımlanmayan başka bir kod tabanına geçti. Kurulum paketi, imza, otomatik güncelleme, telemetri yok.
- Çark özel çizimdir; dilimlerin ekran okuyucu (UI Automation) öğeleri henüz yok. Klavye gezintisi var ama bu erişilebilirlik iddiası değildir.
- DPI: yerleşim ölçek katsayılarıyla sınandı, farklı DPI'lı gerçek monitörlerle değil; per-monitor DPI v2 yok.
- Pencere kontrolleri test oturumunda koşar; gerçek kullanıcı masaüstü kabulü değildir. Süreç devreden paketli Not Defteri'nin penceresine dönüş kullanıcıyla denenmedi.
- Yalnız Windows 11'de denendi; Mac ve Linux sürümü yok.
- Araca özel argüman, terminal/proje seçimi ve ajana özel hazırlık yok.

## Lisans

Apache-2.0: [LICENSE](LICENSE), [NOTICE](NOTICE).
