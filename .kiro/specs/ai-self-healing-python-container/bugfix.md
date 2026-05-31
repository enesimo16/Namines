# Bugfix Requirements Document

## Introduction

The Namines CoderAI feature generates Python Streamlit applications using Groq AI and deploys them in Docker containers alongside database containers. Currently, when the AI generates faulty Python code (syntax errors, import errors, incorrect database connection strings), the Streamlit container crashes within seconds and the entire operation fails without any recovery attempt. This bug prevents the system from achieving its goal of autonomous admin panel generation, as users are left with failed deployments and no actionable feedback.

This bugfix implements an AI self-healing mechanism that detects container crashes, extracts error logs, sends them back to the AI for code correction, and retries the deployment up to 3 times before final failure.

**Impact:** High - Affects the core CoderAI feature reliability and user experience. Without self-healing, any AI-generated code error results in complete failure.

**Affected Components:**
- `Namines.Infrastructure/Services/DockerService.cs` - `RunDualSandboxAsync` method
- `Namines.Core/Interfaces/IAIService.cs` - New `FixStreamlitAppAsync` method
- `Namines.Infrastructure/AI/GroqAIService.cs` - Implementation of fix method
- `Namines.Infrastructure/AI/OllamaAIService.cs` - Implementation of fix method

## Bug Analysis

### Current Behavior (Defect)

#### Requirement 1.1: Python Code Syntax Validation and Error Recovery

**User Story:** As a user generating a Streamlit admin panel, I want the system to validate Python code syntax and retry on errors, so that I receive a working application or clear error feedback instead of a crashed container.

**Acceptance Criteria:**

1. WHEN the AI generates Python code for a Streamlit application, THE System SHALL validate the syntax using a Python syntax checker before container deployment.

2. IF Python syntax validation detects errors (missing colons, incorrect indentation, invalid tokens, or unclosed brackets), THEN THE System SHALL attempt to regenerate the code with a maximum of 2 retry attempts.

3. IF syntax validation fails after 2 retry attempts, THEN THE System SHALL terminate the operation, report an error message indicating "Python code generation failed after 3 attempts due to syntax errors", and SHALL NOT create any container.

4. WHEN a Streamlit container is started, THE System SHALL monitor container health for 30 seconds to detect runtime crashes.

5. IF the Streamlit container exits with a non-zero status code within 30 seconds of startup, THEN THE System SHALL classify this as a deployment failure, remove the container and associated network resources, and report an error message indicating "Streamlit application failed to start due to runtime error".

6. WHEN syntax validation or container deployment fails, THE System SHALL send an SSE event with error details including the failure reason and the attempt number.

7. IF all retry attempts are exhausted or container deployment fails, THEN THE System SHALL preserve the generated Python code in the job result for user inspection and debugging.

#### Requirement 1.2: Import Error Detection and Recovery

**User Story:** As a user, I want the system to detect and recover from Python import errors, so that missing or incorrect module references don't cause permanent deployment failures.

**Acceptance Criteria:**

1. WHEN a Streamlit container is started, THE system SHALL monitor container logs for 120 seconds to detect successful startup or failure.

2. IF the container exits with a non-zero exit code within 120 seconds AND container logs contain "ModuleNotFoundError", "ImportError", or "No module named", THEN THE system SHALL classify this as an import error failure.

3. IF an import error is detected, THEN THE system SHALL extract the full container logs, terminate the container, and SHALL NOT attempt any retry (0 retry attempts, 0 second timeout).

4. WHEN an import error is detected, THE system SHALL send an SSE event to the user with error details including the missing module name and the full error traceback.

5. IF the container does not exit within 120 seconds AND container logs do not contain "Network URL" or "You can now view your Streamlit app", THEN THE system SHALL classify this as a timeout failure and terminate the container.

6. WHEN a timeout failure occurs, THE system SHALL send an SSE event indicating "Container startup timeout after 120 seconds" and SHALL NOT attempt any retry.

#### Requirement 1.3: Database Connection Parameter Validation

**User Story:** As a user, I want the system to validate database connection parameters before deployment, so that incorrect configurations are caught early and corrected automatically.

**Acceptance Criteria:**

1. IF the AI generates Python code with database connection parameters where hostname is not "db", THEN THE system SHALL terminate execution with exit code non-zero and report "Database hostname validation failed: expected 'db', found '[actual_value]'" with 0 retry attempts and 0 second timeout.

