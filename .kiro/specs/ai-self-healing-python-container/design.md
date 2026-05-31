# AI Self-Healing Python Container Bugfix Design

## Overview

This design document formalizes the bug condition and validation approach for implementing an AI self-healing mechanism in the Namines CoderAI feature. The bug manifests when AI-generated Python code contains errors (syntax, import, or database connection issues), causing the Streamlit container to crash within seconds without any recovery attempt. The fix introduces a retry loop with AI-powered code correction, allowing the system to automatically detect crashes, extract error logs, request corrected code from the AI service, and retry deployment up to 3 times before final failure.

The implementation follows the bug condition methodology: detect container crashes during the readiness probe, extract and parse error logs, invoke AI fix service with comprehensive context, and retry deployment with corrected code while preserving all existing successful deployment behavior.

## Glossary

- **Bug_Condition (C)**: The condition that triggers the bug - when the Streamlit container crashes during the readiness probe (inspect.State.Running == false AND inspect.State.ExitCode > 0)
- **Property (P)**: The desired behavior when the bug condition is met - extract logs, call AI fix service, retry deployment up to 3 times
- **Preservation**: Existing successful deployment behavior (first-attempt success, SSE streaming, network topology, cleanup) that must remain unchanged
- **RunDualSandboxAsync**: The method in `DockerService.cs` that orchestrates dual container deployment (database + Streamlit)
- **Readiness Probe**: The 60-attempt loop (120 seconds) that monitors container logs for "Network URL" to detect successful Streamlit startup
- **ContainerProfiles**: Static class providing database-specific configuration (image, credentials, ports) for MSSQL, PostgreSQL, and MySQL
- **IAIService**: Interface defining AI service methods including the new `FixStreamlitAppAsync` for code correction
- **FixStreamlitAppAsync**: New AI service method that accepts original code, error logs, schema, and dbType to generate corrected Python code

## Bug Details

### Bug Condition

The bug manifests when the AI generates faulty Python code (syntax errors, import errors, incorrect database connection strings) and the Streamlit container crashes within seconds of startup. The `RunDualSandboxAsync` method detects the crash during the readiness probe loop but immediately throws an exception without attempting log extraction, error analysis, or retry.


**Formal Specification:**
```
FUNCTION isBugCondition(input)
  INPUT: input of type ContainerInspectResponse
  OUTPUT: boolean
  
  RETURN input.State.Running == false
         AND input.State.ExitCode > 0
         AND deploymentPhase == "READINESS_PROBE"
         AND elapsedTime < 120 seconds
END FUNCTION
```

### Examples

- **Syntax Error Example**: AI generates code with missing colon after `if` statement → Container crashes with `SyntaxError: invalid syntax` → System logs "HATA: Python container beklenmedik şekilde kapandı (exit=1)" → Operation fails without retry
- **Import Error Example**: AI generates code with `import pandas as pd` but pandas not in requirements.txt → Container crashes with `ModuleNotFoundError: No module named 'pandas'` → System logs crash → Operation fails without retry
- **Database Connection Error Example**: AI generates code with `SERVER=localhost` instead of `SERVER=db` → Container crashes with `pyodbc.OperationalError: TCP Provider: Error code 0x2746` → System logs crash → Operation fails without retry
- **Edge Case - Timeout**: Container starts but Streamlit never logs "Network URL" within 120 seconds → System logs "⚠️ Streamlit 2 dakikada yanıt vermedi" → URL sent anyway (not a crash, different code path)

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- Successful first-attempt deployments must complete without triggering retry logic (retry counter remains 0)
- Immediate success detection when "Network URL" appears in logs within first 20 seconds
- Dual sandbox network topology (database container accessible via hostname "db", both containers on same network)
- SSE log streaming to frontend terminal UI with real-time progress updates
- Downloadable .zip package generation containing app.py, schema.sql, requirements.txt
- Sandbox cleanup via CleanupSandboxAsync removing containers, networks, and temp files
- ContainerProfiles usage for database configuration (image, credentials, ports)
- Existing AI service methods (GenerateSchemaAsync, ReviseSchemaAsync, GenerateMockDataAsync, GenerateProjectSummaryAsync, GenerateStreamlitAppAsync) remain unchanged

