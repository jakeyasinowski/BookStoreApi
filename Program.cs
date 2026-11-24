using BookStoreApi.Models;
using BookStoreApi.Services;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// 1. Read the BookStoreDatabase settings and register the service (one MongoClient for the app)
builder.Services.Configure<BookStoreDatabaseSettings>(
    builder.Configuration.GetSection("BookStoreDatabase"));
builder.Services.AddSingleton<BooksService>();

// 2. Keep JSON property names as written in C# (Name, Price), not camelCase
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

// 3. OpenAPI document at /openapi/v1.json.
//    Version 3.0 so Swagger UI can read it (.NET 11 defaults to 3.2, which Swagger UI rejects).
builder.Services.AddOpenApi(options =>
{
    options.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0;
});

var app = builder.Build();

// 4. Serve the OpenAPI document and Swagger UI at /swagger (Development only).
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUi(options =>
    {
        options.Path = "/swagger";
        options.DocumentPath = "/openapi/v1.json";
    });
}

// 5. Endpoints, all under /books, shown under "Books" in Swagger
var books = app.MapGroup("/books").WithTags("Books");

// GET /books
books.MapGet("/", async (BooksService service) =>
    await service.GetAsync());

// GET /books/top?count=3
books.MapGet("/top", async (BooksService service, int count = 3) =>
{
    if (count < 1)
    {
        return Results.BadRequest("Count must be greater than zero.");
    }

    var topBooks = await service.GetTopAsync(count);
    return Results.Ok(topBooks);
})
.WithName("GetTopBooks")
.Produces<List<TopBook>>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest);

// GET /books/{id}   (an ObjectId is 24 characters)
books.MapGet("/{id:length(24)}", async (string id, BooksService service) =>
    await service.GetAsync(id) is Book book
        ? Results.Ok(book)
        : Results.NotFound());

// POST /books
books.MapPost("/", async (Book newBook, BooksService service) =>
{
    await service.CreateAsync(newBook);
    return Results.Created($"/books/{newBook.Id}", newBook);
});

// PUT /books/{id}
books.MapPut("/{id:length(24)}", async (string id, Book updatedBook, BooksService service) =>
{
    var book = await service.GetAsync(id);
    if (book is null)
    {
        return Results.NotFound();
    }

    updatedBook.Id = book.Id;
    await service.UpdateAsync(id, updatedBook);
    return Results.NoContent();
});

// PATCH /books/{id}/price
books.MapPatch("/{id:length(24)}/price", async (
    string id,
    PriceUpdate priceUpdate,
    BooksService service) =>
{
    var updated = await service.UpdatePriceAsync(id, priceUpdate.Price);
    return updated
        ? Results.NoContent()
        : Results.NotFound();
})
.WithName("UpdateBookPrice")
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status404NotFound);

// DELETE /books/{id}
books.MapDelete("/{id:length(24)}", async (string id, BooksService service) =>
{
    var book = await service.GetAsync(id);
    if (book is null)
    {
        return Results.NotFound();
    }

    await service.RemoveAsync(id);
    return Results.NoContent();
});

// POST /books/upload
books.MapPost("/upload", async (
    [FromForm] IFormFile file,
    BooksService booksService) =>
{
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest("No file uploaded.");
    }

    if (!Path.GetExtension(file.FileName)
        .Equals(".json", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest("Only JSON files are allowed.");
    }

    string json;

    using (var reader = new StreamReader(file.OpenReadStream()))
    {
        json = await reader.ReadToEndAsync();
    }

    List<Book>? booksFromFile;

    try
    {
        booksFromFile =
            JsonSerializer.Deserialize<List<Book>>(json);
    }
    catch
    {
        return Results.BadRequest("Invalid JSON.");
    }

    if (booksFromFile == null)
    {
        return Results.BadRequest("No books found.");
    }

    var invalidBooks = booksFromFile
        .Where(b =>
            string.IsNullOrWhiteSpace(b.BookName) ||
            string.IsNullOrWhiteSpace(b.Category) ||
            string.IsNullOrWhiteSpace(b.Author) ||
            b.Price <= 0)
        .ToList();

    if (invalidBooks.Any())
    {
        return Results.BadRequest(new
        {
            Message = "Validation failed",
            InvalidBooks = invalidBooks
        });
    }

    await booksService.InsertManyAsync(booksFromFile);

    return Results.Ok(new
    {
        InsertedCount = booksFromFile.Count,
        Ids = booksFromFile.Select(b => b.Id)
    });
})
.DisableAntiforgery();

// GET /books/search
books.MapGet("/search", async (
    string? category,
    decimal? minPrice,
    decimal? maxPrice,
    BooksService booksService) =>
{
    var results = await booksService.SearchAsync(
        category,
        minPrice,
        maxPrice);

    return Results.Ok(results);
})
.WithName("SearchBooks")
.Produces<List<Book>>(StatusCodes.Status200OK);

app.Run();
