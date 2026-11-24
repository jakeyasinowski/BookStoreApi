using BookStoreApi.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace BookStoreApi.Services;

public class BooksService(IOptions<BookStoreDatabaseSettings> bookStoreDatabaseSettings)
{
    private readonly IMongoCollection<Book> _booksCollection = new MongoClient(bookStoreDatabaseSettings.Value.ConnectionString)
        .GetDatabase(bookStoreDatabaseSettings.Value.DatabaseName)
        .GetCollection<Book>(bookStoreDatabaseSettings.Value.BooksCollectionName);

    public async Task<List<Book>> GetAsync() =>
        await _booksCollection.Find(_ => true).ToListAsync();

    public async Task<Book?> GetAsync(string id) =>
        await _booksCollection.Find(x => x.Id == id).FirstOrDefaultAsync();

    public async Task CreateAsync(Book newBook) =>
        await _booksCollection.InsertOneAsync(newBook);

    public async Task UpdateAsync(string id, Book updatedBook) =>
        await _booksCollection.ReplaceOneAsync(x => x.Id == id, updatedBook);

    public async Task RemoveAsync(string id) =>
        await _booksCollection.DeleteOneAsync(x => x.Id == id);

    public async Task InsertManyAsync(List<Book> books)
    {
        await _booksCollection.InsertManyAsync(books);
    }

    public async Task<List<Book>> SearchAsync(
        string? category,
        decimal? minPrice,
        decimal? maxPrice)
    {
        var filter = Builders<Book>.Filter.Empty;

        if (!string.IsNullOrWhiteSpace(category))
        {
            filter &= Builders<Book>.Filter.Eq(
                b => b.Category,
                category);
        }

        if (minPrice.HasValue)
        {
            filter &= Builders<Book>.Filter.Gte(
                b => b.Price,
                minPrice.Value);
        }

        if (maxPrice.HasValue)
        {
            filter &= Builders<Book>.Filter.Lte(
                b => b.Price,
                maxPrice.Value);
        }

        return await _booksCollection
            .Find(filter)
            .ToListAsync();
    }

    public async Task<List<TopBook>> GetTopAsync(int count)
    {
        var sort = Builders<Book>.Sort.Descending(b => b.Price);
        var projection = Builders<Book>.Projection
            .Include("Name")
            .Include("Price");

        return await _booksCollection
            .Find(_ => true)
            .Sort(sort)
            .Limit(count)
            .Project<TopBook>(projection)
            .ToListAsync();
    }

    public async Task<bool> UpdatePriceAsync(string id, decimal price)
    {
        var filter = Builders<Book>.Filter.Eq(b => b.Id, id);
        var update = Builders<Book>.Update.Set(b => b.Price, price);
        var result = await _booksCollection.UpdateOneAsync(filter, update);

        return result.MatchedCount == 1;
    }
}