**Scope:**
All inputs that do NOT involve container crashes (successful deployments, timeouts without crashes, user-initiated cleanup) should be completely unaffected by this fix. This includes:
- Successful Streamlit container startups (inspect.State.Running == true AND logs contain "Network URL")
- Database container operations (DDL execution, warmup delays)
- Network and volume management
- File system operations (temp directory creation, .zip packaging)


## Hypothesized Root Cause

Based on the bug description and code analysis, the most likely issues are:

1. **Missing Retry Logic**: The readiness probe loop in `RunDualSandboxAsync` detects container crashes but immediately throws an exception without attempting recovery. The code path is: detect crash → log error → throw exception → propagate to CoderAIController → mark job as failed.

2. **No Log Extraction**: When a crash is detected (`inspect.State.Running == false`), the system logs a generic error message but does not call `_client.Containers.GetContainerLogsAsync()` to extract detailed error information from stdout/stderr.

3. **No AI Fix Service Integration**: The `IAIService` interface does not have a method for code correction. The existing `GenerateStreamlitAppAsync` method generates initial code but cannot accept error feedback for correction.

4. **Single-Attempt Deployment**: The `CoderAIController.GenerateSandbox` background task calls `RunDualSandboxAsync` once and handles exceptions by marking the job as failed. There is no retry counter or loop to attempt redeployment with corrected code.

## Correctness Properties

Property 1: Bug Condition - AI Self-Healing Retry Mechanism

_For any_ container deployment where the Streamlit container crashes during the readiness probe (isBugCondition returns true), the fixed RunDualSandboxAsync method SHALL extract container logs, call IAIService.FixStreamlitAppAsync with original code, error logs, schema, and database connection parameters, receive corrected code, increment retry counter, cleanup crashed container, and retry deployment with corrected code up to 3 total attempts.

**Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5**

Property 2: Preservation - Successful First-Attempt Deployment

_For any_ container deployment where the Streamlit container starts successfully on the first attempt (isBugCondition returns false), the fixed RunDualSandboxAsync method SHALL produce exactly the same behavior as the original method, preserving immediate success detection, zero retry counter, no AI fix service calls, and all existing network topology, SSE streaming, and cleanup behaviors.

**Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8**


## Fix Implementation

### Changes Required

Assuming our root cause analysis is correct:

**File 1**: `Namines.Core/Interfaces/IAIService.cs`

**Changes**:
1. **Add FixStreamlitAppAsync Method**: Add new method signature to the interface:
   ```csharp
   Task<string> FixStreamlitAppAsync(string originalCode, string errorLogs, DatabaseSchema schema, DatabaseType dbType);
   ```
   - Parameters: originalCode (faulty Python code), errorLogs (container logs), schema (database schema JSON), dbType (MSSQL/PostgreSQL/MySQL)
   - Returns: Corrected Python code as string
   - Purpose: Provide AI service with comprehensive error context for code correction

**File 2**: `Namines.Infrastructure/AI/GroqAIService.cs`

**Changes**:
1. **Implement FixStreamlitAppAsync**: Add method implementation with prompt engineering:
   ```csharp
   public async Task<string> FixStreamlitAppAsync(string originalCode, string errorLogs, DatabaseSchema schema, DatabaseType dbType)
   {
       var profile = ContainerProfiles.GetProfile(dbType);
       var schemaJson = JsonSerializer.Serialize(schema);
       
       var systemPrompt = "You are a Python debugging expert. Fix the provided Streamlit code based on error logs.";
       var userPrompt = $@"
       Original Code:
       {originalCode}
       
       Error Logs:
       {errorLogs}
       
       Database Schema:
       {schemaJson}
       
       Database Connection Parameters:
       - Hostname: db
       - Port: {GetPortForDbType(dbType)}
       - Username: {GetUsernameFromProfile(profile)}
       - Password: {GetPasswordFromProfile(profile)}
       - Database: naminesdb
       
       Fix the code and return ONLY the corrected Python code without markdown.";
       
       // Call Groq API with retry logic (similar to GenerateStreamlitAppAsync)
       // Parse response, clean markdown, return corrected code
   }
   ```

2. **Add Helper Methods**: Add private methods to extract connection parameters:
   - `GetPortForDbType(DatabaseType dbType)`: Returns 1433 for MSSQL, 5432 for PostgreSQL, 3306 for MySQL
   - `GetUsernameFromProfile(ContainerProfile profile)`: Extracts username from EnvVars
   - `GetPasswordFromProfile(ContainerProfile profile)`: Extracts password from EnvVars


