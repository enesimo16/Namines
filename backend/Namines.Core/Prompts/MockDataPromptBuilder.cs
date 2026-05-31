using System.Text.Json;
using Namines.Core.Models;

namespace Namines.Core.Prompts;

public static class MockDataPromptBuilder
{
    public static string BuildSystemPrompt()
    {
        return @"You are an expert SQL developer. Your task is to generate mock data for a given database schema.
Generate exactly 5 INSERT INTO statements for each table in the schema.
Consider relationships (Foreign Keys) and generate data that makes sense and maintains referential integrity.
Output raw SQL code only, NO markdown, NO comments.
Use appropriate realistic fake data.
The output MUST be plain SQL text.";
    }

    public static string BuildUserPrompt(DatabaseSchema schema)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var schemaJson = JsonSerializer.Serialize(schema, options);

        return $@"Generate mock data for this schema:
{schemaJson}

Provide exactly 5 INSERT INTO statements per table. Order them so that parent tables are populated before child tables to avoid FK constraint errors.
Raw SQL string only.";
    }
}
