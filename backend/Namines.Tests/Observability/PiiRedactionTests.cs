using Namines.Infrastructure.Observability;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.TestCorrelator;

namespace Namines.Tests.Observability;

/// <summary>
/// Log PII redaksiyonu (new-phase/21-OBSERVABILITY.md §2).
///
/// Bu bir GÜVENLİK KONTROLÜ, biçimlendirme değil: log'lar uygulamadan uzun yaşar
/// ve daha çok kişi görür. Bir bağlantı dizesi ya da JWT oraya bir kez düşerse,
/// geri alınamaz. Testler tek tek desenleri değil, "şu değer çıktıda GÖRÜNMEMELİ"
/// sözleşmesini kilitliyor.
/// </summary>
public class PiiRedactionTests
{
    [Theory]
    [InlineData("password=hunter2")]
    [InlineData("Password: hunter2")]
    [InlineData("api_key=abc123def456")]
    [InlineData("apiKey: abc123def456")]
    [InlineData("secret=topsecretvalue")]
    [InlineData("token=abc.def.ghi")]
    public void Key_value_secrets_are_redacted(string input)
    {
        var result = PiiRedactionEnricher.Scrub(input);

        Assert.Contains(PiiRedactionEnricher.Placeholder, result);
        Assert.DoesNotContain("hunter2", result);
        Assert.DoesNotContain("abc123def456", result);
        Assert.DoesNotContain("topsecretvalue", result);
    }

    [Fact]
    public void The_key_name_survives_so_the_line_stays_useful()
    {
        // "password=[REDACTED]" satırı neyin gizlendiğini söylediği için ayıklamada
        // işe yarar; tamamen silinmiş bir satır yalnızca kafa karıştırır.
        Assert.Contains("password=", PiiRedactionEnricher.Scrub("password=hunter2"));
    }

    [Fact]
    public void A_namines_api_key_is_redacted()
    {
        var result = PiiRedactionEnricher.Scrub("Using key nmn_bs361fG0abcdef123456 for project x");

        Assert.DoesNotContain("nmn_bs361fG0abcdef123456", result);
        Assert.Contains("for project x", result);
    }

    [Fact]
    public void A_jwt_is_redacted()
    {
        const string jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0In0.abc-_123";

        var result = PiiRedactionEnricher.Scrub($"Authorization: Bearer {jwt}");

        Assert.DoesNotContain(jwt, result);
    }

    [Fact]
    public void A_connection_string_is_redacted_whole_not_just_the_password()
    {
        // Host ve kullanıcı adı da müşteri altyapısını ele verir; yalnızca parolayı
        // maskelemek "hangi sunucu, hangi kullanıcı" bilgisini açıkta bırakırdı.
        const string cs = "Host=db.musteri.com;Port=5432;Database=prod;Username=admin;Password=s3cret;";

        var result = PiiRedactionEnricher.Scrub($"Connecting with {cs}");

        Assert.DoesNotContain("db.musteri.com", result);
        Assert.DoesNotContain("s3cret", result);
        Assert.DoesNotContain("admin", result);
    }

    [Fact]
    public void An_email_is_redacted()
    {
        var result = PiiRedactionEnricher.Scrub("User ali.veli@example.com signed in");

        Assert.DoesNotContain("ali.veli@example.com", result);
        Assert.Contains("signed in", result);
    }

    [Fact]
    public void Ordinary_text_passes_through_untouched()
    {
        // Her şeyi maskeleyen bir enricher, log'ları işe yaramaz kılar. Aynı
        // referansın dönmesi, gereksiz tahsis de yapılmadığını gösteriyor.
        const string text = "Branch database provisioned for feature/orders on port 54321";

        Assert.Same(text, PiiRedactionEnricher.Scrub(text));
    }

    [Fact]
    public void Null_and_empty_are_safe()
    {
        Assert.Equal(string.Empty, PiiRedactionEnricher.Scrub(string.Empty));
    }

    // ── Gerçek Serilog boru hattı ────────────────────────────────────────────

    [Fact]
    public void The_enricher_scrubs_properties_in_a_real_pipeline()
    {
        // Saf fonksiyon testleri deseni kanıtlar, boru hattına BAĞLI olduğunu değil.
        using var logger = new LoggerConfiguration()
            .Enrich.With<PiiRedactionEnricher>()
            .WriteTo.TestCorrelator()
            .CreateLogger();

        using (TestCorrelator.CreateContext())
        {
            logger.Information(
                "Connecting to {ConnectionString}",
                "Host=db.musteri.com;Database=prod;Username=admin;Password=s3cret;");

            var logEvent = TestCorrelator.GetLogEventsFromCurrentContext().Single();
            var rendered = logEvent.Properties["ConnectionString"].ToString();

            Assert.DoesNotContain("s3cret", rendered);
            Assert.DoesNotContain("db.musteri.com", rendered);
        }
    }

    [Fact]
    public void Nested_structures_are_scrubbed_too()
    {
        // Yalnızca üst seviyeyi taramak, iç içe bir nesnedeki token'ı kaçırırdı.
        using var logger = new LoggerConfiguration()
            .Enrich.With<PiiRedactionEnricher>()
            .WriteTo.TestCorrelator()
            .CreateLogger();

        using (TestCorrelator.CreateContext())
        {
            logger.Information("Request {@Payload}", new { User = "ali@example.com", Retry = 3 });

            var logEvent = TestCorrelator.GetLogEventsFromCurrentContext().Single();
            var rendered = logEvent.Properties["Payload"].ToString();

            Assert.DoesNotContain("ali@example.com", rendered);
            // Gizli olmayan alan korunmalı; aksi hâlde log okunamaz hâle gelir.
            Assert.Contains("3", rendered);
        }
    }

    [Fact]
    public void The_message_template_itself_is_left_alone()
    {
        // Şablon sabit metindir ve geliştirici yazar; sır ÖZELLİKLERDE taşınır.
        // Şablonu da taramak her log satırına gereksiz maliyet eklerdi.
        using var logger = new LoggerConfiguration()
            .Enrich.With<PiiRedactionEnricher>()
            .WriteTo.TestCorrelator()
            .CreateLogger();

        using (TestCorrelator.CreateContext())
        {
            logger.Information("Sending mail to {Recipient}", "ali@example.com");

            var logEvent = TestCorrelator.GetLogEventsFromCurrentContext().Single();

            Assert.Contains("Sending mail to", logEvent.MessageTemplate.Text);
            Assert.DoesNotContain("ali@example.com", logEvent.Properties["Recipient"].ToString());
        }
    }
}