2. IF the AI generates Python code with database connection parameters where port does not match the expected port for the database type (1433 for MSSQL, 5432 for PostgreSQL, 3306 for MySQL), THEN THE system SHALL terminate execution with exit code non-zero and report "Database port validation failed: expected [expected_port], found [actual_port]" with 0 retry attempts and 0 second timeout.

3. IF the AI generates Python code with database connection parameters where username or password do not match the values from ContainerProfiles, THEN THE system SHALL terminate execution with exit code non-zero and report "Database credential validation failed: [specific parameter that failed validation]" with 0 retry attempts and 0 second timeout.

4. WHEN database connection parameter validation fails, THE system SHALL send an SSE event to the user with error details including the specific parameter that failed validation and the expected vs. actual values.

5. IF the container crashes with `OperationalError: Login failed for user 'sa'` or similar database authentication errors, THEN THE system SHALL extract container logs, classify this as a database connection error, and report the error to the user via SSE event with 0 retry attempts.

#### Requirement 1.4: Readiness Probe Crash Detection and Logging

**User Story:** As a system operator, I want comprehensive crash detection and logging during the readiness probe, so that I can diagnose and fix deployment failures.

**Acceptance Criteria:**

1. WHEN the Streamlit container crashes during the readiness probe loop (defined as inspect.State.Running == false AND inspect.State.ExitCode > 0 within 120 seconds of container start), THEN THE system SHALL log the error message "HATA: Python container beklenmedik şekilde kapandı (exit=[ExitCode])".

2. WHEN a container crash is detected during the readiness probe, THEN THE system SHALL NOT extract container logs using GetContainerLogsAsync.

3. WHEN a container crash is detected during the readiness probe, THEN THE system SHALL NOT call IAIService.FixStreamlitAppAsync to attempt code correction.

4. WHEN a container crash is detected during the readiness probe, THEN THE system SHALL throw an exception with message "Python container crashed with exit code [ExitCode]".

5. WHEN the exception is thrown, THEN THE system SHALL terminate the operation and mark the job status as "Error" with the exception message.

#### Requirement 1.5: Exit Code Handling Without Root Cause Analysis

**User Story:** As a system, I want to handle container exit codes consistently, so that failures are reported uniformly.

**Acceptance Criteria:**

1. WHEN the container exits with an exit code ≥ 1, THEN THE system SHALL throw an exception with message "Python container crashed with exit code [ExitCode]".

2. WHEN the exception is thrown, THEN THE system SHALL propagate the exception to the calling method (CoderAIController.GenerateSandbox background task).

3. WHEN a container crash exception is thrown, THEN THE system SHALL NOT extract container logs using Docker.DotNet GetContainerLogsAsync.

4. WHEN a container crash exception is thrown, THEN THE system SHALL NOT call IAIService.FixStreamlitAppAsync or any other AI service method.

5. WHEN the exception reaches the CoderAIController background task, THEN THE system SHALL call _jobManager.CompleteJob(jobId, "", ex.Message) to mark the job as failed with the exception message.

6. WHEN the job is marked as failed, THEN THE system SHALL verify that the retry counter remains at 0 (no retry attempts were made).

### Expected Behavior (Correct)

#### Requirement 2.1: Container Log Extraction on Crash

**User Story:** As a system, I want to extract container logs when a crash is detected, so that error details can be analyzed and sent to the AI for code correction.

**Acceptance Criteria:**

1. WHEN the Streamlit container crashes during the readiness probe (defined as inspect.State.Running == false AND inspect.State.ExitCode > 0), THEN THE system SHALL extract container logs within 10 seconds using Docker API.

2. WHEN extracting container logs, THE system SHALL retrieve the last 1000 lines or 100,000 characters (whichever limit is reached first) from both stdout and stderr streams.

3. IF log extraction fails or times out after 10 seconds, THEN THE system SHALL proceed with the retry mechanism using an error message indicating "Failed to extract container logs: [error details]" as the error context.

#### Requirement 2.2: Container Log Parsing

**User Story:** As a system, I want to parse extracted container logs to identify specific error types, so that the AI receives categorized error information for more accurate fixes.

**Acceptance Criteria:**

1. WHEN container logs are extracted after a crash, THE system SHALL parse the logs within 30 seconds to extract error messages categorized as syntax errors, import errors, database connection errors, or runtime exceptions.

2. WHEN container logs are parsed successfully, THE system SHALL return a structured list containing each identified error with its category and log line number.

3. IF log parsing fails or times out after 30 seconds, THEN THE system SHALL return an error indication specifying the parsing failure.

4. IF extracted container logs are empty or contain no recognizable error patterns, THEN THE system SHALL return an empty error list with a status indicating no errors were identified.

