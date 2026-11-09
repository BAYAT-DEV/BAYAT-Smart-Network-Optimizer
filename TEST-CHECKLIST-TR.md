# Windows test kontrol listesi — v1.1.0

Bu sürüm için Windows cihazlarında gerçek doğrulama gereklidir. Statik XAML kontrolü, gerçek Windows derlemesinin veya donanım testinin yerine geçmez.

## Genel arayüz ve canlı izleme
- [ ] .NET 10 SDK ile win-x64 Release yayını temiz Windows 11 x64 üzerinde açılır.
- [ ] Desteklenen Windows 11 ARM64 üzerinde win-arm64 yayını açılır.
- [ ] Başlangıç taraması tamamlanır; ağ bağlantısı olmayan cihazda çökme olmaz.
- [ ] Sekmeler ve önceki bütün buton işlevleri erişilebilir kalır.
- [ ] Ethernet gateway ping'inde 1 saniyelik döngü birikmiş eşzamanlı Ping isteği oluşturmaz.
- [ ] Ping yanıtı geldiğinde grafik, son/ortalama RTT ve ardışık başarılı örnek farklarından jitter tahmini güncellenir.
- [ ] ICMP engelli gateway'de başarısız örnekler kayıp olarak görünür; uygulama kilitlenmez.
- [ ] Adaptör kapanır veya gateway değişirse uygulama hata vermeden durumu günceller.
- [ ] 60 örnekten eski değerler bellek kuyruğundan çıkar; grafik son 60 örneği gösterir.
- [ ] Durdur düğmesi yeni Ping isteği başlatılmasını engeller.
- [ ] Temizle düğmesi örnek kuyruğunu/grafiği temizler.
- [ ] CSV dışa aktarımı yalnızca kullanıcı dosya seçip onaylarsa yapılır; UTC/yerel zaman alanı tutarlı ve CSV satırları doğru biçimlidir.
- [ ] Canlı örneklerin uygulama kapanınca kalıcı dosyaya kendiliğinden yazılmadığı doğrulanır.

## Windows açılışında başlatma
- [ ] Yayınlanmış `SmartNetworkOptimizer.exe` üzerinde seçenek işaretlenince yalnızca HKCU Run girdisi oluşturulur.
- [ ] Başka kullanıcı profillerinin HKCU kayıtları değiştirilmez.
- [ ] Yönetici izni istenmez.
- [ ] Seçenek kaldırılınca yalnızca `SmartNetworkOptimizer` adlı uygulama girdisi silinir.
- [ ] Uygulama başka bir EXE tarafından/`dotnet.exe` ile çalışıyorsa başlangıç girdisi oluşturulmaz.
- [ ] Başlangıç girdisi mevcutsa yeniden açılışta checkbox işaretli görünür.
- [ ] Inno Setup ile kaldırma sırasında uygulamanın başlangıç girdisi silinir.

## Yayın ve kurulum
- [ ] `publish.ps1 -RuntimeIdentifier win-x64` tek EXE ve ZIP oluşturur.
- [ ] `publish.ps1 -RuntimeIdentifier win-arm64` ARM64 hedefinde derlenir.
- [ ] ZIP SHA256 değeri üretimden sonra bağımsız olarak doğrulanır.
- [ ] GitHub Actions her iki mimari için build artifact üretir.
- [ ] Inno Setup kullanıcı seviyesinde kurulum/kaldırma ve kısayollar test edilir.
- [ ] Authenticode imzası ve zaman damgası, kamuya açık dağıtımdan önce eklenir ve doğrulanır.
- [ ] Windows 10 Home/Pro için .NET 10 resmi destek garantisi verilmediği dokümanda ve indirme sayfasında belirtilir; Windows 10 LTSC/Enterprise cihaz testleri ayrıca tamamlanır.

---

# Windows 10/11 test kontrol listesi — v0.9.0

Bu kontrol listesi gerçek Windows donanımında çalıştırılmalıdır. Testler tamamlanmadan herkese açık dağıtım yapmayın.

## Derleme ve ilk açılış
- [ ] .NET 10 SDK ile `publish.ps1` başarıyla çalışıyor.
- [ ] WPF uygulaması Windows 10/11 x64 üzerinde açılıyor.
- [ ] İlk açılışta sistem taraması tamamlanıyor; ağ adaptörü bulunmayan/kapalı cihazlarda uygulama çökmeden rapor üretiyor.
- [ ] Yönetici izni olmadan donanım ve NIC özellik analizi çalışıyor.