**File 3**: `Namines.Infrastructure/AI/OllamaAIService.cs`

**Changes**:
1. **Implement FixStreamlitAppAsync**: Add identical method implementation using Ollama API endpoint:
   ```csharp
   public async Task<string> FixStreamlitAppAsync(string originalCode, string errorLogs, DatabaseSchema schema, DatabaseType dbType)
   {
       // Same prompt engineering as GroqAIService
       // Use Ollama /api/chat endpoint with qwen2.5-coder model
       // Temperature: 0.2 for deterministic fixes
   }
   ```

**File 4**: `Namines.Infrastructure/Services/DockerService.cs`

**Function**: `RunDualSandboxAsync`

**Specific Changes**:

1. **Add Retry Loop Wrapper**: Wrap the entire deployment logic in a retry loop:
   ```csharp
   public async Task<DualSandboxResult> RunDualSandboxAsync(
       string jobId, string sqlContent, string appPyContent, 
       DatabaseType dbType, Action<string> onProgress)
   {
       const int MAX_RETRIES = 3;
       int retryCount = 0;
       string currentAppPyContent = appPyContent;
       List<string> retryLogs = new List<string>();
       
       while (retryCount < MAX_RETRIES)
       {
           try
           {
               // Existing deployment logic here
               return result; // Success - exit retry loop
           }
           catch (ContainerCrashException ex)
           {
               retryCount++;
               if (retryCount >= MAX_RETRIES)
               {
                   throw new Exception($"Maximum retry attempts ({MAX_RETRIES}) exceeded. Retry logs: {string.Join("; ", retryLogs)}");
               }
               
               // Extract logs, call AI fix, retry
               retryLogs.Add($"Attempt {retryCount}/{MAX_RETRIES}: {ex.Message}");
               onProgress($"🔄 Deneme {retryCount}/{MAX_RETRIES} başarısız. AI ile kod düzeltiliyor...");
               
               // Continue to next iteration with corrected code
           }
       }
   }
   ```

2. **Modify Crash Detection**: Replace immediate exception throw with log extraction:
   ```csharp
   // OLD CODE:
   if (!inspect.State.Running)
   {
       onProgress($"HATA: Python container beklenmedik şekilde kapandı (exit={exitCode}).");
       throw new Exception($"Python container crashed with exit code {exitCode}.");
   }
   
   // NEW CODE:
   if (!inspect.State.Running)
   {
       var exitCode = inspect.State.ExitCode;
       onProgress($"⚠️ Container kapandı (exit={exitCode}). Loglar çıkarılıyor...");
       
       var errorLogs = await ExtractContainerLogsAsync(pyContainerId);
       throw new ContainerCrashException(exitCode, errorLogs);
   }
   ```


3. **Add Log Extraction Method**: Create new private method to extract container logs:
   ```csharp
   private async Task<string> ExtractContainerLogsAsync(string containerId)
   {
       try
       {
           var ms = new MemoryStream();
           using (var logStream = await _client.Containers.GetContainerLogsAsync(
               containerId, true,
               new ContainerLogsParameters 
               { 
                   ShowStdout = true, 
                   ShowStderr = true, 
                   Tail = "1000" // Last 1000 lines
               }))
           {
               await logStream.CopyOutputToAsync(Stream.Null, ms, Stream.Null, CancellationToken.None);
           }
           
           var logText = Encoding.UTF8.GetString(ms.ToArray());
           
           // Truncate to 10,000 characters if needed
           if (logText.Length > 10000)
           {
               logText = logText.Substring(logText.Length - 10000);
           }
           
           return logText;
       }
       catch (Exception ex)
       {
           return $"Failed to extract container logs: {ex.Message}";
       }
   }
   ```

