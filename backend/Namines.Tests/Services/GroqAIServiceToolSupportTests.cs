using Namines.Infrastructure.AI;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// Gemini'nin OpenAI-uyumluluk uç noktasının <c>tools</c>/<c>tool_choice</c>
/// parametrelerini kabul ettiği hiç DOĞRULANMADI (bkz. spec Bölüm 2 notu).
/// Doğrulanana kadar o yola yönlendirilen bir model için araçlar hiç
/// gönderilmemeli — göndermek, sağlayıcı reddederse turun bir yapılandırma
/// sorununu "[agent] Repair round N could not run" diye görünmez bir agent
/// hatasına çevirmesine yol açardı.
/// </summary>
public class GroqAIServiceToolSupportTests
{
    [Theory]
    [InlineData("gemini-2.0-flash")]
    [InlineData("gemini-1.5-pro")]
    [InlineData("Gemini-2.0-Flash")]
    public void Gemini_routed_models_do_not_support_tool_calling(string model)
    {
        Assert.False(GroqAIService.SupportsToolCalling(model));
    }

    [Theory]
    [InlineData("openai/gpt-oss-20b")]
    [InlineData("qwen/qwen3.6-27b")]
    [InlineData("openai/gpt-oss-120b")]
    [InlineData("llama-3.3-70b")]
    public void Groq_native_models_support_tool_calling(string model)
    {
        Assert.True(GroqAIService.SupportsToolCalling(model));
    }
}
