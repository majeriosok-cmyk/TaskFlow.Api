using TaskFlow.Api.Models;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.UseHttpsRedirection();

// Ruta de prueba
app.MapGet("/prueba", () => "¡La API funciona!");

// Lista de tareas (en memoria)
var tareas = new List<Tarea>();

// GET: Obtener todas las tareas
app.MapGet("/api/tareas", () => tareas);

// POST: Crear una nueva tarea
app.MapPost("/api/tareas", (Tarea nuevaTarea) =>
{
    if (string.IsNullOrWhiteSpace(nuevaTarea.Titulo))
        return Results.BadRequest("El título es obligatorio");

    nuevaTarea.Id = tareas.Count + 1;
    nuevaTarea.FechaCreacion = DateTime.Now;
    tareas.Add(nuevaTarea);

    return Results.Created($"/api/tareas/{nuevaTarea.Id}", nuevaTarea);
});

app.Run();