4. **Add AI Fix Service Call**: In the catch block of the retry loop:
   ```csharp
   catch (ContainerCrashException ex)
   {
       retryCount++;
       if (retryCount >= MAX_RETRIES)
       {
           throw new Exception($"Maximum retry attempts ({MAX_RETRIES}) exceeded. Final error: {ex.Message}");
       }
       
       retryLogs.Add($"Attempt {retryCount}/{MAX_RETRIES}: Container crashed with exit code {ex.ExitCode}");
       onProgress($"🔄 Deneme {retryCount}/{MAX_RETRIES} başarısız. AI ile kod düzeltiliyor...");
       
       // Call AI fix service
       var aiService = _aiFactory.GetService("Groq"); // Inject IAIFactory via constructor
       var schema = _currentSchema; // Store schema in instance variable
       
       currentAppPyContent = await aiService.FixStreamlitAppAsync(
           currentAppPyContent, 
           ex.ErrorLogs, 
           schema, 
           dbType
       );
       
       onProgress($"✅ AI düzeltilmiş kod üretti. Yeniden deneniyor...");
       
       // Cleanup crashed container before retry
       await CleanupContainerAsync(pyContainerId);
       
       // Continue to next iteration
   }
   ```


5. **Add Custom Exception Class**: Create new exception type to carry error logs:
   ```csharp
   public class ContainerCrashException : Exception
   {
       public int ExitCode { get; }
       public string ErrorLogs { get; }
       
       public ContainerCrashException(int exitCode, string errorLogs) 
           : base($"Container crashed with exit code {exitCode}")
       {
           ExitCode = exitCode;
           ErrorLogs = errorLogs;
       }
   }
   ```

6. **Add Cleanup Helper Method**: Create method to cleanup crashed container:
   ```csharp
   private async Task CleanupContainerAsync(string containerId)
   {
       try
       {
           await _client.Containers.StopContainerAsync(containerId, 
               new ContainerStopParameters { WaitBeforeKillSeconds = 2 });
           await _client.Containers.RemoveContainerAsync(containerId, 
               new ContainerRemoveParameters { Force = true });
       }
       catch (Exception)
       {
           // Ignore cleanup errors
       }
   }
   ```

7. **Update Constructor**: Add IAIFactory dependency injection:
   ```csharp
   private readonly DockerClient _client;
   private readonly IAIFactory _aiFactory;
   
   public DockerService(IAIFactory aiFactory)
   {
       var dockerUri = Environment.OSVersion.Platform == PlatformID.Win32NT 
           ? "npipe://./pipe/docker_engine" 
           : "unix:///var/run/docker.sock";
           
       _client = new DockerClientConfiguration(new Uri(dockerUri)).CreateClient();
       _aiFactory = aiFactory;
   }
   ```

8. **Store Schema Context**: Add instance variable to store schema for AI fix calls:
   ```csharp
   private DatabaseSchema? _currentSchema;
   
   // In RunDualSandboxAsync, before retry loop:
   public async Task<DualSandboxResult> RunDualSandboxAsync(
       string jobId, string sqlContent, string appPyContent, 
       DatabaseType dbType, Action<string> onProgress, 
       DatabaseSchema schema) // Add schema parameter
   {
       _currentSchema = schema;
       // ... rest of method
   }
   ```


**File 5**: `Namines.Core/Interfaces/IDockerService.cs`

**Changes**:
1. **Update RunDualSandboxAsync Signature**: Add schema parameter:
   ```csharp
   Task<DualSandboxResult> RunDualSandboxAsync(
       string jobId, 
       string sqlContent, 
       string appPyContent, 
       DatabaseType dbType, 
       Action<string> onProgress,
       DatabaseSchema schema); // New parameter
   ```

**File 6**: `Namines.API/Controllers/CoderAIController.cs`

**Changes**:
1. **Update RunDualSandboxAsync Call**: Pass schema parameter:
   ```csharp
   // OLD CODE:
   var result = await dockerService.RunDualSandboxAsync(
       jobId, sqlContent, appPyContent, request.DbType, log =>
   {
       _logger.LogInformation("CoderAI [{JobId}] Docker: {Log}", jobId, log);
       _jobManager.AddLog(jobId, log);
   });
   
   // NEW CODE:
   var result = await dockerService.RunDualSandboxAsync(
       jobId, sqlContent, appPyContent, request.DbType, log =>
   {
       _logger.LogInformation("CoderAI [{JobId}] Docker: {Log}", jobId, log);
       _jobManager.AddLog(jobId, log);
   }, request.Schema); // Pass schema
   ```

**File 7**: `Namines.Infrastructure/Extensions/ServiceCollectionExtensions.cs` (if exists)

**Changes**:
1. **Register IAIFactory**: Ensure IAIFactory is registered in DI container for DockerService injection


## Testing Strategy

### Validation Approach

