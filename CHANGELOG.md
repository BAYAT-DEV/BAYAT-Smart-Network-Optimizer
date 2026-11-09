# Değişiklik Günlüğü

## v1.1.0 — BAYAT arayüzü ve platform uyumluluğu

- Pencere başlığı, ana marka başlığı, sekmeler, kart/metric başlıkları ve durum başlığı `BAYAT` önekiyle güncellendi.
- Ürün sürümü, paketleme betiği ve Inno Setup tanımı 1.1.0 olarak yükseltildi.
- Windows 10 Home/Pro için Microsoft destek durumu hakkında salt okunur uyumluluk uyarısı eklendi; uygulama başlangıcı engellenmez.
- Windows 10 Enterprise/LTSC cihazlar için sürüm yaşam döngüsünün ayrıca doğrulanması gerektiği raporlanır.
- Tag ile tetiklenen GitHub Actions akışına release ZIP ve SHA256 dosyalarını GitHub Release'e ekleme adımı eklendi.
- Yerel analiz, yedekleme, onay ve geri alma davranışları değiştirilmedi.


## 1.0.0 — İlk genel sürüm adayı

- Eski işlevler korunarak arayüz genel bakış, bağlantı/oyun, optimizasyon, A/B testi ve raporlar sekmelerine ayrıldı.
- Genel bakışta varsayılan ağ geçidine saniyede bir ICMP örneği gönderen isteğe bağlı canlı izleme eklendi.
- Son 60 örnek için gecikme grafiği, son RTT, ortalama RTT, örnek kaybı ve örnek sayısı göstergeleri eklendi.
- Canlı ölçümleri kullanıcı seçerse CSV olarak dışa aktarma eklendi; ham ölçümler ağ üzerinden gönderilmez ve uygulama belleğinde tutulur.
- Windows açılışında başlatma tercihi eklendi. Yalnızca mevcut Windows kullanıcı hesabının `Run` anahtarına yazılır, yönetici izni istemez ve kullanıcı kapatabilir.
- Açılış girdisi yalnızca yayınlanmış `SmartNetworkOptimizer.exe` sürecinden kaydedilebilir; geliştirme ortamında yanlışlıkla `dotnet.exe` başlangıç girdisi oluşturulmaz.
- Win-x64 ve win-arm64 self-contained tek dosya yayın betiği ve GitHub Actions iş akışı eklendi.
- İsteğe bağlı, kullanıcı seviyesinde Inno Setup kurulum betiği eklendi. Kurulum yöneticilik istemez; ağ yapılandırma değişiklikleri uygulama içindeki onay/UAC akışına tabidir.
- Sürüm numarası 1.0.0 olarak güncellendi.

### Canlı izleme sınırları

- Grafik varsayılan ağ geçidine ICMP ölçer; oyun sunucusu gecikmesi veya UDP oyun kaybı değildir.
- Bazı ağ geçitleri ICMP yanıtlarını kısıtlayabilir. Zaman aşımı doğrudan internet kesintisi anlamına gelmez.
- Canlı örnekler yalnızca uygulama çalışırken bellekte tutulur. CSV yalnızca kullanıcı dışa aktarımı başlatırsa oluşturulur.

### Doğrulama

- XAML, proje, manifest ve workflow XML/YAML yapıları statik olarak kontrol edilmelidir.
- Bu geliştirme ortamında .NET SDK/Windows bulunmadığından Windows derlemesi, uygulama çalıştırması ve gerçek ağ kartı testi yapılmamıştır. Bu sürüm henüz imzalı ya da doğrulanmış ikili dağıtım değildir.

## 0.9.0

- Akıllı karar motoru, kanıt kapsamı ve gizlilik dostu yerel performans geçmişi.
- Son 100 tamamlanmış A/B karşılaştırmasını yerel olarak tutar.
- Geçmişte hedef IP/alan adı, adaptör GUID/adı, SSID veya ham ICMP örnekleri saklanmaz.

## 0.8.0

- Aynı çözümlenmiş IP'ye A/B performans ölçümü, 20 ICMP örneği, RTT/jitter/kayıp istatistikleri.
- Ölçüm oturumunu yerel JSON olarak saklama ve A/B karşılaştırma raporu.

## 0.7.0

- Sürücü tarafından sunulan doğrulanmış RSS seçeneği için sınırlı, yedekli, kullanıcı onaylı A/B değişikliği.
- Fiziksel Ethernet ve donanım uygunluğu doğrulanır; otomatik geri alma ve ayrı geri yükleme akışı.

## 0.6.0

- Ağ kartı sürücüsü gelişmiş özelliklerinin salt okunur envanteri ve donanıma özel öneriler.

## 0.5.0

- Oyun süreçlerini ve TCP/UDP yerel bağlantı uç noktalarını salt okunur tanıma.

## 0.4.0

- Oyun hedefi ping, jitter/kayıp tahmini, IPv4 traceroute ve isteğe bağlı TCP bağlantı testi.

## 0.3.0

- DNS sağlayıcı yanıt sürelerini karşılaştırma; IPv4/IPv6 DNS yedekleme ve geri yükleme.

## 0.2.0

- Ağ/donanım analiz raporları ve güvenli öneriler.

## 0.1.0

- İlk Windows WPF prototipi.
