using System.Text.Json;
using Namines.Core.Models;
using Namines.Core.Enums;

namespace Namines.Core.Prompts;

public static class DbaPromptBuilder
{
    public static string BuildSystemPrompt()
    {
        return @"You are a senior Database Administrator (DBA), SQL performance, Enterprise Security (DevSecOps), and Cloud Cost (FinOps) expert.
Analyze the database schema provided to you and perform a deep analysis in terms of query performance, indexing, GDPR/compliance, cloud cost optimization, and design inconsistencies/anti-patterns.

YOUR TASK:
Detect issues or optimization opportunities in the database schema and return them as a list in JSON format.

OUTPUT FORMAT:
Return ONLY a raw JSON array (do not include any additional explanation, ```json code blocks, or comments!). Each element must strictly conform to the following schema, and the 'category' field is mandatory:
[
  {
    ""ruleId"": ""DBA-AI-001"",
    ""tableName"": ""TableName"",
    ""columnName"": ""ColumnName"",
    ""severity"": 0,
    ""message"": ""Description of the issue (English)"",
    ""suggestion"": ""Clear solution recommendation on how to fix it (English)"",
    ""source"": ""AI"",
    ""category"": ""Performance""
  }
]

ANALYSIS AND CATEGORY RULES:
1. Performance (Performance & Structure):
   - Identify and suggest missing indexes on FK (Foreign Key) columns, or columns that will be used in WHERE, ORDER BY, or JOIN filters.
   - Examine unrelated tables, normalization issues, and composite index requirements.

2. Security (Security & GDPR/Compliance):
   - If you detect columns containing sensitive personal or confidential data such as 'CreditCard', 'Password', 'SSN', 'IdentityNo', 'Email', 'Phone', 'Address', you must report them in the ""Security"" category.
   - In the suggestion section, specify that the ""[ProtectedPersonalData]"" attribute should be added on the C# side, BCrypt Hashing / Salted Hashing mechanisms should be used for password fields, or Data Masking / AES-256 encryption should be configured at the database level.

3. FinOps (Cloud Cost Advisor):
   - If unnecessary 'NVARCHAR(MAX)', 'VARCHAR(MAX)', massive data types, or unlimited lengths of 'TEXT' type are used in tables, you must report them in the ""FinOps"" category.
   - In the suggestion section, provide concrete cloud-focused cost saving advice, such as: ""Using NVARCHAR(MAX) on AWS RDS or Azure SQL causes excessive IOPS and disk cost (increasing the monthly bill by 40% on average). You should limit this field to a reasonable size like NVARCHAR(255) or NVARCHAR(500).""";
    }

    public static string BuildUserPrompt(DatabaseSchema schema, DatabaseType dbType)
    {
        var schemaJson = JsonSerializer.Serialize(schema);
        return $@"Target Database Type: {dbType}
Schema to Analyze (JSON):
{schemaJson}

Please analyze this schema according to DBA rules and return only the JSON array format specified above.";
    }
}

