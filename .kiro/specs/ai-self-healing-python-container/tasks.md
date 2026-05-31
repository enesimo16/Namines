# Implementation Plan

## Overview

Bu implementation plan, AI self-healing Python container bugfix'i için gerekli tüm task'ları öncelik sırasına göre listeler. Her task, açık acceptance criteria, etkilenen dosyalar, bağımlılıklar ve test stratejisi içerir.

---

## Phase 1: Exploration Tests (BEFORE Implementation)

### 1. Write bug condition exploration test

- **Property 1: Bug Condition** - Container Crash Without Retry
- **CRITICAL**: Bu test UNFIXED code üzerinde çalıştırılmalı ve BAŞARISIZ olmalı - başarısızlık bug'ın varlığını doğrular
- **DO NOT attempt to fix the test or the code when it fails**
- **NOTE**: Bu test expected behavior'ı encode eder - implementation'dan sonra geçtiğinde fix'i validate edecek
- **GOAL**: Bug'ın varlığını gösteren counterexample'ları ortaya çıkar
- **Scoped PBT Approach**: Deterministik bug için property'yi concrete failing case'lere scope et (syntax error, import error, database connection error)
- Test implementation details:
  - Inject faulty Python code with syntax error (missing colon after `if` statement)
  - Deploy using RunDualSandboxAsync
  - Assert container crashes with ExitCode > 0
  - Assert system logs "HATA: Python container beklenmedik şekilde kapandı"
  - Assert NO log extraction occurs (GetContainerLogsAsync not called)
  - Assert NO AI fix service call occurs (FixStreamlitAppAsync not called)
  - Assert retry counter remains 0
  - Assert operation fails with exception
