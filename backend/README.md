# Gemini Project Context: SchoolPanel Backend

This document provides an overview of the SchoolPanel backend project to guide future development and analysis.

## Project Overview

This is an ASP.NET Core 8 Web API that serves as the backend for the SchoolPanel school management system. It follows a traditional, controller-based architecture and relies on Dapper for data access, preferring stored procedures for database interactions.

### Key Technologies

*   **Framework:** ASP.NET Core 8
*   **Database Access:** Dapper with ADO.NET (no Entity Framework Core)
*   **Authentication:** JWT Bearer tokens (HS512) with support for Google OAuth.
*   **Logging:** Serilog with sinks for console and file.
*   **API Documentation:** Swashbuckle (Swagger)
*   **File Storage:** Azure Blob Storage for uploads (e.g., student photos).
*   **Reporting:** QuestPDF for PDF generation and ClosedXML for Excel exports.
*   **Database:** SQL Server (inferred from connection string and health checks).

### Architecture

The project is structured in a conventional ASP.NET Core way:

*   **`Controllers`**: Feature-based API endpoints (e.g., `StudentsController`, `FeesController`). The project explicitly uses controllers over Minimal APIs for better organization of complex endpoints.
*   **`Services`**: Contains business logic decoupled from the controllers (e.g., `TokenService`, `ReportService`, `BlobStorageService`).
*   **`Repositories`**: Data access layer that uses Dapper to interact with the database, often by calling stored procedures.
*   **`Middleware`**: Custom middleware for tasks like exception handling and audit logging.
*   **`Extensions`**: Extension methods for configuring services in `Program.cs`.
*   **`Program.cs`**: The application's entry point, which bootstraps and configures all services and the middleware pipeline in a specific, well-documented order.
*   **`appsettings.json`**: Configuration for the application, including database connection strings, JWT settings, and Serilog configuration.

## Building and Running

The project is a standard .NET project.

*   **Run from an IDE (Visual Studio, Rider):** Open the `.sln` file and run the `SchoolPanel.Api` project. This is the recommended approach as it handles all dependencies and build steps.
*   **Run from the CLI:**
    ```bash
    cd backend/SchoolPanel/SchoolPanel.Api
    dotnet run
    ```
    The API will be available at the URL specified in `Properties/launchSettings.json`, typically `https://localhost:7053` or `http://localhost:5053`.

### Database Setup

The application requires a SQL Server database. The connection string must be configured in `appsettings.Development.json`:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=YourServer;Database=SchoolPanelDb;User Id=YourUser;Password=YourPassword;"
}
```

The database schema is managed via stored procedures, which are not included in this repository.

## Development Conventions

*   **Data Access**: Use Dapper and raw SQL queries, preferably by calling stored procedures (`sp_*`). Avoid using Entity Framework Core.
*   **Controllers**: New features should be added as new controllers. Endpoints should be clearly documented using Swagger attributes (`[ProducesResponseType]`, `[Summary]`).
*   **Error Handling**: The application uses a custom `ExceptionMiddleware` to handle exceptions and returns standardized `ProblemDetails` responses (RFC 7807).
*   **Configuration**: Use the `IOptions<T>` pattern for strongly-typed configuration.
*   **Security**: Routes are protected using JWT authentication and authorization policies. The `[Authorize]` attribute should be used on controllers and actions.
*   **Logging**: Use Serilog for structured logging. Inject `ILogger<T>` into services and controllers.
*   **File Naming**: C# files follow standard .NET conventions (e.g., `MyService.cs`).
*   **Styling**: The project uses comments and region blocks to structure code within files.
