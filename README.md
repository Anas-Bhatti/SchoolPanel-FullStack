# SchoolPanel 🎓

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet)
![Angular](https://img.shields.io/badge/Angular-DD0031?style=for-the-badge&logo=angular)
![SQL Server](https://img.shields.io/badge/SQL_Server-CC2927?style=for-the-badge&logo=microsoft-sql-server)
![License](https://img.shields.io/badge/License-Proprietary-blue?style=for-the-badge)

**SchoolPanel** is a scalable, multi-phase commercial school management dashboard designed to streamline educational administration. Developed by **ANAXION TECHNOLOGIES**, it features a robust backend architecture and a highly responsive, real-time frontend UI.

---

## 🚀 Key Features

* **Secure Authentication:** Role-based access control (RBAC) utilizing JWT authentication and two-factor verification.
* **Comprehensive Student Management:** End-to-end CRUD operations for student records, enrollment, and profile management.
* **Financial Module:** Centralized fee processing, pdf receipt generation, and payment tracking.
* **Attendance Tracking:** Real-time logging and monitoring of student and staff attendance.
* **Advanced Reporting:** Exportable, data-driven reports and analytics for administrative oversight.
* **Real-Time Notifications:** System-wide alerts and updates via web sockets.
* **File Handling:** Integrated Blob storage and Excel data import/export capabilities.

## 🛠️ Technology Stack

### Backend
* **Framework:** C# / ASP.NET Core Web API
* **Architecture:** Repository Pattern, Custom Middleware (Audit Logging, Exception Handling)
* **Database:** Microsoft SQL Server (Highly optimized stored procedures & relational architecture)
* **Security:** JWT Tokens, Login Lockout Filters, Strict CORS/App Options

### Frontend
* **Framework:** Angular
* **Styling:** SCSS, Custom Global Theme
* **State Management:** RxJS, custom API interceptors (JWT & Loading)
* **UI/UX:** Responsive Shell Component, dynamic sidebars, and custom toast notifications.

## 📁 Repository Structure

This project utilizes a clean **Monorepo** approach to keep the frontend and backend tightly synchronized.

```text
SchoolPanel/
├── backend/
│   ├── SchoolPanel.Api/         # Core API logic, Controllers, DTOs, Services
│   └── SchoolPanel.sln          # Visual Studio Solution File
├── frontend/
│   ├── src/app/                 # Angular modules (Auth, Dashboard, Students, etc.)
│   ├── angular.json             # Angular workspace configuration
│   └── package.json             # Node dependencies
└── .gitignore                   # Monorepo optimization rules