The testing strategy follows a two-phase approach: first, surface counterexamples that demonstrate the bug on unfixed code (container crashes without retry), then verify the fix works correctly (retry mechanism triggers, AI corrects code, deployment succeeds) and preserves existing behavior (successful first-attempt deployments remain unchanged).

### Exploratory Bug Condition Checking

**Goal**: Surface counterexamples that demonstrate the bug BEFORE implementing the fix. Confirm or refute the root cause analysis. If we refute, we will need to re-hypothesize.

**Test Plan**: Manually inject faulty Python code into the CoderAI workflow and observe container crash behavior on the UNFIXED code. Run these tests to observe failures and understand the root cause.

**Test Cases**:
1. **Syntax Error Test**: Inject Python code with missing colon after `if` statement → Observe container crash with `SyntaxError` → Verify system logs "HATA: Python container beklenmedik şekilde kapandı" → Verify no retry attempt (will fail on unfixed code)
2. **Import Error Test**: Inject Python code with `import pandas as pd` (not in requirements.txt) → Observe container crash with `ModuleNotFoundError` → Verify system logs crash → Verify no retry attempt (will fail on unfixed code)
3. **Database Connection Error Test**: Inject Python code with `SERVER=localhost` instead of `SERVER=db` → Observe container crash with `pyodbc.OperationalError` → Verify system logs crash → Verify no retry attempt (will fail on unfixed code)
4. **Multiple Error Test**: Inject code with both syntax error AND wrong hostname → Observe which error is detected first → Verify crash handling (will fail on unfixed code)

**Expected Counterexamples**:
- Container crashes are detected but no log extraction occurs
- System throws exception immediately without calling AI fix service
- Possible causes: missing retry loop, no log extraction method, no AI fix service integration

### Fix Checking

**Goal**: Verify that for all inputs where the bug condition holds (container crashes), the fixed function produces the expected behavior (retry with AI correction).

**Pseudocode:**
```
FOR ALL input WHERE isBugCondition(input) DO
  result := RunDualSandboxAsync_fixed(input)
  ASSERT result.retryCount > 0 AND result.retryCount <= 3
  ASSERT result.aiFixServiceCalled == true
  ASSERT result.logsExtracted == true
  ASSERT (result.success == true) OR (result.retryCount == 3 AND result.errorMessage CONTAINS "Maximum retry attempts")
END FOR
```


### Preservation Checking

**Goal**: Verify that for all inputs where the bug condition does NOT hold (successful deployments), the fixed function produces the same result as the original function.

**Pseudocode:**
```
FOR ALL input WHERE NOT isBugCondition(input) DO
  ASSERT RunDualSandboxAsync_original(input) = RunDualSandboxAsync_fixed(input)
  ASSERT retryCount == 0
  ASSERT aiFixServiceCalled == false
  ASSERT logsExtracted == false
END FOR
```

**Testing Approach**: Property-based testing is recommended for preservation checking because:
- It generates many test cases automatically across the input domain (different schemas, database types, valid Python code)
- It catches edge cases that manual unit tests might miss (large schemas, special characters in table names, complex relationships)
- It provides strong guarantees that behavior is unchanged for all non-buggy inputs

**Test Plan**: Deploy with correct AI-generated code on UNFIXED code first to establish baseline behavior, then write property-based tests capturing that behavior and verify it continues after fix.

**Test Cases**:
1. **Successful First-Attempt Deployment**: Generate valid schema → AI generates correct Python code → Deploy → Observe "Network URL" in logs within 20 seconds → Verify retry counter remains 0 → Verify no AI fix service calls
2. **Immediate Success Detection**: Generate simple schema → Deploy → Verify URL returned within first 10 readiness probe attempts (20 seconds) → Verify no full 120-second timeout wait
3. **Network Topology Preservation**: Deploy → Verify database container accessible via hostname "db" → Verify both containers on same network → Verify file bindings to /app directory
4. **SSE Streaming Preservation**: Deploy → Monitor SSE stream → Verify real-time log updates → Verify log order and formatting preserved
5. **Cleanup Preservation**: Deploy → Click "Sandbox'ı Kapat" → Verify CleanupSandboxAsync removes all containers, networks, temp files

### Unit Tests

