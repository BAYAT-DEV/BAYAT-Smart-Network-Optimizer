# BAYAT Smart Network Optimizer — Kullanım Kılavuzu

**Sürüm:** 1.1.3
**Yayıncı:** BAYAT  
**Lisans:** GPL-3.0-only

## 1. İndirme

Yalnızca resmî GitHub deposunun **Releases** bölümünde yayımlanan dosyaları kullanın:

https://github.com/BAYAT-DEV/BAYAT-Smart-Network-Optimizer/releases

- **Windows x64:** `SmartNetworkOptimizer-Setup-1.1.3-win-x64.exe` kurulum paketi veya `SmartNetworkOptimizer-win-x64.zip` taşınabilir paket.
- **Windows ARM64:** `SmartNetworkOptimizer-win-arm64.zip` paketi. x64 kurulum sihirbazı ARM64 için değildir.

Dosya adları, sürüm ve mimari bilgisiyle eşleşmelidir. Başka sitelerden veya yeniden yüklenmiş dosyalardan indirmeyin.

## 2. Açık kaynak, test ve güvenlik şeffaflığı

Smart Network Optimizer; ağ bağdaştırıcısı ve bağlantı bilgilerini görüntülemek, canlı ping/RTT ölçümü yapmak, ölçüm sonuçlarını CSV olarak dışa aktarmak ve kullanıcı kontrolündeki ağ ayarı iş akışlarını sunmak için geliştirilmiştir. Uzaktan bilgisayar kontrolü veya kullanıcı parolalarını toplama, kişisel dosyaları gizlice aktarma gibi işlevler uygulamanın amaçlanan özellikleri değildir. GPL-3.0-only lisanslı açık kaynak projedir; kaynak kodunu, test projesini ve GitHub Actions iş akışını bu depoda herkes inceleyebilir.

Yayın iş akışı, Windows üzerinde otomatik testleri çalıştırır; test aşaması başarısız olursa paket üretimi/yayın adımına geçilmez. Otomatik testler belirli senaryoları kontrol eder; tek başına bağımsız, satır satır bir güvenlik denetimi anlamına gelmez ve hiçbir yazılım için tüm olası hata ve güvenlik açıklarının bulunmadığını kanıtlamaz. Açık kaynak olması, kullanıcıların kodu inceleyebilmesini ve kendi derlemelerini oluşturabilmesini sağlar.

SHA256 dosya bütünlüğü kontrolüne, GitHub Actions provenance bilgileri de derlemenin kaynağına ilişkin ek kanıt sağlar. Bunlar antivirüs taraması, bağımsız güvenlik denetimi veya Authenticode dijital imzasının yerine geçmez.

## 2. Önemli güvenlik bilgisi

Bu sürümün Windows EXE dosyaları Authenticode kod imzasıyla imzalanmamıştır. Windows SmartScreen “Windows bilgisayarınızı korudu” veya “Bilinmeyen yayıncı” uyarısı gösterebilir. “Bilinmeyen yayıncı” uyarısı, tek başına uygulamanın zararlı olduğunun tespiti değildir; Windows'un güvenilir bir yayıncı kod imzasını doğrulayamadığını bildirir.

Kullanıcıları bu uyarıyı aşmaya, “Yine de çalıştır” seçeneğini kullanmaya veya Windows güvenlik korumalarını kapatmaya yönlendirmiyoruz. Kaynak kodu, SHA256 özeti ve provenance bilgileri inceleme/doğrulama için sunulur; bunlar Authenticode imzasının veya SmartScreen itibarının yerine geçmez. Uyarı ve imza durumu hakkında karar vermeden önce bu sınırlamayı dikkate alın.

## 3. SHA256 doğrulaması

İndirdiğiniz ZIP veya kurulum dosyasının yanında aynı dosya adına ait `.sha256` dosyası yayımlanır.

PowerShell'de indirdiğiniz dosyanın bulunduğu klasöre geçin ve örneğin x64 ZIP için çalıştırın:

```powershell
Get-FileHash .\SmartNetworkOptimizer-win-x64.zip -Algorithm SHA256
```

Çıktıdaki SHA256 değerini GitHub Release sayfasındaki ilgili `.sha256` dosyasındaki değerle karakter karakter karşılaştırın. Değerler eşleşmiyorsa dosyayı kullanmayın.