#### Requirement 2.3: AI Fix Service Invocation

**User Story:** As a system, I want to invoke the AI fix service with comprehensive error context, so that the AI can generate corrected Python code.

**Acceptance Criteria:**

1. WHEN error logs are identified from Docker container execution, THE system SHALL call IAIService.FixStreamlitAppAsync with parameters (originalCode, errorLogs, schema, dbType) within 60 seconds.

2. IF the AI service call fails or times out after 60 seconds, THEN THE system SHALL increment the retry counter and retry the fix attempt up to 2 additional times (3 total attempts).

3. IF all 3 AI service call attempts fail, THEN THE system SHALL terminate the operation with an error message indicating "AI service failed to generate fixed code after 3 attempts".

4. WHEN calling FixStreamlitAppAsync, THE system SHALL limit input sizes to 10,000 characters for errorLogs and 100,000 characters for originalCode.

5. IF input size limits are exceeded, THEN THE system SHALL truncate the inputs to the specified limits and log a warning message indicating truncation occurred.

#### Requirement 2.4: Retry Deployment with Corrected Code

**User Story:** As a system, I want to retry deployment with AI-corrected code, so that transient code errors can be automatically resolved.

**Acceptance Criteria:**

1. WHEN the AI returns corrected code, THE system SHALL increment the retry counter by 1.

2. WHEN the retry counter is incremented, THE system SHALL remove the crashed container and its associated volumes.

3. WHEN the crashed container is removed, THE system SHALL redeploy the Streamlit container using the corrected code with the same database configuration (network, environment variables, port bindings).

4. IF the AI service returns an error or null response, THEN THE system SHALL terminate the operation with an error message indicating "AI service failed to generate corrected code" and SHALL NOT increment the retry counter or attempt redeployment.

#### Requirement 2.5: Maximum Retry Limit Enforcement

**User Story:** As a system, I want to enforce a maximum retry limit, so that infinite retry loops are prevented and users receive clear failure feedback.

**Acceptance Criteria:**

1. WHEN the retry counter reaches 3 attempts, THE system SHALL terminate the operation and return an error response to the caller.

2. WHEN the operation is terminated due to max retries, THE system SHALL include in the error response: (a) total number of attempts (3), (b) final error message from the last attempt, (c) retry logs containing attempt number, error category, and error message for each attempt.

3. WHEN retry logs are included in the error response, THE system SHALL format each retry log entry as: "Attempt [N]/3: [error_category] - [error_message]" where N is the attempt number (1, 2, or 3).

#### Requirement 2.6: Successful Container Startup Detection

**User Story:** As a system, I want to detect successful Streamlit container startup, so that I can complete the operation and return the application URL to the user.

**Acceptance Criteria:**

1. WHEN the Streamlit container is started, THE system SHALL verify that the container state is Running (inspect.State.Running == true).

2. WHEN the container state is Running, THE system SHALL search the last 100 lines of container logs for the text "Network URL" or "You can now view your Streamlit app" within 120 seconds.

3. IF the success indicator text is found in the logs, THEN THE system SHALL extract the Streamlit URL in the format "http://localhost:[port]" where [port] is the dynamically assigned external port.

4. WHEN the Streamlit URL is extracted, THE system SHALL return a success result to the caller containing the URL and SHALL NOT increment the retry counter.

5. IF the success indicator text is not found within 120 seconds, THEN THE system SHALL classify this as a timeout failure and proceed with the retry mechanism.

#### Requirement 2.7: AI Fix Prompt Content

**User Story:** As the self-healing system, I want to provide comprehensive error context to the AI, so that it can generate accurate code fixes.

**Acceptance Criteria:**

1. WHEN calling `FixStreamlitAppAsync`, THE system SHALL provide a prompt containing: (a) the original faulty Python code that was deployed to the container, (b) the error logs from the container limited to the last 500 lines or 10,000 characters (whichever is smaller), (c) the database schema serialized as JSON including table names, column names, data types, and primary key constraints, (d) the database connection parameters retrieved from ContainerProfiles including hostname set to "db", port number matching the database type (1433 for MSSQL, 5432 for PostgreSQL, 3306 for MySQL), username, and password.

2. IF ContainerProfiles returns null or missing connection parameters for the specified database type, THEN THE system SHALL fail the fix attempt with an error message indicating "Unable to retrieve database connection parameters for [dbType]" and SHALL NOT call FixStreamlitAppAsync.

#### Requirement 2.8: Python Code Syntax Validation

