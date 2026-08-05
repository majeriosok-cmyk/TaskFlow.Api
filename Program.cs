using Microsoft.Data.Sqlite;
using TaskFlow.Api.Models;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseHttpsRedirection();

// Base de datos en la raíz del proyecto (más simple)
var connectionString = "Data Source=tareas.db";

// Crear la tabla si no existe
using (var connection = new SqliteConnection(connectionString))
{
    connection.Open();
    var command = connection.CreateCommand();
    command.CommandText = @"
        CREATE TABLE IF NOT EXISTS Tareas (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Titulo TEXT NOT NULL,
            Descripcion TEXT,
            Estado TEXT NOT NULL,
            FechaCreacion TEXT NOT NULL
        )";
    command.ExecuteNonQuery();
}

// GET: Obtener todas las tareas
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
});

// POST: Crear una nueva tarea
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
});

app.Run();