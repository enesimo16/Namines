using Xunit;

// Bu projedeki her test GERÇEK container açıyor. Paralel çalıştırmak, aynı
// anda birkaç veritabanı konteyneri demek: AGENTS.md'nin uyarısı gereği bu
// makinede Docker Desktop'ın WSL2 arka ucunu boğabiliyor ve testler koda
// bakılmaksızın kırmızı yanıyor.
//
// GÖZLENDİ: LaunchService testleri bu projeye taşındıktan sonra
// BranchTestRunnerServiceTests tek başına geçerken paket içinde düşmeye
// başladı — kodda hiçbir değişiklik olmadan. Kırmızı, çakışmanın kendisiydi.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