**User Story:** As a system, I want to validate AI-generated Python code syntax before deployment, so that syntactically invalid code is rejected.

**Acceptance Criteria:**

1. WHEN the AI generates fixed code, THE system SHALL validate the code using Python's ast.parse() function to verify syntactic correctness.

2. IF the syntax validation fails, THEN THE system SHALL reject the deployment attempt and return an error message indicating the syntax error location and description.

3. IF the syntax validation succeeds, THEN THE system SHALL proceed with the deployment process.

### Unchanged Behavior (Regression Prevention)

#### Requirement 3.1: Successful First-Attempt Deployment

**User Story:** As a user, I want successful deployments to complete without unnecessary retry overhead, so that system performance is not degraded.

**Acceptance Criteria:**

1. WHEN the AI generates correct Python code on the first attempt (code that starts successfully and logs contain "Network URL" within 120 seconds), THE system SHALL deploy the container successfully without triggering any retry logic.

2. WHEN a first-attempt deployment succeeds, THE system SHALL verify that the retry counter remains at 0.

3. WHEN a first-attempt deployment succeeds, THE system SHALL NOT call IAIService.FixStreamlitAppAsync.

#### Requirement 3.2: Immediate Success Detection

**User Story:** As a user, I want successful deployments to be detected immediately, so that I can access my application without unnecessary delays.

**Acceptance Criteria:**

1. WHEN the Streamlit container starts successfully AND the readiness probe detects "Network URL" in logs within the first 10 attempts (20 seconds), THE system SHALL return the Streamlit URL immediately.

2. WHEN the Streamlit URL is returned immediately, THE system SHALL NOT wait for the full 60-attempt timeout (120 seconds).

#### Requirement 3.3: Network Topology Preservation

**User Story:** As a system, I want to maintain the existing dual sandbox network topology, so that database connectivity remains consistent.

**Acceptance Criteria:**

1. WHEN the dual sandbox network, database container, and file bindings are configured, THE system SHALL use the existing network topology where the database container is accessible via hostname "db".

2. WHEN the dual sandbox is configured, THE system SHALL mount the app.py file to the /app directory in the Streamlit container.

3. WHEN the dual sandbox is configured, THE system SHALL connect both containers to the same Docker network (namines-net-[jobId]).

#### Requirement 3.4: Downloadable Package Generation

**User Story:** As a user, I want to download my generated admin panel as a .zip package, so that I can deploy it independently.

**Acceptance Criteria:**

1. WHEN the CoderAI job completes successfully, THE system SHALL generate a downloadable .zip package containing app.py, schema.sql, and requirements.txt.

2. WHEN the .zip package is generated, THE system SHALL store it in the Outputs directory with filename "coderai_[jobId].zip".

3. WHEN the .zip package is stored, THE system SHALL set the job download URL to "/api/coderai/download/[jobId]".

#### Requirement 3.5: Real-Time SSE Log Streaming

**User Story:** As a user, I want to see real-time progress logs in the terminal UI, so that I can monitor the deployment process.

**Acceptance Criteria:**

1. WHEN the frontend SSE stream receives progress logs from the backend, THE system SHALL display them in real-time in the terminal UI component.

2. WHEN progress logs are displayed, THE system SHALL preserve the log order and formatting.

#### Requirement 3.6: Sandbox Cleanup

**User Story:** As a user, I want to clean up sandbox resources when I'm done, so that system resources are freed.

**Acceptance Criteria:**

1. WHEN the user clicks "Sandbox'ı Kapat" button, THE system SHALL call CleanupSandboxAsync with the jobId.

2. WHEN CleanupSandboxAsync is called, THE system SHALL remove all containers (database and Streamlit), networks, and temporary files associated with the jobId.

#### Requirement 3.7: Database Profile Configuration

**User Story:** As a system, I want to use ContainerProfiles for database configuration, so that database-specific settings are centralized and consistent.

**Acceptance Criteria:**

1. WHEN the system uses ContainerProfiles.GetProfile(dbType), THE system SHALL retrieve the correct database image, credentials, and connection parameters for MSSQL, PostgreSQL, and MySQL.

2. WHEN ContainerProfiles returns database configuration, THE system SHALL use the returned values for container creation without modification.

#### Requirement 3.8: AI Service Method Preservation

**User Story:** As a system, I want existing AI service methods to remain unchanged, so that schema generation, revision, and mock data features continue to work.

**Acceptance Criteria:**

1. WHEN the GroqAIService or OllamaAIService is used for schema generation (GenerateSchemaAsync), THE system SHALL function without any changes to the method implementation.