- Run test on UNFIXED code
- **EXPECTED OUTCOME**: Test FAILS (bu doğru - bug'ın var olduğunu kanıtlar)
- Document counterexamples found:
  - Syntax error case: Container crashes, no retry
  - Import error case: Container crashes, no retry
  - Database connection error case: Container crashes, no retry
- Mark task complete when test is written, run, and failure is documented
- _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_

### 2. Write preservation property tests (BEFORE implementing fix)

- **Property 2: Preservation** - Successful First-Attempt Deployment
- **IMPORTANT**: Observation-first methodology'yi takip et
- Observe behavior on UNFIXED code for non-buggy inputs:
  - Deploy with correct AI-generated Python code
  - Observe: Container starts successfully (inspect.State.Running == true)
  - Observe: Logs contain "Network URL" within 20 seconds
  - Observe: Streamlit URL returned immediately
  - Observe: Retry counter remains 0
  - Observe: No AI fix service calls
  - Observe: SSE streaming works correctly
  - Observe: Network topology preserved (database accessible via "db")
  - Observe: Cleanup works correctly
- Write property-based tests capturing observed behavior patterns:
  - Property: For all valid schemas and correct Python code, deployment succeeds on first attempt
  - Property: For all successful deployments, retry counter == 0
  - Property: For all successful deployments, FixStreamlitAppAsync not called
  - Property: For all successful deployments, URL returned within 120 seconds
  - Property: For all successful deployments, SSE logs streamed in real-time
- Property-based testing generates many test cases for stronger guarantees
- Run tests on UNFIXED code
- **EXPECTED OUTCOME**: Tests PASS (baseline behavior'ı preserve etmek için)
- Mark task complete when tests are written, run, and passing on unfixed code
- _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8_

---

## Phase 2: Interface and Model Changes

### 3. Update IAIService interface

- **Affected Files**: `Namines.Core/Interfaces/IAIService.cs`
- **Dependencies**: None (interface definition)
- **Acceptance Criteria**:
  1. Add new method signature: `Task<string> FixStreamlitAppAsync(string originalCode, string errorLogs, DatabaseSchema schema, DatabaseType dbType);`
  2. Method must accept 4 parameters: originalCode (faulty Python code), errorLogs (container logs), schema (database schema JSON), dbType (MSSQL/PostgreSQL/MySQL)
  3. Method must return corrected Python code as string
  4. XML documentation must explain purpose: "Fixes faulty Streamlit code based on error logs and database schema"
  5. All existing method signatures must remain unchanged (GenerateSchemaAsync, ReviseSchemaAsync, GenerateMockDataAsync, GenerateProjectSummaryAsync, GenerateStreamlitAppAsync)
- **Test Strategy**:
  - Verify interface compiles without errors
  - Verify all implementing classes show compilation errors (expected - will be fixed in next tasks)
- _Requirements: 2.3_

### 4. Update IDockerService interface

- **Affected Files**: `Namines.Core/Interfaces/IDockerService.cs`
- **Dependencies**: Task 3 (IAIService interface updated)
- **Acceptance Criteria**:
  1. Update RunDualSandboxAsync signature to include DatabaseSchema parameter
  2. New signature: `Task<DualSandboxResult> RunDualSandboxAsync(string jobId, string sqlContent, string appPyContent, DatabaseType dbType, Action<string> onProgress, DatabaseSchema schema);`
  3. Parameter order must be: jobId, sqlContent, appPyContent, dbType, onProgress, schema (schema added at end)
  4. XML documentation must explain schema parameter: "Database schema for AI fix service context"
  5. All other method signatures must remain unchanged (CleanupSandboxAsync, etc.)
- **Test Strategy**:
  - Verify interface compiles without errors
  - Verify DockerService implementation shows compilation error (expected - will be fixed in next task)
  - Verify CoderAIController shows compilation error (expected - will be fixed later)
- _Requirements: 2.3_

### 5. Add ContainerCrashException model

- **Affected Files**: `Namines.Infrastructure/Exceptions/ContainerCrashException.cs` (new file)
- **Dependencies**: None
- **Acceptance Criteria**:
  1. Create new exception class inheriting from Exception
  2. Add ExitCode property (int) with public getter
  3. Add ErrorLogs property (string) with public getter
  4. Add constructor accepting exitCode and errorLogs parameters
  5. Constructor must call base constructor with message: "Container crashed with exit code {exitCode}"
  6. Class must be public and in namespace Namines.Infrastructure.Exceptions
- **Test Strategy**:
  - Unit test: Create exception with exitCode=1 and errorLogs="test logs" → Verify ExitCode == 1, ErrorLogs == "test logs", Message contains "exit code 1"
  - Unit test: Throw and catch exception → Verify properties accessible in catch block
- _Requirements: 2.1_

---

## Phase 3: Core Logic Implementation

### 6. Implement DockerService retry loop and log extraction

- **Affected Files**: `Namines.Infrastructure/Services/DockerService.cs`
- **Dependencies**: Task 4 (IDockerService interface updated), Task 5 (ContainerCrashException created)
- **Acceptance Criteria**:
  1. Add retry loop wrapper around entire deployment logic with MAX_RETRIES = 3
  2. Add instance variable `private DatabaseSchema? _currentSchema;` to store schema context
  3. Update constructor to inject IAIFactory dependency: `public DockerService(IAIFactory aiFactory)`
  4. Store schema in _currentSchema at start of RunDualSandboxAsync
  5. Modify crash detection to extract logs instead of immediate exception throw:
     - Replace `throw new Exception($"Python container crashed with exit code {exitCode}");`
     - With: `var errorLogs = await ExtractContainerLogsAsync(pyContainerId); throw new ContainerCrashException(exitCode, errorLogs);`
  6. Add private method `ExtractContainerLogsAsync(string containerId)`:
     - Use Docker.DotNet GetContainerLogsAsync with ShowStdout=true, ShowStderr=true, Tail="1000"
     - Read logs into MemoryStream, convert to UTF-8 string
     - Truncate to 10,000 characters if longer
     - Return logs as string
     - On exception, return "Failed to extract container logs: {ex.Message}"
  7. Add private method `CleanupContainerAsync(string containerId)`:
     - Stop container with 2-second timeout
     - Remove container with Force=true
     - Ignore cleanup exceptions
  8. Implement retry loop catch block:
     - Catch ContainerCrashException
     - Increment retryCount
     - If retryCount >= MAX_RETRIES, throw exception with retry logs
     - Add retry log entry: "Attempt {retryCount}/{MAX_RETRIES}: Container crashed with exit code {ex.ExitCode}"
     - Call onProgress with "🔄 Deneme {retryCount}/{MAX_RETRIES} başarısız. AI ile kod düzeltiliyor..."
     - Get AI service from _aiFactory.GetService("Groq")
     - Call aiService.FixStreamlitAppAsync(currentAppPyContent, ex.ErrorLogs, _currentSchema, dbType)
     - Update currentAppPyContent with corrected code
     - Call onProgress with "✅ AI düzeltilmiş kod üretti. Yeniden deneniyor..."
     - Call CleanupContainerAsync(pyContainerId)
     - Continue to next iteration
  9. On successful deployment, return result immediately (exit retry loop)
- **Test Strategy**:
  - Unit test: Mock Docker client to return crashed container (Running=false, ExitCode=1) → Verify ExtractContainerLogsAsync called → Verify ContainerCrashException thrown with logs
  - Unit test: Mock Docker client to return logs "SyntaxError: invalid syntax" → Verify logs extracted correctly → Verify truncation to 10,000 chars
  - Unit test: Mock 3 consecutive crashes → Verify retry counter increments → Verify max retry exception thrown with retry logs
  - Unit test: Mock 1 crash then success → Verify retry counter == 1 → Verify AI fix service called once → Verify cleanup called once → Verify success result returned
  - Integration test: Deploy with syntax error → Verify retry mechanism triggers → Verify logs extracted → Verify AI fix service called
- _Requirements: 2.1, 2.2, 2.4, 2.5_
- _Bug_Condition: isBugCondition(input) where input.State.Running == false AND input.State.ExitCode > 0 AND deploymentPhase == "READINESS_PROBE"_
- _Expected_Behavior: Extract logs, call AI fix service, retry deployment up to 3 times_
- _Preservation: Successful first-attempt deployments must not trigger retry logic (retry counter remains 0)_

---

## Phase 4: AI Service Implementations

### 7. Implement GroqAIService.FixStreamlitAppAsync

- **Affected Files**: `Namines.Infrastructure/AI/GroqAIService.cs`
- **Dependencies**: Task 3 (IAIService interface updated)
- **Acceptance Criteria**:
  1. Implement FixStreamlitAppAsync method matching interface signature
  2. Extract database connection parameters from ContainerProfiles:
     - Call ContainerProfiles.GetProfile(dbType)
     - Extract username from profile.EnvVars (key: "SA_PASSWORD" for MSSQL, "POSTGRES_PASSWORD" for PostgreSQL, "MYSQL_ROOT_PASSWORD" for MySQL)
     - Extract password from profile.EnvVars
     - Determine port: 1433 for MSSQL, 5432 for PostgreSQL, 3306 for MySQL
  3. Serialize schema to JSON using JsonSerializer.Serialize(schema)
  4. Construct system prompt: "You are a Python debugging expert. Fix the provided Streamlit code based on error logs."
  5. Construct user prompt with sections:
     - "Original Code:" followed by originalCode
     - "Error Logs:" followed by errorLogs
     - "Database Schema:" followed by schemaJson
     - "Database Connection Parameters:" with hostname="db", port, username, password, database="naminesdb"
     - "Fix the code and return ONLY the corrected Python code without markdown."
  6. Call Groq API with retry logic (similar to GenerateStreamlitAppAsync):
     - Use model "llama-3.3-70b-versatile"
     - Temperature: 0.2 for deterministic fixes
     - Max retries: 3
     - Timeout: 60 seconds per attempt
  7. Parse response, clean markdown code blocks (remove ```python and ```), return corrected code
  8. On API failure after 3 retries, throw exception with message "Groq API failed to generate fixed code after 3 attempts"
- **Test Strategy**:
  - Unit test: Mock Groq API to return corrected code → Verify markdown cleaned → Verify code returned
  - Unit test: Mock Groq API to fail 3 times → Verify exception thrown with retry message
  - Unit test: Provide originalCode with "SERVER=localhost" and errorLogs with "TCP Provider: Error" → Verify prompt contains "Hostname: db"
  - Integration test: Call with real syntax error → Verify Groq returns corrected code
- _Requirements: 2.3, 2.7_

### 8. Implement OllamaAIService.FixStreamlitAppAsync

- **Affected Files**: `Namines.Infrastructure/AI/OllamaAIService.cs`
- **Dependencies**: Task 3 (IAIService interface updated)
- **Acceptance Criteria**:
  1. Implement FixStreamlitAppAsync method matching interface signature
  2. Use identical prompt engineering as GroqAIService (same system prompt, user prompt structure, connection parameters)
  3. Call Ollama /api/chat endpoint with:
     - Model: "qwen2.5-coder"
     - Temperature: 0.2 for deterministic fixes
     - Stream: false
     - Max retries: 3
     - Timeout: 60 seconds per attempt
  4. Parse JSON response, extract "message.content" field, clean markdown, return corrected code
  5. On API failure after 3 retries, throw exception with message "Ollama API failed to generate fixed code after 3 attempts"
- **Test Strategy**:
  - Unit test: Mock Ollama API to return corrected code → Verify markdown cleaned → Verify code returned
  - Unit test: Mock Ollama API to fail 3 times → Verify exception thrown with retry message
  - Unit test: Verify prompt structure matches GroqAIService (same sections, same connection parameters)
  - Integration test: Call with real import error → Verify Ollama returns corrected code with fixed import
- _Requirements: 2.3, 2.7_

---

## Phase 5: Controller Updates

### 9. Update CoderAIController to pass schema parameter

- **Affected Files**: `Namines.API/Controllers/CoderAIController.cs`
- **Dependencies**: Task 4 (IDockerService interface updated), Task 6 (DockerService implementation updated)
- **Acceptance Criteria**:
  1. Locate RunDualSandboxAsync call in GenerateSandbox background task
  2. Update call to pass request.Schema as final parameter:
     ```csharp
     var result = await dockerService.RunDualSandboxAsync(
         jobId, sqlContent, appPyContent, request.DbType, log =>
     {
         _logger.LogInformation("CoderAI [{JobId}] Docker: {Log}", jobId, log);
         _jobManager.AddLog(jobId, log);
     }, request.Schema); // Pass schema
     ```
  3. Verify request.Schema is available (should be populated by earlier AI service call)
  4. No other changes to controller logic (exception handling, job completion, SSE streaming remain unchanged)
- **Test Strategy**:
  - Unit test: Mock dockerService.RunDualSandboxAsync → Verify schema parameter passed correctly
  - Integration test: Trigger CoderAI job with valid schema → Verify schema passed to DockerService → Verify deployment succeeds
  - Integration test: Trigger CoderAI job with faulty code → Verify retry mechanism triggers → Verify schema passed to AI fix service
- _Requirements: 2.3_

---

## Phase 6: Dependency Injection

### 10. Update ServiceCollectionExtensions for DI registration

- **Affected Files**: `Namines.API/Extensions/ServiceCollectionExtensions.cs` (or `Namines.Infrastructure/Extensions/ServiceCollectionExtensions.cs`)
- **Dependencies**: Task 6 (DockerService constructor updated to require IAIFactory)
- **Acceptance Criteria**:
  1. Locate IDockerService registration (likely `services.AddScoped<IDockerService, DockerService>();`)
  2. Verify IAIFactory is already registered (should be registered for existing AI service usage)
  3. If IAIFactory not registered, add registration: `services.AddSingleton<IAIFactory, AIFactory>();`
  4. Ensure DockerService can resolve IAIFactory from DI container
  5. No other changes to service registrations
- **Test Strategy**:
  - Integration test: Start application → Verify DockerService resolves from DI container without errors
  - Integration test: Verify IAIFactory resolves from DI container
  - Integration test: Trigger CoderAI job → Verify DockerService receives IAIFactory instance → Verify AI fix service accessible
- _Requirements: 2.3_

---

## Phase 7: Post-Implementation Validation

### 11. Verify bug condition exploration test now passes

- **Property 1: Expected Behavior** - Container Crash With Retry
- **IMPORTANT**: Re-run the SAME test from task 1 - do NOT write a new test
- The test from task 1 encodes the expected behavior
- When this test passes, it confirms the expected behavior is satisfied
- Run bug condition exploration test from step 1 on FIXED code
- **EXPECTED OUTCOME**: Test PASSES (confirms bug is fixed)
- Verify test assertions:
  - Container crashes with ExitCode > 0 (still true)
  - System extracts logs using GetContainerLogsAsync (NOW true)
  - System calls FixStreamlitAppAsync with correct parameters (NOW true)
  - Retry counter increments (NOW true)
  - Deployment retries with corrected code (NOW true)
  - Operation succeeds OR fails gracefully after 3 attempts (NOW true)
- _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_

### 12. Verify preservation tests still pass

- **Property 2: Preservation** - Successful First-Attempt Deployment
- **IMPORTANT**: Re-run the SAME tests from task 2 - do NOT write new tests
- Run preservation property tests from step 2 on FIXED code
- **EXPECTED OUTCOME**: Tests PASS (confirms no regressions)
- Verify all preservation properties:
  - Successful deployments complete on first attempt (retry counter == 0)
  - No AI fix service calls for successful deployments
  - Immediate success detection (URL returned within 20 seconds)
  - Network topology preserved (database accessible via "db")
  - SSE streaming works correctly
  - Cleanup works correctly
  - Downloadable .zip package generated
  - ContainerProfiles usage unchanged
  - Existing AI service methods unchanged
- Confirm all tests still pass after fix (no regressions)
- _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8_

---

## Phase 8: Final Checkpoint

### 13. Checkpoint - Ensure all tests pass

- Run all unit tests, integration tests, and property-based tests
- Verify no compilation errors
- Verify no runtime exceptions
- Verify all acceptance criteria met
- Ask the user if questions arise
- Document any edge cases discovered during testing
- Verify retry logs format: "Attempt [N]/3: [error_category] - [error_message]"
- Verify SSE progress messages appear correctly in frontend terminal UI
- Verify downloadable .zip package still generated correctly
- Verify sandbox cleanup still works correctly

---

## Task Dependencies Graph

```
Task 1 (Exploration Test) ──┐
Task 2 (Preservation Test) ─┤
                            │
Task 3 (IAIService) ────────┼──> Task 7 (GroqAIService)
                            │    Task 8 (OllamaAIService)
                            │
Task 4 (IDockerService) ────┤
Task 5 (ContainerCrashException) ─┤
                                  │
                                  ├──> Task 6 (DockerService) ──> Task 9 (CoderAIController)
                                  │                                      │
                                  │                                      ├──> Task 10 (DI Registration)
                                  │                                      │
                                  └──────────────────────────────────────┼──> Task 11 (Verify Fix)
                                                                         │    Task 12 (Verify Preservation)
                                                                         │
                                                                         └──> Task 13 (Checkpoint)
```

---

## Implementation Notes

**Critical Reminders:**

1. **Exploration tests (Task 1) MUST be written and run on UNFIXED code BEFORE any implementation** - failures confirm the bug exists
2. **Preservation tests (Task 2) MUST be written and run on UNFIXED code to establish baseline** - passes confirm behavior to preserve
3. **Task 6 (DockerService) is the most complex** - contains retry loop, log extraction, AI fix service integration, cleanup logic
4. **Task 11 and 12 re-run the SAME tests from Task 1 and 2** - do NOT write new tests, verify existing tests now pass/still pass
5. **Retry limit is 3 attempts** - balance between reliability and performance
6. **Log extraction truncates to 10,000 characters** - prevents memory issues while providing sufficient error context
7. **AI prompt includes explicit connection parameters** - guides AI toward correct database hostname ("db")
8. **Cleanup crashed containers before retry** - prevents resource leaks and port conflicts
9. **Preserve all existing successful deployment behavior** - retry logic only triggers on crashes

**Testing Priorities:**

1. **Fix Checking (Task 11)**: Verify retry mechanism works for syntax errors, import errors, database connection errors
2. **Preservation Checking (Task 12)**: Verify successful deployments unchanged, SSE streaming works, cleanup works
3. **Edge Cases**: Test AI API failures, Docker API failures, max retry limit, unfixable errors
4. **Integration**: End-to-end tests with real Groq/Ollama APIs, real Docker containers, real database deployments
