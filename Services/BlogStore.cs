using System.Text.Json;
using Microsoft.Data.Sqlite;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

// A separate database: this service never opens the account, archive or billing stores.
public sealed class BlogStore
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string connectionString;
    public string ImageDirectory { get; }
    public SemaphoreSlim DeliveryLock { get; } = new(1, 1);

    public BlogStore(IWebHostEnvironment environment, IConfiguration configuration)
        : this(Path.Combine(AppDataPath.Resolve(environment, configuration), "blog")) { }

    public BlogStore(string directory)
    {
        Directory.CreateDirectory(directory);
        ImageDirectory = Path.Combine(directory, "images");
        Directory.CreateDirectory(ImageDirectory);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "autoseo.sqlite"),
            Mode = SqliteOpenMode.ReadWriteCreate, DefaultTimeout = 15, Pooling = false
        }.ToString();
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS autoseo_posts (
                article_id INTEGER PRIMARY KEY,
                slug TEXT NOT NULL UNIQUE,
                updated_ticks INTEGER NOT NULL,
                published_ticks INTEGER NOT NULL,
                post_json TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(connectionString);
        db.Open();
        return db;
    }

    public BlogPost? Find(long id) => FindBy("article_id", id);
    public BlogPost? Find(string slug) => FindBy("slug", slug);
    private BlogPost? FindBy(string column, object value)
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = $"SELECT post_json FROM autoseo_posts WHERE {column}=$value";
        command.Parameters.AddWithValue("$value", value);
        return command.ExecuteScalar() is string json ? JsonSerializer.Deserialize<BlogPost>(json, Json) : null;
    }

    public IReadOnlyList<BlogPost> List(int limit = 50, int offset = 0)
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT post_json FROM autoseo_posts ORDER BY published_ticks DESC, article_id DESC LIMIT $limit OFFSET $offset";
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        using var reader = command.ExecuteReader();
        var posts = new List<BlogPost>();
        while (reader.Read()) posts.Add(JsonSerializer.Deserialize<BlogPost>(reader.GetString(0), Json)!);
        return posts;
    }

    public void Upsert(BlogPost post)
    {
        using var db = Open();
        using var command = db.CreateCommand();
        // Preserve the first public slug; renamed titles/slugs keep existing inbound links valid.
        // Delayed deliveries cannot replace a newer version. Only this new table is updated.
        command.CommandText = """
            INSERT INTO autoseo_posts(article_id,slug,updated_ticks,published_ticks,post_json)
            VALUES($id,$slug,$updated,$published,$json)
            ON CONFLICT(article_id) DO UPDATE SET
                updated_ticks=excluded.updated_ticks, published_ticks=excluded.published_ticks,
                post_json=excluded.post_json
            WHERE excluded.updated_ticks > autoseo_posts.updated_ticks;
            """;
        command.Parameters.AddWithValue("$id", post.Article.Id);
        command.Parameters.AddWithValue("$slug", post.Article.Slug);
        command.Parameters.AddWithValue("$updated", post.Article.UpdatedAt.UtcTicks);
        command.Parameters.AddWithValue("$published", post.Article.PublishedAt.UtcTicks);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(post, Json));
        command.ExecuteNonQuery();
    }
}
