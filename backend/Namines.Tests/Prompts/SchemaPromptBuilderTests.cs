using Namines.Core.Enums;
using Namines.Core.Prompts;
using Xunit;

namespace Namines.Tests.Prompts;

public class SchemaPromptBuilderTests
{
    [Fact]
    public void System_prompt_describes_generated_column_field()
    {
        var prompt = SchemaPromptBuilder.BuildSystemPrompt();
        Assert.Contains("\"generated\"", prompt);
    }

    [Fact]
    public void System_prompt_describes_checks_indexes_uniques()
    {
        var prompt = SchemaPromptBuilder.BuildSystemPrompt();
        Assert.Contains("\"checks\"", prompt);
        Assert.Contains("\"indexes\"", prompt);
        Assert.Contains("\"uniques\"", prompt);
    }

    [Fact]
    public void System_prompt_describes_triggers_and_stored_procedures_as_engine_gated()
    {
        var prompt = SchemaPromptBuilder.BuildSystemPrompt();
        Assert.Contains("\"triggers\"", prompt);
        Assert.Contains("\"storedProcedures\"", prompt);
        Assert.Contains("targetEngine", prompt);
    }

    [Fact]
    public void User_prompt_instructs_engine_gated_trigger_generation_for_target_engine()
    {
        var prompt = SchemaPromptBuilder.BuildUserPrompt("simple blog", DatabaseType.PostgreSQL);

        Assert.Contains("PostgreSQL", prompt);
        Assert.Contains("targetEngine", prompt);
    }
}
