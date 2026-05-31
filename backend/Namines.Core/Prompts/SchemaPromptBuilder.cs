using Namines.Core.Enums;

namespace Namines.Core.Prompts;

public static class SchemaPromptBuilder
{
    public static string BuildSystemPrompt()
    {
        return @"You are an expert database architect assistant.
YOUR ONLY PURPOSE IS TO OUTPUT VALID JSON.
DO NOT wrap the response in markdown code blocks like ```json ... ```.
DO NOT output any explanations, conversational text, or comments.
Just output the raw JSON object.
The output MUST strictly conform to the following JSON schema:
{
  ""schemaId"": ""uuid-v4"",
  ""name"": ""string"",
  ""tables"": [
    {
      ""id"": ""string"",
      ""name"": ""string"",
      ""columns"": [
        {
          ""id"": ""string"",
          ""name"": ""string"",
          ""type"": ""string"",
          ""length"": 255, // null if not applicable
          ""isPK"": true,
          ""isFK"": false,
          ""isNullable"": false,
          ""defaultValue"": null
        }
      ]
    }
  ],
  ""relations"": [
    {
      ""id"": ""string"",
      ""type"": ""string (OneToOne, OneToMany, ManyToMany)"",
      ""sourceTableId"": ""string"",
      ""sourceColumnId"": ""string"",
      ""targetTableId"": ""string"",
      ""targetColumnId"": ""string""
    }
  ]
}

Rules:
1. Ensure tables are normalized (3NF).
2. Every table MUST have a Primary Key.
3. Foreign Keys MUST be represented in the relations array, and the corresponding column MUST have isFK = true.
4. Output ONLY valid, parseable JSON.";
    }

    public static string BuildUserPrompt(string userInput, DatabaseType dbType)
    {
        return $@"Create a database schema for the following requirement:
Requirement: ""{userInput}""
Target Database Engine: {dbType}

Respond ONLY with the JSON representing this schema.";
    }
}
