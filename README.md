# UserManagementAPI
Project： Building a Simple API with Copilot

# User API (.NET 8)

This is a simple User Management API built with .NET 8, featuring authentication using JWT, logging with Serilog, and in-memory database storage.

## Features
- User authentication with JWT
- Logging using Serilog
- Middleware for request/response logging, error handling, and authentication
- In-memory database using Entity Framework Core
- Use middleware to check the number of requests and reject new requests if it exceeds the set limit
- RESTful API endpoints for managing users

## Prerequisites
- .NET 8 SDK
- Git

## Installation

1. **Clone the repository**:
   ```sh
   git clone https://github.com/LeiMuu/UserManagementAPI.git
   cd UserManagementAPI
   ```

2. **Run the application**:
   ```sh
   dotnet dev-certs https --trust
   dotnet run
   ```

3. **Access Swagger UI**:
   Open your browser and go to:
   ```
   https://localhost:44395/swagger
   ```

## API Endpoints

### Authentication
- **Login**: `POST /auth/login`
  ```json
  {
    "username": "testuser",
    "password": "password123"
  }
  ```
  Response:
  ```json
  {
    "token": "your_jwt_token"
  }
  ```

### User Management (Requires JWT Token)
- **Get all users**: `GET /users`
- **Get user by ID**: `GET /users?id={id}`
- **Create user**: `POST /users`
- **Update user**: `PUT /users/{id}`
- **Delete user**: `DELETE /users/{id}`

## Middleware Setup Order
1. Error Handling Middleware
2. Authentication Middleware
3. Logging Middleware

## Logging
Logs are written to `logs/app.log`. To ignore logs in Git, add `logs/` to `.gitignore`.