using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using TaskFlow.Api.Models;

using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

// Configuración de JWT
var jwtKey = "EstaEsUnaClaveSuperSecretaDeAlMenos32Caracteres!!";
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

var connectionString = "Data Source=tareas.db";

// Crear tablas si no existen
using (var connection = new SqliteConnection(connectionString))
{
    connection.Open();

    // Tabla de Usuarios
    var cmdUsuarios = connection.CreateCommand();
    cmdUsuarios.CommandText = @"
        CREATE TABLE IF NOT EXISTS Usuarios (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Email TEXT NOT NULL UNIQUE,
            Password TEXT NOT NULL
        )";
    cmdUsuarios.ExecuteNonQuery();

    // Tabla de Tareas
    var cmdTareas = connection.CreateCommand();
    cmdTareas.CommandText = @"
        CREATE TABLE IF NOT EXISTS Tareas (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Titulo TEXT NOT NULL,
            Descripcion TEXT,
            Estado TEXT NOT NULL,
            FechaCreacion TEXT NOT NULL
        )";
    cmdTareas.ExecuteNonQuery();
}

// ==================== AUTENTICACIÓN ====================

// Registro de usuario
app.MapPost("/api/auth/register", (UsuarioRegister request) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest("Email y Password son obligatorios");

    using var connection = new SqliteConnection(connectionString);
    connection.Open();

    // Verificar si el email ya existe
    var checkCmd = connection.CreateCommand();
    checkCmd.CommandText = "SELECT COUNT(*) FROM Usuarios WHERE Email = $email";
    checkCmd.Parameters.AddWithValue("$email", request.Email);
    var exists = Convert.ToInt32(checkCmd.ExecuteScalar()) > 0;

    if (exists)
        return Results.BadRequest("El email ya está registrado");

    // Insertar usuario
    var insertCmd = connection.CreateCommand();
    insertCmd.CommandText = "INSERT INTO Usuarios (Email, Password) VALUES ($email, $password)";
    insertCmd.Parameters.AddWithValue("$email", request.Email);
    insertCmd.Parameters.AddWithValue("$password", request.Password); // En producción se hashea
    insertCmd.ExecuteNonQuery();

    return Results.Ok(new { message = "Usuario registrado correctamente" });
});

// Login
app.MapPost("/api/auth/login", (UsuarioLogin request) =>
{
    using var connection = new SqliteConnection(connectionString);
    connection.Open();

    var command = connection.CreateCommand();
    command.CommandText = "SELECT Id, Email FROM Usuarios WHERE Email = $email AND Password = $password";
    command.Parameters.AddWithValue("$email", request.Email);
    command.Parameters.AddWithValue("$password", request.Password);

    using var reader = command.ExecuteReader();
    if (!reader.Read())
        return Results.Unauthorized();

    var userId = reader.GetInt32(0);
    var email = reader.GetString(1);

    // Generar token JWT
    var tokenHandler = new JwtSecurityTokenHandler();
    var key = Encoding.UTF8.GetBytes(jwtKey);
    var tokenDescriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, email)
        }),
        Expires = DateTime.UtcNow.AddHours(8),
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
    };

    var token = tokenHandler.CreateToken(tokenDescriptor);
    var tokenString = tokenHandler.WriteToken(token);

    return Results.Ok(new { token = tokenString });
});

// ==================== TAREAS ====================

app.MapGet("/api/tareas", () =>
{
    var tareas = new List<Tarea>();
    using var connection = new SqliteConnection(connectionString);
    connection.Open();

    var command = connection.CreateCommand();
    command.CommandText = "SELECT Id, Titulo, Descripcion, Estado, FechaCreacion FROM Tareas";

    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
        tareas.Add(new Tarea
        {
            Id = reader.GetInt32(0),
            Titulo = reader.GetString(1),
            Descripcion = reader.IsDBNull(2) ? null : reader.GetString(2),
            Estado = reader.GetString(3),
            FechaCreacion = DateTime.Parse(reader.GetString(4))
        });
    }

    return Results.Ok(tareas);
}) .RequireAuthorization();

app.MapPost("/api/tareas", (Tarea nuevaTarea) =>
{
    if (string.IsNullOrWhiteSpace(nuevaTarea.Titulo))
        return Results.BadRequest("El título es obligatorio");

    using var connection = new SqliteConnection(connectionString);
    connection.Open();

    var command = connection.CreateCommand();
    command.CommandText = @"
        INSERT INTO Tareas (Titulo, Descripcion, Estado, FechaCreacion)
        VALUES ($titulo, $descripcion, $estado, $fecha);
        SELECT last_insert_rowid();";

    command.Parameters.AddWithValue("$titulo", nuevaTarea.Titulo);
    command.Parameters.AddWithValue("$descripcion", (object?)nuevaTarea.Descripcion ?? DBNull.Value);
    command.Parameters.AddWithValue("$estado", nuevaTarea.Estado);
    command.Parameters.AddWithValue("$fecha", DateTime.Now.ToString("o"));

    var id = Convert.ToInt32(command.ExecuteScalar());
    nuevaTarea.Id = id;
    nuevaTarea.FechaCreacion = DateTime.Now;

    return Results.Created($"/api/tareas/{id}", nuevaTarea);
}).RequireAuthorization();

// PUT: Actualizar una tarea
app.MapPut("/api/tareas/{id}", (int id, Tarea tareaActualizada) =>
{
    using var connection = new SqliteConnection(connectionString);
    connection.Open();

    var command = connection.CreateCommand();
    command.CommandText = @"
        UPDATE Tareas 
        SET Titulo = $titulo, 
            Descripcion = $descripcion, 
            Estado = $estado
        WHERE Id = $id";

    command.Parameters.AddWithValue("$id", id);
    command.Parameters.AddWithValue("$titulo", tareaActualizada.Titulo);
    command.Parameters.AddWithValue("$descripcion", (object?)tareaActualizada.Descripcion ?? DBNull.Value);
    command.Parameters.AddWithValue("$estado", tareaActualizada.Estado);

    var filasAfectadas = command.ExecuteNonQuery();

    if (filasAfectadas == 0)
        return Results.NotFound("Tarea no encontrada");

    return Results.Ok(new { message = "Tarea actualizada correctamente" });
}).RequireAuthorization();

// DELETE: Eliminar una tarea
app.MapDelete("/api/tareas/{id}", (int id) =>
{
    using var connection = new SqliteConnection(connectionString);
    connection.Open();

    var command = connection.CreateCommand();
    command.CommandText = "DELETE FROM Tareas WHERE Id = $id";
    command.Parameters.AddWithValue("$id", id);

    var filasAfectadas = command.ExecuteNonQuery();

    if (filasAfectadas == 0)
        return Results.NotFound("Tarea no encontrada");

    return Results.Ok(new { message = "Tarea eliminada correctamente" });
}).RequireAuthorization();

app.Run();

// Clases auxiliares
public class UsuarioRegister
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class UsuarioLogin
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