## NIC analizi
- [ ] Intel Ethernet adaptöründe bağdaştırıcı adı, açıklaması, link ve sürücü bilgisi doğru görüntüleniyor.
- [ ] Realtek Ethernet adaptöründe doğru mevcut değerler ve ValidDisplayValues listeleniyor.
- [ ] Wi-Fi adaptöründe Ethernet'e özel EEE değerlendirmesi uygulanmıyor.
- [ ] Sanal/VPN adaptör seçildiğinde öneriler fiziksel adaptör varsaymıyor; kullanıcı seçimini/aktif bağdaştırıcıyı doğruluyor.
- [ ] Sürücü bilinmeyen veya yerelleştirilmiş özellik adları döndürdüğünde bilinmeyen değerler değiştirilmeden listeleniyor.
- [ ] RSS kapalı, 4+ mantıksal işlemci ve sürücünün `Enabled` seçeneğini sunduğu test cihazında yalnızca A/B adayı gösteriliyor.
- [ ] RSS zaten etkinse mevcut durumun korunması öneriliyor.
- [ ] Interrupt Moderation, LSO, checksum offload, Flow Control ve buffer seçeneklerinde varsayılanı koruma uyarısı görüntüleniyor.
- [ ] Analiz sırasında hiçbir NIC özelliği değişmiyor; ağ bağlantısı kesilmiyor; Registry yazımı olmuyor.

## Oyun ve ölçüm
- [ ] Aynı oyun sunucusu/hedefiyle en az üç adet önce/sonra ölçüm kaydediliyor.
- [ ] Ölçümler aynı bağlantı türünde, benzer ağ yükünde ve aynı örnek sayısında alınıyor.
- [ ] ICMP kaybının UDP oyun kaybıyla aynı olmadığı kullanıcıya hatırlatılıyor.

## DNS güvenliği
- [ ] Kullanıcı onayı olmadan DNS değişikliği yapılmıyor.
- [ ] Mevcut DNS yedeği korunuyor ve geri yükleme doğrulanıyor.
- [ ] Kurumsal/VPN DNS kullanan sistemlerde kullanıcı uyarıları inceleniyor.

## Sonuç kaydı
- [ ] Windows sürümü, ağ kartı modeli, sürücü sürümü/tarihi, bağlantı türü ve test sonuçları rapora ekleniyor.
- [ ] Başarısız testlerde hata mesajı ve işletim sistemi olay kayıtları saklanıyor.


## NIC değişiklik / geri alma — v0.7.0
- [ ] Wi-Fi veya `Get-NetAdapter -Physical` listesinde olmayan sanal bağdaştırıcıda RSS ayarı değişikliği önerilmediği ve uygulanmadığı doğrulanır.
- [ ] 4'ten az mantıksal işlemcili sistemde RSS adayı üretilmez.
- [ ] RSS zaten etkinse değişiklik adayı üretilmez.
- [ ] Sürücü standart `*RSS` RegistryKeyword, `Enabled` seçeneği veya eşleşen `ValidRegistryValues` bildirmiyorsa değişiklik yapılmaz.
- [ ] Aday sistemde uygulama öncesi `RegistryValue` mevcut değerle eşleşiyorsa yedek dosyası oluşturulur ve UAC isteği gelir.
- [ ] UAC reddedilirse ayar değişmez ve gereksiz NIC yedeği temizlenir.
- [ ] Kullanıcı onayı olmadan NIC ayarı değişmez.
- [ ] `nic-backup.json` doğru InterfaceGuid, RegistryKeyword, eski/yeni RegistryValue değerlerini içerir.
- [ ] Uygulama sırasında sürücüye ait geçerli olmayan hedef değer reddedilir.
- [ ] `-NoRestart` nedeniyle uygulama adapter'ı otomatik yeniden başlatmaz; kullanıcıya olası restart gereksinimi bildirilir.
- [ ] Uygulama sonrası RegistryValue tekrar okunarak hedefle eşleştiği doğrulanır.
- [ ] Doğrulama başarısızsa otomatik geri alma denenir; başarısız olursa yedek saklanır.
- [ ] Normal geri yüklemede mevcut değer hedef değerden farklıysa dış değişikliğin üstüne yazılmaz ve yedek korunur.
- [ ] Normal geri yükleme başarılı olduğunda eski RegistryValue tekrar okunarak doğrulanır ve yalnızca başarıda yedek silinir.
- [ ] NIC ve DNS yedekleri birbirinden bağımsızdır; birinin işlemi diğerinin yedeğini silmez.
- [ ] Intel, Realtek ve en az bir farklı OEM driver üzerinde Windows 10/11 testleri yapılır.
- [ ] Önce/sonra gecikme ve kayıp ölçümü aynı sunucu/hedef, aynı bağlantı, benzer ağ yükü ve benzer test süresiyle alınır.