2. WHEN the GroqAIService or OllamaAIService is used for schema revision (ReviseSchemaAsync), THE system SHALL function without any changes to the method implementation.

3. WHEN the GroqAIService or OllamaAIService is used for mock data generation (GenerateMockDataAsync), THE system SHALL function without any changes to the method implementation.

4. WHEN the GroqAIService or OllamaAIService is used for project summary generation (GenerateProjectSummaryAsync), THE system SHALL function without any changes to the method implementation.

---

## Bug Condition Derivation

### Bug Condition Function

```pascal
FUNCTION isBugCondition(X)
  INPUT: X of type StreamlitDeployment
  OUTPUT: boolean
  
  // Returns true when the Streamlit container crashes during deployment
  RETURN (X.containerState.Running = false) AND 
         (X.containerState.ExitCode > 0) AND
         (X.deploymentPhase = "READINESS_PROBE")
END FUNCTION
```

### Property Specification (Fix Checking)

```pascal
// Property: Self-Healing Retry Mechanism
FOR ALL X WHERE isBugCondition(X) DO
  logs ← ExtractContainerLogs(X.containerId)
  fixedCode ← AI.FixStreamlitAppAsync(X.originalCode, logs, X.schema, X.dbType)
  X.retryCount ← X.retryCount + 1
  
  IF X.retryCount <= 3 THEN
    CleanupContainer(X.containerId)
    result ← RetryDeployment(fixedCode, X.schema, X.dbType)
    ASSERT (result.success = true) OR (isBugCondition(result) AND result.retryCount < 3)
  ELSE
    ASSERT result.status = "ERROR" AND 
           result.errorMessage CONTAINS "Maximum retry attempts (3) exceeded"
  END IF
END FOR
```

### Preservation Goal

```pascal
// Property: Preservation Checking
FOR ALL X WHERE NOT isBugCondition(X) DO
  // X.containerState.Running = true (container starts successfully)
  ASSERT F(X) = F'(X)
  // Where F is the original RunDualSandboxAsync and F' is the fixed version
  // Successful deployments should behave identically
END FOR
```

### Counterexample

**Concrete Example Demonstrating the Bug:**

```
Input:
  - Schema: { Name: "TestDB", Tables: [{ Name: "Users", Columns: [...] }] }
  - DbType: MSSQL
  - AI-Generated Code: 
      ```python
      import streamlit as st
      import pyodbc
      
      conn = pyodbc.connect(
          "DRIVER={ODBC Driver 18 for SQL Server};"
          "SERVER=localhost;"  # ❌ BUG: Should be "db"
          "DATABASE=naminesdb;"
          "UID=sa;"
          "PWD=Namines_Secure123!;"
      )
      ```

Current Behavior:
  1. Container starts
  2. Container crashes after 2 seconds with:
     ```
     pyodbc.OperationalError: ('08001', '[08001] [Microsoft][ODBC Driver 18 for SQL Server]
     TCP Provider: Error code 0x2746 (10054) (SQLDriverConnect)')
     ```
  3. System logs: "HATA: Python container beklenmedik şekilde kapandı (exit=1)"
  4. Operation fails, user sees error in SSE stream
  5. No retry, no log analysis, no AI correction

Expected Behavior (After Fix):
  1. Container starts
  2. Container crashes after 2 seconds
  3. System detects crash, extracts logs
  4. System calls AI.FixStreamlitAppAsync with error logs
  5. AI returns corrected code with "SERVER=db;"
  6. System retries deployment (attempt 2/3)
  7. Container starts successfully
  8. Streamlit URL returned to user
```

---

## Implementation Notes

**Key Design Decisions:**

1. **Retry Limit:** Maximum 3 retry attempts to balance reliability vs. performance
2. **Log Extraction:** Use `Docker.DotNet` `GetContainerLogsAsync` with `Tail="all"` to capture complete error context
3. **AI Prompt Engineering:** Include database connection parameters explicitly in the fix prompt to guide the AI toward correct solutions
4. **Graceful Degradation:** After 3 failed attempts, provide detailed error logs to the user for manual debugging
5. **Backward Compatibility:** Wrap retry logic in a try-catch to ensure existing successful deployments are not affected

**Testing Strategy:**

- **Fix Checking:** Manually inject faulty Python code (syntax error, import error, wrong hostname) and verify retry mechanism triggers
- **Preservation Checking:** Deploy with correct AI-generated code and verify no retry logic is triggered
- **Edge Cases:** Test with network failures, AI API failures, and container resource limits
