using Bogus;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Dapper;
using System.Data;
using System;

var builder = WebApplication.CreateBuilder(args);





// Apply the CORS middleware
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(
        builder => builder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// Configure PostgreSQL connection
string connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ??
                          $"Host=localhost;Database=users;Username={Environment.GetEnvironmentVariable("DB_USER")};Password={Environment.GetEnvironmentVariable("DB_PASSWORD")}";

builder.Services.AddSingleton<IDbConnection>(sp => new NpgsqlConnection(connectionString));


var app = builder.Build();

// Enable CORS
app.UseCors();



// Middleware to parse JSON bodies (Handled automatically in ASP.NET Core)

// Simple route to get a message
app.MapGet("/", () =>
{
    Console.WriteLine("Request received at /");
    return Results.Ok(new { message = "hello" });
});

// Route to get a single user by ID
app.MapGet("/user/{id}", async (IDbConnection db, int id) =>
{
    var user = await db.QueryFirstOrDefaultAsync("SELECT * FROM users WHERE id = @Id", new { Id = id });
    return user != null ? Results.Ok(user) : Results.NotFound("User not found");
});

// Route to get all users
app.MapGet("/users", async (IDbConnection db) =>
{
    var users = await db.QueryAsync("SELECT * FROM users");
    return Results.Ok(users);
});


// Route to generate multiple users
app.MapPost("/generate-users/{count:int}", async (IDbConnection db, int count) =>
{
    
    var userFaker = new Faker<User>()
        .RuleFor(u => u.Name, f => f.Name.FullName())
        .RuleFor(u => u.Email, f => f.Internet.Email())
        .RuleFor(u => u.Age, f => f.Random.Int(18, 99))
        .RuleFor(u => u.CreatedAt, f => DateTime.UtcNow);

    var users = new List<User>();

    for (int i = 0; i < count; i++)
    {
        users.Add(userFaker.Generate());
    }
    
    // Open the database connection
    if (db.State == ConnectionState.Closed)
    {
        db.Open();
    }

    using var transaction = db.BeginTransaction();

    try
    {
        foreach (var user in users)
        {
            await db.ExecuteAsync(
                "INSERT INTO users (name, email, age, created_at) VALUES (@Name, @Email, @Age, @CreatedAt)",
                new { user.Name, user.Email, user.Age, user.CreatedAt }, transaction: transaction);
        }

        transaction.Commit();
        return Results.Created($"/users", new { message = $"{count} users created successfully" });

    }
    catch (Exception ex)
    {
        transaction.Rollback();
        Console.Error.WriteLine(ex);
        return Results.StatusCode(500);
    }
});




// Start the server
app.Run();

// User class definition
public class User
{
    public string Name { get; set; }
    public string Email { get; set; }
    public int Age { get; set; }
    public DateTime CreatedAt { get; set; }
}