## A/B performans karşılaştırması — v0.8.0
- [ ] Alan adı girildiğinde A aşamasında tek IP'ye çözülür; B aşaması JSON'daki aynı IP adresini kullanır.
- [ ] IP literal girildiğinde DNS çözümlemesi yapılmadan hedef aynen kullanılır.
- [ ] 20 ICMP örneği kaydedilir; timeout/erişilemeyen hedefler başarısız örnek ve kayıp olarak sayılır.
- [ ] Min/ortalama/medyan/P95 RTT, jitter tahmini ve kayıp yüzdesi ham örneklerle tutarlıdır.
- [ ] A/B aşamaları arasında aktif adaptör kimliği değiştiğinde B ölçümü reddedilir.
- [ ] Sunucu alanı A aşamasından sonra değiştirilirse B ölçümü reddedilir.
- [ ] A/B oturumu uygulama yeniden başlatıldığında JSON'dan yüklenebilir.
- [ ] Bozuk/elle değiştirilmiş A/B JSON dosyası güvenli hata verir; uygulama çökmez.
- [ ] RSS uygulama düğmesi, A aşaması oturumu yoksa, farklı adaptöre aitse veya A/B zaten tamamlandıysa çalışmayı reddeder.
- [ ] B ölçümü öncesi NIC ayarının etkinleşmesi için restart gerekebileceği uyarısı gösterilir.
- [ ] Yanıt oranı %75'in altındaysa karşılaştırma 'yetersiz ölçüm' olarak sınıflandırılır.
- [ ] Belirgin fark yokken program iyileşme iddiasında bulunmaz.
- [ ] TXT rapora A/B karşılaştırma çıktısı eklenebilir.
- [ ] A/B oturumunu sıfırlama kullanıcı onayı ister ve DNS/NIC yedeklerine dokunmaz.
- [ ] VPN/çoklu yönlendirme, ICMP engeli, değişken oyun sunucusu ve arka plan yükü senaryolarında kullanıcıya ölçüm sınırları gösterilir.
- [ ] Aynı hedef/benzer saat/ağ yükü ile en az 3 A/B çifti gerçek Windows 10/11 sisteminde tekrarlanır.


## v0.9.0 testleri — Windows üzerinde yapılmalı

- [ ] Akıllı Profil ve Öneriler, sistem taramasından önce çalıştırılamıyor.
- [ ] Wi-Fi bağlantıda Ethernet'e özel RSS yazma adayı oluşturulmuyor.
- [ ] Aktif bağdaştırıcı yeniden taramayla değişince eski NIC sürücü envanteri geçersiz sayılıyor.
- [ ] Gateway ölçümü yoksa plan bunu eksik kanıt olarak belirtiyor.
- [ ] Gateway ICMP kaybı, UDP oyun paket kaybı olarak sunulmuyor.
- [ ] Tam A/B oturumu yokken tamamlanmış optimizasyon kazanımı iddia edilmiyor.
- [ ] A/B olumsuzsa NIC yedeği mevcut olduğunda geri yükleme düğmesi öneriliyor.
- [ ] A/B karşılaştırması aynı SessionId ile iki kez çalıştırıldığında geçmişte bir kayıt tutuluyor.
- [ ] Yerel geçmişte hedef hostname/IP, adaptör adı/GUID, SSID veya ham ping örneği bulunmuyor.
- [ ] Yerel geçmiş 100 kaydı aşmıyor; en eski kayıtlar düşüyor.
- [ ] Geçmişi silme onayı reddedilirse dosya silinmiyor.
- [ ] Geçmişi silmek DNS/NIC backup dosyalarına dokunmuyor.
- [ ] JSON bozuk veya şema sürümü bilinmiyorsa kullanıcıya hata gösteriliyor, sessizce üzerine yazılmıyor.
- [ ] Windows 10 ve Windows 11 üzerinde Release derlemesi ve paket açılış testi yapılıyor.
- [ ] Ethernet / Wi-Fi, Intel / Realtek / Killer sürücülerinde salt okunur NIC envanteri deneniyor.

Not: Bu geliştirme ortamında .NET SDK/Windows PowerShell bulunmadığından .NET derlemesi ve bu Windows test maddeleri çalıştırılmadı. Derleme/gerçek donanım testleri, Windows 10/11 sisteminde yapılmalıdır.


## v1.1.0 ek kabul kontrolleri

- [ ] Ana pencere ve iletişim kutusu başlıkları `BAYAT` ile başlıyor.
- [ ] Ana marka başlığı, beş sekme başlığı, metrik ve kart başlıkları `BAYAT` ile başlıyor.
- [ ] Durum çubuğunun başlığı ve rapor alanındaki bölüm başlıkları `BAYAT` ile başlıyor.
- [ ] Windows 10 Home/Pro sistem raporunda destek yaşam döngüsü uyarısı görünüyor.
- [ ] Windows 11 sisteminde Windows 10 uyarısı görünmüyor.
- [ ] Windows 10 Enterprise/LTSC için sürüm yaşam döngüsü doğrulama notu görünüyor.
- [ ] GitHub tag release akışı iki mimarinin ZIP ve SHA256 varlıklarını yayımlıyor.
