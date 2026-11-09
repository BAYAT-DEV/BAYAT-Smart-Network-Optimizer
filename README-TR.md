# BAYAT Smart Network Optimizer — v1.1.0
## v1.1.0 — BAYAT arayüzü ve Windows uyumluluk notu

- Arayüzdeki ana başlık, sekme başlıkları, metrik/kart başlıkları, iletişim kutusu başlıkları ve durum başlığı `BAYAT` önekiyle gösterilir.
- Windows 10 Home/Pro algılandığında sistem raporuna Microsoft destek yaşam döngüsü uyarısı eklenir. Uyarı uygulamayı engellemez.
- Tag ile GitHub Actions çalıştırıldığında win-x64 ve win-arm64 ZIP paketleri ile SHA256 dosyaları GitHub Release varlıkları olarak yayımlanabilir.

### Windows 10 desteği hakkında önemli not

Microsoft'un güncel .NET Windows kurulum tablosu, modern .NET'in Windows 10 desteğini LTSC ve Enterprise sürümleriyle sınırlar. Windows 10 Home/Pro için genel destek Ekim 2025'te sona ermiştir. Bu nedenle proje .NET 10 ile Windows 11 ve uygun yaşam döngüsündeki Windows 10 Enterprise/LTSC sürümlerini hedefler; Windows 10 Home/Pro için resmî destek veya doğrulanmış uyumluluk iddiasında bulunmaz. Uygulama bu sistemlerde çalışsa bile bu kombinasyon destek dışıdır ve kullanıcı kendi risk değerlendirmesiyle kullanmalıdır. Kaynak: [Microsoft — Windows'a .NET yükleme](https://learn.microsoft.com/tr-tr/dotnet/core/install/windows), [Windows 10 Home ve Pro yaşam döngüsü](https://learn.microsoft.com/en-us/lifecycle/products/windows-10-home-and-pro).


## Uygulama özellikleri

- Önceki sürümlerin analiz, DNS, NIC, oyun profili, A/B benchmark ve yerel geçmiş özellikleri korunarak WPF arayüzü beş sekmeli bir çalışma alanına dönüştürüldü: Genel Bakış, Bağlantı ve Oyun, Optimizasyon, A/B Test ve Geçmiş, Raporlar.
- **Canlı ağ geçidi izlemesi:** Kullanıcı başlatırsa varsayılan ağ geçidine saniyede en fazla bir ICMP örneği gönderilir. Son 60 örnek çizgi grafiğinde gösterilir; son RTT, başarılı örneklerin ortalaması, bu örnek penceresindeki başarısızlık oranı ve örnek sayısı hesaplanır.
- Canlı grafik yalnızca yerel ağ geçidi ICMP yanıtlarını ölçer. Oyun sunucusunun gecikmesi, UDP oyun kaybı veya ISS performans puanı değildir. Bazı ağ geçitlerinin ICMP'yi sınırlaması nedeniyle timeout tek başına internet bağlantısının kesildiği anlamına gelmez.
- Canlı örnekler uygulama belleğinde tutulur; uygulama kapandığında silinir. Kullanıcı isterse zaman damgası, başarılı/başarısız durumu, RTT ve yanıt durumunu CSV olarak dışa aktarabilir. Otomatik telemetri/ağ üzerinden gönderim eklenmemiştir.
- İsteğe bağlı **Windows açılışında başlat** seçeneği yalnızca geçerli kullanıcı profili için `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` girdisini yönetir. Yönetici izni istemez. Yalnızca yayınlanmış `SmartNetworkOptimizer.exe` üzerinden etkinleştirilebilir; kaldırma betiği bu başlangıç girdisini temizler.
- `publish.ps1` ile self-contained, tek dosyalı win-x64 veya win-arm64 yayın hazırlanabilir. GitHub Actions workflow'u aynı iki hedef için Windows runner üzerinde build artifact üretir.
- `installer/SmartNetworkOptimizer.iss`, win-x64 paketi için isteğe bağlı Inno Setup kullanıcı seviyesinde kurulum tanımıdır. Inno Setup ayrı yüklenmelidir; bu kaynak paketinde hazır kurulum EXE'si bulunmaz.

### Platform ve destek notu

- Proje WPF ve .NET 10 LTS kullanır. Microsoft'un güncel .NET 10 Windows destek tablosunda Windows 11'in desteklenen sürümleri yer alır; Windows 10 desteği ise LTSC/Enterprise sürümleriyle sınırlıdır. Windows 10 Home/Pro (genel tüketici sürümleri) için bu .NET 10 hedefinde resmi destek garantisi verilmez. Geniş Windows 10 tüketici desteği isteniyorsa ayrı bir .NET Framework 4.8 uyumlu hedef ve gerçek cihaz testleri gerekir.
- .NET 10 LTS, Microsoft destek tablosunda Kasım 2028'e kadar desteklenen sürüm olarak listelenmektedir. SDK/Windows matrisi ve destek politikası yayın öncesi tekrar kontrol edilmelidir.

### v1.1.0 canlı izleme nasıl kullanılır?

1. **Sistemi Tara** ile aktif bağdaştırıcıyı ve varsayılan ağ geçidini belirleyin.
2. Genel Bakış sekmesinde **İzlemeyi Başlat** düğmesine basın. Program yerel ağ geçidine saniyede yaklaşık bir ICMP isteği gönderir.
3. Son 60 örneğin grafiğini ve ölçüm kartlarını izleyin. Kırmızı işaret, bu örnekte ICMP yanıtı alınamadığını gösterir.
4. İsterseniz **Canlı Ölçümleri CSV Kaydet** ile son örnekleri yerel dosyaya kaydedin.
5. **Durdur** ile ölçümü bitirin; **Temizle** yalnızca mevcut bellekteki grafiği siler.
6. Windows açılışında başlatma seçeneğini yalnızca yayınlanmış EXE kullanılırken açın; aynı onay kutusuyla kapatabilirsiniz.

### Yayın oluşturma

Windows üzerinde .NET 10 SDK kurulduktan sonra PowerShell'de proje klasöründen:

```powershell
.\publish.ps1 -RuntimeIdentifier win-x64
```

ARM64 Windows cihazı için:

```powershell
.\publish.ps1 -RuntimeIdentifier win-arm64
```

Çıktılar `artifacts/<runtime>/` klasörüne yazılır; ayrıca ZIP ve SHA256 dosyası oluşturulur. İsteğe bağlı installer için Inno Setup kurup `installer/SmartNetworkOptimizer.iss` dosyasını Inno Setup Compiler ile açın. GitHub deposunda **Actions → Windows build → Run workflow** ile de win-x64/win-arm64 build artifact'ları üretilebilir.

**Yayın öncesi gereklilik:** Kod imzalama sertifikası bu pakete dahil değildir. Dağıtım öncesinde EXE/installer Authenticode ile imzalanmalı; temiz Windows 11 x64/ARM64 sistemlerinde ve desteklenen Windows 10 LTSC/Enterprise sürümlerinde kurulum, yükseltme, açılış girdisi, ağ analizi ve geri alma testleri tamamlanmalıdır.

---

# BAYAT Smart Network Optimizer — Geliştirme sürümü (0.9.0)

## 0.9.0 özellikleri

- Akıllı karar motoru; aktif adaptör türü/durumu, link hızı, gateway ve uzak hedef ICMP ölçümleri, sürücü gelişmiş özellik envanteri ve A/B oturumunu tek bir kişiye özel karar planında birleştirir.
- Wi-Fi/Ethernet bağlantı yapısı, düşük Ethernet link hızı, gateway yanıt kaybı/gecikmesi, eksik sürücü analizi, RSS uygunluğu ve A/B sonucuna göre öncelikli öneriler oluşturur.
- **Kanıt kapsamı** yüzdesi gösterilir. Bu bir ping, hız veya internet kalitesi puanı değildir; eldeki donanım/ölçüm verisinin karar üretmek için ne kadar tamam olduğunu gösterir.
- Her tamamlanmış A/B karşılaştırması `%LOCALAPPDATA%\SmartNetworkOptimizer\performance-history.json` konumuna özet kayıt olarak eklenir. Aynı oturum kimliği ikinci kez yazılmaz; en yeni 100 kayıt saklanır.
- Yerel geçmiş, hedef IP/alan adı, adaptör adı/GUID, SSID veya ham ping örneklerini içermez. Geçmişi arayüzden görüntüleyebilir ve kullanıcı onayıyla silebilirsiniz. Silme işlemi DNS/NIC geri yükleme yedeklerini etkilemez.
- Karar motoru salt okunurdur; öneri üretirken DNS veya NIC ayarını değiştirmez. RSS gibi yazma işlemleri önceki sürümdeki ayrı kullanıcı onayı/yedek/UAC akışıyla yapılır.

## 0.8.0 özellikleri

- Önce/sonra A/B performans oturumu: kullanıcı oyun sunucusu IP/alan adını bir kez girer; program alan adını başlangıçta tek IP'ye çözer ve A/B ölçümlerinde aynı IP'yi kullanır.
- Her aşamada 20 ardışık ICMP örneği; başarılı yanıt sayısı, kayıp yüzdesi, minimum/ortalama/medyan/P95 RTT ve arka arkaya gelen başarılı probelar üzerinden jitter tahmini.
- Ölçümlerin JSON oturum dosyası olarak `%LOCALAPPDATA%\SmartNetworkOptimizer\ab-benchmark.json` konumunda saklanması; raporun mevcut TXT dışa aktarma düğmesiyle kaydedilmesi.
- Ölçüm oturumu aktif bağdaştırıcı kimliğine bağlanır. RSS ayarı uygulanmadan önce aynı adaptöre ait Aşama A ölçümü zorunludur.
- A/B karşılaştırmasında ölçüm yetersizliği, belirgin fark olmaması, olumlu/olumsuz değişim için ihtiyatlı eşikler.
- A/B oturumunu kullanıcı onayıyla yenileme/silme.

### A/B testi nasıl kullanılır?

1. Oyunu açın ve mümkünse gerçek maç/oturum sunucusunun IP adresini oyun içi ağ istatistiğinden alın. Bu adresi üstteki **Sunucu IP / alan adı** alanına yazın. Rastgele DNS, web sitesi veya CDN adresini oyun sunucusu sanmayın.
2. **Aşama A: Ön Ölçüm** düğmesine basın. Program hedefi bir kez çözer, seçilen IP'yi ve aktif bağdaştırıcı kimliğini oturuma kaydeder, 20 ICMP örneği alır.
3. Bir seferde yalnızca tek bir ayar değiştirin. RSS testi yapacaksanız **Uygun RSS Testini Uygula** düğmesi Aşama A ölçümünü zorunlu tutar; onay ve UAC olmadan değişiklik yapılmaz.
4. Sürücü/adaptör yeniden başlatması gerekiyorsa tamamlayın. Ardından uygulamayı yeniden açıp **Sistemi Tara** ile aynı bağlantının aktif olduğunu doğrulayın; sunucu alanını A aşamasındaki değerle aynı tutun.
5. **Aşama B: Son Ölçüm** düğmesi A aşamasında kaydedilen aynı IP'ye 20 örnek daha gönderir.
6. **A/B Sonuçlarını Karşılaştır** ile medyan/ortalama/P95 RTT, jitter tahmini ve kayıp farklarını görüntüleyin. Raporu **Raporu Kaydet** düğmesiyle dışa aktarabilirsiniz.
7. Sonuç belirgin biçimde kötüleşirse NIC yedeğini geri yüklemeyi değerlendirin. Bir sonraki deneme için A/B oturumunu sıfırlayıp yeni A ölçümü alın.

**Ölçüm uyarıları:** Tek A/B çifti performans farkının değişiklikten kaynaklandığını kanıtlamaz. Aynı oyun sunucusunda, benzer saatte/ağ yükünde en az üç ayrı A/B çifti alın. ICMP yanıt kaybı UDP oyun kaybıyla aynı değildir. VPN veya özel rota kullanıyorsanız Windows hedefe farklı bir fiziksel arayüz üzerinden çıkabilir; adaptör kimliğinin aynı kalması rota yolunun da aynı kaldığını garanti etmez. Ping ölçümü sırasında indirme/yükleme yapmayın. Alan adı A aşamasında bir kez çözümlenir; sonraki B testi aynı IP'yi kullanır.

Windows 11 ve desteklenen Windows 10 Enterprise/LTSC sürümleri için WPF tabanlı ağ tanılama ve güvenli optimizasyon uygulaması. Bu paket henüz imzalı/kurulabilir bir EXE değildir; kaynak koddur. Bu çalışma ortamında Windows/.NET derleyicisi bulunmadığı için uygulamanın Windows üzerinde derlenmesi ve gerçek ağ bağdaştırıcılarında test edilmesi gerekir.


## 0.7.0 özellikleri

- Ağ kartı sürücüsünün gelişmiş özellikleri artık hem görüntülenen hem ham `RegistryValue` değerleriyle okunur; `ValidDisplayValues` ile `ValidRegistryValues` eşlemesi sürücüden alınır.
- Tek bir kontrollü, geri alınabilir yazma adayı sunulur: yalnızca Ethernet bağdaştırıcısında, en az 4 mantıksal işlemci varsa, RSS mevcutta kapalıysa ve sürücü tam olarak bir `Enabled` seçeneği ile bunun karşılık gelen ham Registry değerini bildiriyorsa RSS etkinleştirme A/B adayı gösterilir.
- Yalnızca `Get-NetAdapter -Physical` ile fiziksel olduğu doğrulanan Ethernet bağdaştırıcıları değerlendirilir. Wi-Fi, sanal/VPN bağdaştırıcı, belirsiz özellik eşlemesi, zaten etkin RSS veya eksik geçerli değer listesi durumunda değişiklik yapılmaz.
- Kullanıcı uygulamayı ayrıca onaylar; Windows UAC yönetici izni istenir. Önceki değer `%LOCALAPPDATA%\SmartNetworkOptimizer\nic-backup.json` dosyasına atomik olarak kaydedilir.
- Uygulama öncesinde mevcut değer tekrar okunur. Analizden sonra dışarıdan değişmişse işlem durdurulur. Hedef değer sürücünün `ValidRegistryValues` listesinden kontrol edilir.
- `Set-NetAdapterAdvancedProperty -NoRestart` kullanılır; uygulama ağ kartını kendi kendine yeniden başlatmaz. Sürücü değişikliğin etkili olması için adapter/Windows yeniden başlatması isteyebilir.
- Uygulama sonrası sürücü değeri tekrar okunarak doğrulanır. Hata veya doğrulama başarısızlığında otomatik geri alma denenir; geri alma başarısızsa yedek korunur.
- Ayrı **Önceki Ağ Kartı Ayarını Geri Yükle** düğmesi, uygulama sonrasında Registry değeri beklenen hedef değerdeyse önceki değeri geri yazar ve yeniden okuma ile doğrular. Dışarıdan değiştirilmiş değer üzerine yazılmaz; yedek tutulur.
- Bu sürüm sadece RSS için yazılabilir A/B testi sunar. Interrupt Moderation, EEE, LSO, Checksum Offload, Flow Control ve RX/TX buffer değerleri salt okunur öneri olarak kalır.

## 0.6.0 özellikleri

- Aktif ağ bağdaştırıcısının NetAdapter sürücü bilgilerini, medya türünü, link durumunu ve link hızını salt okunur alır.
- `Get-NetAdapterAdvancedProperty -AllProperties` ile sürücünün bildirdiği tüm gelişmiş özellikleri ve geçerli görüntülenen seçenekleri listeler.
- Donanım/sürücü özelliklerine göre RSS, Interrupt Moderation, Energy Efficient/Green Ethernet, Large Send Offload, Checksum Offload, Flow Control ve RX/TX buffer alanlarında temkinli öneriler üretir.
- RSS için yalnızca işlemci sayısı, sürücüdeki mevcut durum ve sürücünün bildirdiği geçerli seçenekler dikkate alınır; değişiklik yalnızca kontrollü A/B testi adayı olarak raporlanır.
- Interrupt Moderation, LSO, checksum offload, Flow Control ve buffer değerleri için evrensel oyun tweak'i önermez; sürücü varsayılanını korumayı esas alır.
- Ethernet enerji tasarrufu özellikleri için yalnızca ölçülmüş gecikme sıçraması varsa kontrollü A/B testi önerir; Wi-Fi adaptörüne Ethernet EEE önerisi vermez.
- Bu sürüm hiçbir NIC gelişmiş özelliğini değiştirmez, Registry yazmaz ve ağ sürücüsünü yeniden başlatmaz.

## 0.5.0 özellikleri

- Valorant, Counter-Strike 2, PUBG ve Fortnite oyun süreçlerini yerel Windows süreç listesinden salt okunur biçimde tanır.
- Oyun süreci adı, PID, yaklaşık çalışma belleği ve erişilebiliyorsa çalıştırılabilir dosya yolunu gösterir.
- Windows `Get-NetTCPConnection` ile tespit edilen oyun süreçlerine ait etkin TCP uç noktalarını listeler.
- Windows `Get-NetUDPEndpoint` ile oyun süreçlerine ait yerel UDP adresi/portu bilgilerini listeler.
- Tek oyun ailesi tespit edilirse ilgili oyun profilini otomatik seçer; birden fazla oyun açıksa mevcut seçimi korur.
- Süreç/bağlantı envanteri yalnızca kullanıcı düğmeye bastığında çalışır; ağ ayarlarını değiştirmez ve veri dışarı göndermez.
- TCP uzak uçları otomatik oyun sunucusu olarak kabul edilmez. UDP yerel uç nokta API'si uzak eş IP'sini sağlamadığı için program yanlış hedefi otomatik seçmez.

## 0.4.0 özellikleri

- Windows sürümü/build, işlemci, RAM, ekran kartı, disk ve ağ bağdaştırıcısı bilgilerini tarar.
- Varsayılan ağ geçidini ve DNS adreslerini listeler.
- Ağ geçidi, Cloudflare ve Google hedeflerine ping gönderir; ortalama RTT, örnek jitter ve ICMP yanıt kaybını raporlar.
- Valorant, Counter-Strike 2, PUBG, Fortnite ve özel hedef için profil seçimi sunar.
- Kullanıcı tarafından girilen gerçek oyun sunucusu IP/alan adına 15 örnek ICMP ping gönderir; ortalama RTT, kayıp ve jitter tahmini hesaplar.
- IPv4 hedeflerde TTL tabanlı traceroute dener. ICMP yanıt vermeyen ara yönlendiriciler `*` olarak görünür; bu tek başına arıza sayılmaz.
- Kullanıcı TCP portu girerse TCP bağlantı kurma süresini test eder. Bu test UDP tabanlı oyun trafiğini veya oyun içi ping'i temsil etmez.
- Windows `NetAdapter` PowerShell modülü ile ağ kartının gelişmiş sürücü özelliklerini salt okunur biçimde listeler.
- Oyun hedefi ping sonuçlarını kişiye özel öneri raporuna dahil eder.
- Sabit oyun sunucusu IP/port bilgisi varsaymaz. Maç, bölge ve sağlayıcıya göre hedef değişebildiği için gerçek hedefi oyun istemcisinden almak gerekir.
- Ağ kartı gelişmiş değerleri, TCP ayarları ve Registry ayarları bu sürümde değiştirilmez.

## Önceki sürümlerden gelen özellikler

- Cloudflare, Google ve Quad9 DNS resolver yanıt sürelerini karşılaştırır; yeterli başarılı sorgu varsa ölçümde en hızlı sonucu önerir.
- DNS profili kullanıcı onayıyla uygulanabilir. DNS ayarı öncesinde IPv4 ve IPv6 yapılandırmaları ayrı yedeklenir; ayrı ayrı geri yüklenir.
- IPv6 etkin olmayan bağdaştırıcılarda IPv6 DNS ayarına dokunulmaz.
- DNS değişikliği kısmen başarısız olursa otomatik geri alma denenir; başarısız geri alma halinde yedek korunur.
- 0.1/0.2 sürümlerinin DNS yedekleri için geriye dönük okuma bulunur.
- DNS önbelleğini temizler, sistem/ağ raporunu TXT olarak kaydeder ve aktif güç planını salt okunur raporlar.
- Kalıcı DNS değişikliğinde Windows UAC yönetici izni ister; açılışta otomatik değişiklik yapmaz.
- NIC RSS A/B değişikliği yalnızca ayrı kullanıcı onayı + UAC ile çalışır; başlangıçta otomatik değişiklik yapmaz. NIC yedeği DNS yedeğinden ayrı tutulur.

## Derleme

1. Windows 10/11 64-bit bilgisayara **.NET 10 SDK** veya uyumlu Visual Studio kurulumu yapın.
2. `src/SmartNetworkOptimizer/SmartNetworkOptimizer.csproj` dosyasını Visual Studio'da açın veya PowerShell'de proje klasörüne girin.
3. Derleyin:

```powershell
dotnet restore
dotnet build -c Release
```

Tek dosyalı, .NET çalışma zamanını içinde taşıyan x64 yayın paketi:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

`publish.ps1` betiği aynı yayın işlemini yapar. Çıktı `bin\Release\net10.0-windows\win-x64\publish` klasöründe oluşur.

## Oyun sunucusu testi nasıl kullanılır?

1. Program açıldığında sistem taramasının tamamlanmasını bekleyin.
2. Oyun profilini seçin.
3. Oyunun kendi ağ istatistiğinde/oturum bilgisinde gösterdiği gerçek sunucu IP adresini veya alan adını girin. Bir web sitesinin/CDN'nin adresini oyun sunucusu diye kullanmayın.
4. **Oyun Sunucusunu Test Et** düğmesine basın.
5. Program ICMP ping, kayıp, jitter tahmini ve IPv4 rota izini raporlar.
6. TCP portunu yalnızca hedef servisin TCP portunu biliyorsanız girin. Oyun UDP kullanıyorsa TCP testi oyun bağlantısı hakkında anlamlı bir sonuç vermeyebilir.

Oyun profili seçimi sabit oyun sunucusu IP'si varsaymaz. v0.5.0 ile süreç/bağlantı envanteri eklendi; ancak TCP uzak uçları otomatik oyun sunucusu kabul edilmez ve UDP uzak hedefleri standart yerel endpoint listesinden okunamaz. Gerçek hedefi doğrulamak için oyun içi ağ göstergesiyle karşılaştırın.

### Çalışan oyun ve bağlantı taraması

1. Valorant, CS2, PUBG veya Fortnite oyununu açın ve ana menüye/maça girin.
2. **Çalışan Oyunları ve Bağlantıları Tara** düğmesine basın.
3. Bulunan süreç adı, PID, yaklaşık RAM kullanımı ve (erişilebiliyorsa) EXE yolu raporlanır.
4. Aynı PID'ye ait etkin TCP uzak uçları ile UDP yerel portları ayrı listelenir.
5. Yalnızca tek bir oyun ailesi tespit edilirse ilgili profil otomatik seçilir.

Bu envanterdeki TCP uzak IP'leri yardımcı servis veya eşleştirme bağlantısı olabilir. UDP portları yereldir; uzak UDP sunucu IP'sini göstermez. Bu yüzden program bunlardan otomatik oyun sunucusu seçmez. Hiçbir paket içeriği yakalanmaz.

## Ağ kartı gelişmiş özellikleri

**Akıllı Ağ Kartı Analizi** düğmesi Windows `NetAdapter` modülünün `Get-NetAdapter` ve `Get-NetAdapterAdvancedProperty -AllProperties` komutlarıyla sürücü/bağdaştırıcı kimliğini, mevcut değerleri ve sürücünün bildirdiği geçerli seçenekleri salt okunur alır. RSS, Interrupt Moderation, Energy Efficient/Green Ethernet, LSO, checksum offload, Flow Control ve buffer alanlarını donanım/sürücü bağlamında değerlendirir. RSS kapalıysa, yalnızca fiziksel Ethernet + en az 4 mantıksal işlemci + standart `*RSS` anahtarı + sürücünün eşlenmiş Enabled Registry değerini sağlaması halinde ayrı düğmeden geri alınabilir A/B testi uygulanabilir. EEE yalnızca Ethernet için ve ölçülmüş boşta gecikme sorunu varsa kontrollü test adayıdır. Diğer özellikler için genel oyun tweak'i önermek yerine sürücü varsayılanını korur.

## NIC ayarını kontrollü uygulama ve geri yükleme (v0.7.0 ve sonrası)

1. Önce **Akıllı Ağ Kartı Analizi** düğmesiyle fiziksel adaptör ve sürücü özelliklerini okuyun.
2. Program yalnızca fiziksel Ethernet adaptöründe ve standart `*RSS` özelliği sürücünün izin verdiği eşlenmiş `Enabled` değerini sunuyorsa RSS adayını gösterir. Wi-Fi veya sanal adaptörlerde otomatik değişiklik adayı yoktur.
3. **Uygun RSS Testini Uygula** düğmesi ayarları yeniden okur; analizden sonra Registry değeri değişmişse işlemi durdurur.
4. Onay verirseniz eski değer `%LOCALAPPDATA%\SmartNetworkOptimizer\nic-backup.json` dosyasına kaydedilir, sonra UAC yönetici izni istenir.
5. Değer `Set-NetAdapterAdvancedProperty -NoRestart` ile yazılır ve tekrar okunarak doğrulanır. Adapter zorla yeniden başlatılmaz; değişikliğin etkinleşmesi için sürücü/Windows restart isteyebilir.
6. **Önceki Ağ Kartı Ayarını Geri Yükle** düğmesi eski değeri sürücüye yazar ve doğrular. Değer bu arada başka bir uygulama tarafından değiştirilmişse üstüne yazmaz ve yedeği korur.

Bu RSS seçimi ping düşürme garantisi değildir. Aynı oyun sunucusunda birkaç kez önce/sonra test yapın; kayıp veya gecikme kötüleşirse NIC yedeğini geri yükleyin. Sadece bir NIC ayarı aynı anda test edilir. Diğer NIC seçenekleri v0.7.0'da salt okunur kalır.


## Güvenlik ve ölçüm sınırları

- Ping sonucu yalnızca test edilen hedefe ait ICMP yanıtlarını ölçer. Oyun sunucusu ICMP'yi engelliyorsa oyun bağlantısı çalışsa bile ping ölçümü başarısız olabilir.
- ICMP paket kaybı, oyun UDP paket kaybıyla aynı şey değildir. Oyunun kendi ağ istatistiğiyle karşılaştırılmalıdır.
- Jitter tahmini ardışık başarılı ICMP RTT örneklerinin mutlak farklarının ortalamasıdır; oyun motorunun jitter metriğiyle aynı değildir.
- Traceroute ara düğümleri ICMP yanıtlarını engelleyebilir veya düşük önceliklendirebilir. `*` görmek doğrudan paket kaybı kanıtı değildir.
- TCP connect süresi yalnızca belirtilen TCP porta el sıkışma süresidir. UDP oyun paketlerini ölçmez.
- DNS değişikliği çoğunlukla alan adı çözümlemeyi etkiler; aktif oyun bağlantısının rota gecikmesini doğrudan düşürmesi beklenmemelidir.
- Sıfır paket kaybı veya belirli ping garantisi verilemez. ISS, rota, Wi-Fi paraziti, modem, sunucu ve oyun ağı koşulları etkilidir.
- DNS yedeği mevcutken ikinci DNS değişikliği engellenir; önce geri yükleme yapılmalıdır.
- Bu sürümde otomatik güncelleme, imzalı kurulum, telemetri, çevrimiçi profil/veri toplama ve Linux/macOS desteği yoktur.

## Süreç ve bağlantı envanterinin sınırları

- Süreç tanıma, bilinen çalıştırılabilir süreç adlarına dayanır. Oyun güncellemesi/launcher değişikliği nedeniyle isim değişirse süreç tanınmayabilir.
- `Get-NetTCPConnection` tarafından bulunan TCP uzak adresleri aynı PID'ye aittir, ancak bunların oyun sunucusu olduğu kanıtlanamaz; eşleştirme, ses, anti-cheat ve web servisleri de aynı süreçte bağlantı açabilir.
- `Get-NetUDPEndpoint` yalnızca yerel UDP endpoint bilgisini sağlar; UDP uzak hedefi veya paket içeriği göstermez. Bu sürümde paket yakalama/WFP/ETW tabanlı trafik izlemesi yoktur.
- Korunan/yükseltilmiş süreçlerde executable yolu veya bazı metaveriler erişilemeyebilir. Süreç taraması yönetici olarak çalıştırılmayı gerektirmez; Windows izin sınırlarına uyar.
- Süreç listesi, PID ve bağlantı metaverileri yalnızca yerel rapora yazılır; çevrimiçi hizmete/telemetriye gönderilmez.

## Sonraki geliştirme adımları

1. Windows 10/11 üzerinde gerçek derleme ve Intel/AMD, Ethernet/Wi-Fi cihaz testleri.
2. Oyun içi istatistikler ve kullanıcı tarafından doğrulanan hedeflerle TCP/UDP oturum korelasyonunun doğruluğunu artırma.
3. RSS A/B akışını Windows 10/11 Intel/Realtek Ethernet sürücülerinde test etme; sürücü/adapter yeniden başlatma gereksinimini doğrulama.
4. Gerçek oyun oturumlarında önce/sonra ölçümlerini aynı hedef ve eş koşullarda karşılaştırma.
5. Sadece güvenilir, sürücüye özgü eşleme ve test ölçümü varsa diğer tekil NIC özellikleri için ayrı ayrı aday ekleme; genel tweak listesi uygulamama.
6. Kod imzalama, kurulum paketi ve otomatik güncelleme.


## v0.8.0 A/B oturum dosyası

A/B oturumu, yerel `%LOCALAPPDATA%\SmartNetworkOptimizer\ab-benchmark.json` dosyasında tutulur. Dosya hedef adı/IP'si, adaptör kimliği/açıklaması, test zamanları ve ham ICMP RTT örneklerini içerir. Veri çevrimiçi gönderilmez veya telemetriye eklenmez. Dosyayı sıfırlamak mevcut A/B ölçümünü siler; önce TXT raporunu dışa aktarın. DNS/NIC ayar yedekleri bu oturum dosyasından bağımsızdır.

A/B sonucu ayarı otomatik geri almaz. Karşılaştırma sınıflandırması pratik bir uyarı eşiğidir; istatistiksel anlamlılık testi değildir. Jitter, başarılı ICMP örneklerinin ardışık mutlak RTT farklarının ortalamasıdır.

## Teknoloji

- C# / WPF / .NET 10
- `System.Management` ile Windows WMI donanım bilgileri
- `System.Net.NetworkInformation` ile ağ arabirimi ve ICMP ölçümleri
- `System.Net.Sockets` ile isteğe bağlı TCP bağlantı denemesi
- Windows PowerShell `NetAdapter` modülü ile NIC özelliklerini okuma ve onaylı RSS değişikliği
- DNS yapılandırması için yönetici izniyle çalışan `netsh interface ipv4/ipv6`


## v0.9.0 akıllı karar planı ve yerel geçmiş

1. Önce **Sistemi Tara** ile aktif bağdaştırıcıyı tespit edin.
2. **Ping / Jitter / Paket Kaybı Testi** ile yerel gateway ve genel test hedeflerinde ICMP ölçümü alın.
3. **Akıllı Ağ Kartı Analizi** ile seçili aktif adaptörün NetAdapter sürücü özelliklerini okuyun. Sistem yeniden taranıp aktif adaptör değişirse önceki sürücü envanteri geçersiz sayılır.
4. Oyun sunucusu IP/alan adını oyunun kendi ağ göstergesinden doğrulayın. Aynı hedefte Aşama A ölçümü alın; yalnızca desteklenen ve onaylanan tek bir NIC ayarı denenecekse uygulayın; ardından Aşama B ölçümü alın.
5. **Akıllı Profil ve Öneriler** düğmesi, mevcut kanıtlara göre öncelikli plan üretir. Kanıt kapsamı yüzdesi performans notu değildir.
6. **A/B Sonuçlarını Karşılaştır** tamamlanmış oturumun özetini yerel geçmişe kaydeder. **Yerel Performans Geçmişi** kayıtları gösterir; **Geçmişi Sil** yalnızca bu özet geçmiş dosyasını siler.

Yerel geçmiş sadece en fazla 100 A/B özeti tutar. Hedef hostname/IP, adaptör GUID/adı, SSID ve ham ping örnekleri burada saklanmaz. A/B oturum dosyası ise daha ayrıntılı ölçüm ve hedef bilgisi içerir; `%LOCALAPPDATA%\SmartNetworkOptimizer\ab-benchmark.json` dosyasını ayrı olarak yönetir. TXT raporunu dışa aktardıktan sonra A/B oturumunu arayüzden sıfırlayabilirsiniz.

Karar motoru hiçbir ayarı kendisi uygulamaz. Tek ölçüm çifti nedensellik kanıtı değildir; olumlu sonuçlar birkaç ayrı oturumda tekrarlanmalıdır. ICMP sonuçları UDP tabanlı oyun kaybı/jitter metriğiyle aynı değildir.

## Lisans ve üçüncü taraf bileşenler

Bu proje **GNU Genel Kamu Lisansı, sürüm 3 (GPL-3.0-only)** kapsamında yayımlanır.

- Lisansın tam metni proje kökündeki LICENSE.txt dosyasındadır.
- Proje telif bildirimi: Copyright (C) 2026 BAYAT.
- Kaynak dosyalarındaki SPDX-License-Identifier: GPL-3.0-only bildirimi bu lisansı belirtir.

### Üçüncü taraf bağımlılıklar

- System.Management 10.0.0: Microsoft tarafından yayımlanan, Windows Management Instrumentation (WMI) erişimi sağlayan NuGet paketidir.
- Bu bağımlılık MIT lisanslıdır. Kendi lisans koşulları ve telif bildirimleri geçerliliğini korur.
- Paket bilgileri ve lisans: https://www.nuget.org/packages/System.Management/10.0.0

Projenin GPL-3.0-only lisansı, üçüncü taraf bileşenlerin kendi lisans koşullarını ortadan kaldırmaz.