## 4. Kurulum (Windows x64)

1. Resmî Release sayfasından x64 kurulum dosyasını indirin.
2. İndirme tamamlandığında dosyanın SHA256 değerini doğrulayın.
3. SmartScreen “Bilinmeyen yayıncı” uyarısı gösterebilir; bu uyarı tek başına zararlı yazılım tespiti değildir, ancak yayıncı imzasının doğrulanamadığını belirtir. Kullanıcıları uyarıyı aşmaya yönlendirmiyoruz.
4. Kaynak kodu, sürüm bilgisi, SHA256 özeti ve provenance açıklamasını inceleyerek kendi güvenlik değerlendirmenizi yapın. Windows güvenlik korumalarını kapatmayın.
5. Kurulum kullanıcı profili altındaki `%LOCALAPPDATA%\Programs\SmartNetworkOptimizer` klasörüne yapılır; yönetici izni gerektirmemesi amaçlanmıştır.
6. İsteğe bağlı masaüstü kısayolunu seçebilirsiniz.

## 5. Taşınabilir kullanım

ZIP paketini bir klasöre çıkarın ve `SmartNetworkOptimizer.exe` dosyasını bu klasörden başlatın. Paket self-contained olarak hazırlanır; .NET çalışma zamanını ayrıca kurmanız beklenmez. Yine de Windows sürümü ve işlemci mimarisi paketle uyumlu olmalıdır.

## 6. Uygulama özellikleri ve güvenli kullanım

- Sistem ve ağ bağdaştırıcısı bilgileri görüntülenebilir.
- Canlı ağ ölçümü başlatılabilir, durdurulabilir ve temizlenebilir.
- CSV dışa aktarımı kullanıcı işlemiyle yapılır.
- Ağ kartı veya DNS ayarlarını değiştiren işlemler için uygulama içindeki açıklamaları okuyun ve açık onay vermeden değişiklik uygulamayın.
- Öneriler donanım, sürücü, ağ, VPN ve servis sağlayıcısına göre değişebilir. Her bilgisayarda aynı sonucu garanti etmez.
- ICMP/ping kaybı, oyun içi UDP paket kaybıyla aynı şey değildir.
- Uygulama belirli bir gecikme düşüşü, hız artışı veya oyun performansı artışı garanti etmez.
- Windows başlangıcında çalıştırma seçeneği isteğe bağlıdır. Kapalı bırakıldığında uygulama Windows oturum açılışında otomatik başlatılmaz.

## 7. Desteklenen sistemler ve mimariler

- Paket hedefi: Windows 64-bit.
- x64 ZIP ve x64 kurulum paketi ayrı dosyalardır.
- ARM64 için ZIP paketi sağlanır; x64 kurulum paketi ARM64 kurulum dosyası değildir.
- Uygulama .NET 10 / WPF hedefler. Windows 10 Home/Pro için .NET 10 desteği ve işletim sistemi yaşam döngüsü sınırlamaları nedeniyle uyumluluk garantisi verilmez. Windows 10 LTSC/Enterprise sürümleri ayrıca değerlendirilmelidir.
- ARM64 cihazlarda gerçek donanım testi tamamlanmadan tüm cihazlarda sorunsuz çalışma garantisi verilmez.

## 8. Kaldırma

x64 kurulum paketiyle kurulduysa Windows **Ayarlar > Uygulamalar > Yüklü uygulamalar** bölümünden “BAYAT Smart Network Optimizer” uygulamasını kaldırın. Taşınabilir sürüm kullanıldıysa uygulamayı kapatıp çıkardığınız klasörü silebilirsiniz.

## 9. Gizlilik ve destek

Uygulamanın raporlarını veya CSV dosyalarını paylaşmadan önce ağ adresi, cihaz adı ve diğer kişisel/kurumsal bilgileri kontrol edin. Destek ve hata bildirimi için GitHub deposundaki Issues bölümünü kullanın:

https://github.com/BAYAT-DEV/BAYAT-Smart-Network-Optimizer/issues

## 10. Lisans

Smart Network Optimizer GPL-3.0-only lisansı altında yayımlanır. Lisans metni depodaki `LICENSE.txt` dosyasındadır. GPL lisansı, Windows kod imzalama sertifikası değildir.