- Test `ExtractContainerLogsAsync` with mock Docker client returning various log formats (syntax errors, import errors, database errors)
- Test `FixStreamlitAppAsync` with mock AI service returning corrected code
- Test retry loop logic with mock container crashes (1 crash, 2 crashes, 3 crashes)
- Test max retry limit enforcement (verify exception thrown after 3 attempts)
- Test cleanup helper method with mock Docker client
- Test ContainerCrashException creation and property access


### Property-Based Tests

- Generate random database schemas (varying table counts, column types, relationship complexity) → Deploy with correct code → Verify all deployments succeed without retry logic
- Generate random faulty Python code patterns (syntax errors, import errors, connection errors) → Deploy → Verify retry mechanism triggers for all patterns → Verify AI fix service called with correct parameters
- Generate random combinations of database types (MSSQL, PostgreSQL, MySQL) and error types → Verify correct connection parameters passed to AI fix service
- Test retry counter increments correctly across many scenarios (1 crash, 2 crashes, 3 crashes, mixed success/failure patterns)

### Integration Tests

- **End-to-End Syntax Error Recovery**: Generate schema → Inject syntax error in AI-generated code → Deploy → Verify container crash detected → Verify logs extracted → Verify AI fix service called → Verify corrected code deployed → Verify Streamlit URL returned
- **End-to-End Import Error Recovery**: Generate schema → Inject import error → Deploy → Verify retry mechanism → Verify AI corrects import statement → Verify successful deployment
- **End-to-End Database Connection Error Recovery**: Generate schema → Inject wrong hostname → Deploy → Verify retry mechanism → Verify AI corrects hostname to "db" → Verify successful deployment
- **Max Retry Limit Integration**: Generate schema → Inject unfixable error (AI returns same faulty code) → Deploy → Verify 3 retry attempts → Verify final exception with retry logs
- **Successful Deployment Integration**: Generate schema → AI generates correct code → Deploy → Verify no retry logic triggered → Verify URL returned immediately → Verify .zip package generated
- **SSE Streaming Integration**: Deploy with faulty code → Monitor SSE stream → Verify retry progress messages appear ("🔄 Deneme 1/3 başarısız. AI ile kod düzeltiliyor...") → Verify final success or failure message
- **Cleanup Integration**: Deploy with faulty code → Trigger crash → Verify crashed container cleaned up before retry → Verify final cleanup removes all resources

## Implementation Notes

**Key Design Decisions:**

1. **Retry Limit**: Maximum 3 retry attempts balances reliability (gives AI multiple chances to fix code) vs. performance (prevents infinite loops, provides timely feedback to users)

2. **Log Extraction**: Use `Docker.DotNet` `GetContainerLogsAsync` with `Tail="1000"` to capture sufficient error context (last 1000 lines) while avoiding memory issues with extremely large logs. Truncate to 10,000 characters to respect AI service input limits.

3. **AI Prompt Engineering**: Include database connection parameters explicitly in the fix prompt (hostname="db", port, username, password) to guide the AI toward correct solutions. This addresses the most common error category (wrong connection strings).

4. **Graceful Degradation**: After 3 failed attempts, provide detailed error logs and retry history to the user for manual debugging. This ensures users are not left without actionable information.

5. **Backward Compatibility**: Wrap retry logic in a try-catch structure that only triggers on ContainerCrashException. Successful deployments follow the original code path without any retry overhead, ensuring existing successful deployments are not affected.

6. **Dependency Injection**: Inject IAIFactory into DockerService to enable AI fix service calls. This maintains separation of concerns and testability.

7. **Schema Context**: Pass DatabaseSchema as parameter to RunDualSandboxAsync and store in instance variable. This enables AI fix service to receive complete schema context for accurate code correction.

8. **Custom Exception**: Create ContainerCrashException to carry both exit code and error logs through the exception handling chain. This enables the retry loop to access detailed error information for AI fix calls.

**Testing Strategy:**

- **Fix Checking**: Manually inject faulty Python code (syntax error, import error, wrong hostname) and verify retry mechanism triggers, logs are extracted, AI fix service is called, and deployment succeeds or fails gracefully after 3 attempts
- **Preservation Checking**: Deploy with correct AI-generated code and verify no retry logic is triggered, retry counter remains 0, and all existing behaviors (SSE streaming, network topology, cleanup) are preserved
- **Edge Cases**: Test with network failures (Docker daemon unavailable), AI API failures (Groq/Ollama timeout), container resource limits (memory exhausted), and unfixable errors (AI returns same faulty code repeatedly)
