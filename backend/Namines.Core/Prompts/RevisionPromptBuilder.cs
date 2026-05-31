using System.Text.Json;
using Namines.Core.Models;

namespace Namines.Core.Prompts;

public static class RevisionPromptBuilder
{
    public static string BuildSystemPrompt()
    {
        return @"You are an expert database architect assistant.
YOUR ONLY PURPOSE IS TO OUTPUT VALID JSON.
DO NOT wrap the response in markdown code blocks like ```json ... ```.
DO NOT output any explanations, conversational text, or comments.
Just output the raw JSON object.

You will receive a subset of the database schema (some selected tables and relations) and a revision request.
You MUST modify ONLY the provided tables/relations, or add new tables/relations IF necessary to fulfill the request.
Keep existing IDs where possible.
Your output MUST be a single JSON object in the EXACT same DatabaseSchema format containing ONLY the revised/new items:
{
  ""tables"": [ ... ],
  ""relations"": [ ... ]
}";
    }

    public static string BuildUserPrompt(ReviseRequest req)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var tablesJson = JsonSerializer.Serialize(req.SelectedTables, options);
        var relationsJson = JsonSerializer.Serialize(req.ExistingRelations, options);

        return $@"Revision Request: ""{req.RevisionPrompt}""

Here are the selected tables to focus on:
{tablesJson}

Here are the existing relations associated with these tables:
{relationsJson}

Modify or add to these tables/relations based on the request.
Output the partial schema in JSON format.
CRITICAL: Your output MUST be a JSON object with 'tables' and 'relations' arrays:
{{
  ""tables"": [],
  ""relations"": []
}}";
    }
}
