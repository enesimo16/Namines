using Namines.Core.Analysis;

namespace Namines.Tests.Analysis;

/// <summary>
/// Model maliyet çarpanının kotaya GERÇEKTEN uygulanması.
///
/// <b>Bulunan hata:</b> <see cref="NaiCatalog.CostOf"/> yalnızca testlerde
/// çağrılıyordu — üretimde ölü koddu. Yani token başına ~4 kat pahalı olan Pro
/// modelini kullanan biri, Flash kullananla AYNI kotayı ödüyordu. Kota gerçek
/// parayı yansıtmalı; aksi hâlde en pahalı modeli seçmek kullanıcı için
/// bedava, bizim için değil.
/// </summary>
public class ModelCostMultiplierTests
{
    [Fact]
    public void The_same_measured_usage_costs_more_quota_on_a_more_expensive_model()
    {
        const int measured = 10_000;

        var flash = NaiCatalog.CostOf(NaiModel.Flash, measured);
        var standard = NaiCatalog.CostOf(NaiModel.Standard, measured);
        var pro = NaiCatalog.CostOf(NaiModel.Pro, measured);

        Assert.True(flash < standard, "Flash, Standard'dan ucuz olmalı");
        Assert.True(standard < pro, "Standard, Pro'dan ucuz olmalı");
    }

    [Fact]
    public void Flash_is_half_price_and_pro_is_double()
    {
        // Çarpanlar katalogda tanımlı: 0.5 / 1.0 / 2.0.
        Assert.Equal(5_000, NaiCatalog.CostOf(NaiModel.Flash, 10_000));
        Assert.Equal(10_000, NaiCatalog.CostOf(NaiModel.Standard, 10_000));
        Assert.Equal(20_000, NaiCatalog.CostOf(NaiModel.Pro, 10_000));
    }

    [Fact]
    public void A_free_user_asking_for_pro_is_charged_at_the_model_they_actually_get()
    {
        // Free planda Pro yok; istek Standard'a indirgeniyor. Ücretlendirme de
        // indirgenen modele göre olmalı — kullanıcı alamadığı bir modelin
        // fiyatını ödememeli.
        var effective = NaiCatalog.ClampToPlan(NaiModel.Pro, PlanTier.Free);

        Assert.Equal(NaiModel.Standard, effective);
        Assert.Equal(NaiCatalog.CostOf(NaiModel.Standard, 10_000), NaiCatalog.CostOf(effective, 10_000));
    }

    [Fact]
    public void A_paid_user_asking_for_pro_really_is_charged_the_pro_rate()
    {
        var effective = NaiCatalog.ClampToPlan(NaiModel.Pro, PlanTier.Pro);

        Assert.Equal(NaiModel.Pro, effective);
        Assert.Equal(20_000, NaiCatalog.CostOf(effective, 10_000));
    }

    [Fact]
    public void Rounding_never_charges_less_than_the_measured_usage_on_the_base_model()
    {
        // Yukarı yuvarlama: 1 token'lık bir işi sıfıra yuvarlamak, çok sayıda
        // küçük çağrıyı bedava yapardı.
        Assert.Equal(1, NaiCatalog.CostOf(NaiModel.Standard, 1));
        Assert.Equal(1, NaiCatalog.CostOf(NaiModel.Flash, 1));
    }
}
