# Namines - AI-Powered Interactive Database Architecture Builder

Namines is a state-of-the-art, AI-powered interactive database schema creation and compilation platform designed for modern software developers, database architects, and data engineers. It transforms natural language instructions or voice commands into fully-fledged DDL scripts, Entity Framework Core models, and comprehensive PDF data dictionaries in seconds.

## Key Features

- **AI-Driven Schema Generation**: Design your database using natural language (text or voice) powered by Groq (Cloud) or Ollama (Local models).
- **Interactive React Flow Canvas**: Visualize the generated database schema in a visual, drag-and-drop interface, reposition tables (nodes), and manage relationships (edges) dynamically.
- **Regional Prompting / Targeted Revisions**: Request specific changes targeting only a subset of tables (e.g., "Add audit log columns to this table") instead of rebuilding the entire schema.
- **Real-Time Database Rule Engine (Linter)**: Instantly detect issues or type mismatches in relationships (e.g., linking a VARCHAR Primary Key to an INT Foreign Key).
- **Advanced Export Options (Compiler)**:
  - Optimized DDL (SQL) scripts for Microsoft SQL Server, PostgreSQL, and MySQL.
  - Ready-to-use Entity Framework Core DbContext and Model classes packaged in a single ZIP archive.
  - Comprehensive Database Dictionary documents generated using QuestPDF.
  - Interactive Mermaid ER Diagrams and project markdown documentation.
- **Docker Sandbox Integration**: Execute and test the generated SQL scripts inside an isolated Docker container, streaming output in real-time and providing database backup archives (.tar / .bak).
- **Zustand & IndexedDB Cloud Sync**: Automatically persists schemas, branch history, and chat logs inside local browser IndexedDB to prevent data loss.

## Tech Stack & Architecture

Namines adheres strictly to Clean Architecture principles to ensure modularity, testability, and high maintainability:

### Backend (.NET 8 Web API)
- **Namines.API**: Presentation layer containing RESTful controllers and Server-Sent Events (SSE) for streaming Docker container execution logs.
- **Namines.Core**: Enterprise domain models (SchemaTable, SchemaColumn), service interfaces, and AI prompt builders.
- **Namines.Infrastructure**: Adapters for external dependencies including Groq API client, Ollama API integration, Docker.DotNet engine communication, DDL generators, and QuestPDF export services.

### Frontend (Next.js 16 - App Router)
- **Styling & Layout**: Tailwind CSS custom styling, modern glassmorphism design variables, and dynamic particle systems.
- **State Management**: Zustand-powered centralized store managing schema configurations, custom nodes/edges, branch history, and AI session states.
- **Visualization**: Custom React Flow rendering engine for high-performance interactive diagrams.
- **Voice Integration**: Native MediaRecorder API capture, converted and sent to Whisper model for fast transcription.

## Getting Started

### Prerequisites
- Docker Engine & Docker Compose (required for the sandbox and containerized features).
- Node.js 18+ (for local frontend development).
- .NET 8 SDK (for local backend development).
- Groq API Key (for cloud-based AI generation).
- *Optional*: Ollama running on `localhost:11434` for local AI models (such as `qwen2.5-coder` or `deepseek-coder`).

### Single-Command Setup via Docker Compose

1. Create a `.env` file in the root directory:
   ```env
   GROQ_API_KEY=your_groq_api_key_here
   JWT_KEY=a_secure_jwt_key_of_at_least_32_characters
   ```

2. Build and launch the containerized application:
   ```bash
   docker compose up --build
   ```

3. Open your browser and navigate to: [http://localhost:3000](http://localhost:3000)

Note: The initial build might take 3-5 minutes to download and cache images. Subsequent startups will take under 30 seconds.

### Manual Local Development Setup

#### Backend Setup:
1. Populate your API Key configuration in `backend/Namines.API/appsettings.json` under `Groq:ApiKey` or specify it as an environment variable `GROQ_API_KEY`.
2. Open a terminal, restore dependencies, and launch the Web API:
   ```bash
   cd backend
   dotnet restore
   dotnet run --project Namines.API
   ```
   The backend API service will listen on `http://localhost:5000`.

#### Frontend Setup:
1. Navigate to the frontend directory and install the necessary dependencies:
   ```bash
   cd frontend
   npm install
   ```
2. Start the local development server:
   ```bash
   npm run dev
   ```
   The frontend application will be served at `http://localhost:3000`.

## Quality Assurance & Audit Notes
The application codebase has undergone a comprehensive code quality and resources audit:
- Strong resource management using explicit `using` statements for all unmanaged resources (e.g., zip streams, docker tar streams).
- High reliability via global `ExceptionMiddleware` in the API layer.
- Strictly clean compile phase with zero compiler warnings and zero TypeScript errors.
- Enterprise-grade fallback engines ensuring continuous operation even when API rate limits are reached.
