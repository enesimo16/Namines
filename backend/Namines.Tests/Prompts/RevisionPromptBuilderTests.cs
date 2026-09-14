using Namines.Core.Prompts;
using Xunit;

namespace Namines.Tests.Prompts;

public class RevisionPromptBuilderTests
{
    [Fact]
    public void System_prompt_mentions_generated_checks_indexes_and_engine_gated_triggers()
    {
        var prompt = RevisionPromptBuilder.BuildSystemPrompt();

        Assert.Contains("generated", prompt);
        Assert.Contains("checks", prompt);
        Assert.Contains("indexes", prompt);
        Assert.Contains("targetEngine", prompt);
    }
}
