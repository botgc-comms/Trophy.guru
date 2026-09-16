using System.Text.Json;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

// Version-specific editorial corrections to existing public blog posts. Customer
// stores are never opened. A later CMS revision wins; original public JSON is backed up.
public static class BlogEditorialRevisions
{
    public sealed record Revision(string Slug, string Title, string Description, string Html, DateTimeOffset SupersedesUpdatedAt, DateTimeOffset? RevisedAt = null);
    public static readonly DateTimeOffset PublishedAt = DateTimeOffset.Parse("2026-09-14T08:20:00Z");

    public static IReadOnlyList<Revision> Load()
    {
        var assembly = typeof(BlogEditorialRevisions).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.Contains(".Content.BlogRevisions.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Select(n => {
                using var stream = assembly.GetManifestResourceStream(n)!;
                return JsonSerializer.Deserialize<Revision>(stream, BlogStore.Json)!;
            }).ToArray();
    }

    public static int Apply(BlogStore store)
    {
        var changed = 0;
        foreach (var revision in Load())
        {
            var post = store.Find(revision.Slug);
            if (post is null || post.Article.UpdatedAt != revision.SupersedesUpdatedAt) continue;
            var backupDirectory = Path.Combine(Path.GetDirectoryName(store.ImageDirectory)!, "editorial-backups");
            Directory.CreateDirectory(backupDirectory);
            var backup = Path.Combine(backupDirectory, $"{post.Article.Id}-{post.Article.UpdatedAt.UtcTicks}.json");
            // Never overwrite a previous backup or an unrelated/newer CMS revision.
            if (!File.Exists(backup)) File.WriteAllText(backup, JsonSerializer.Serialize(post, BlogStore.Json));
            var article = post.Article with {
                Title = revision.Title, MetaDescription = revision.Description, ContentHtml = revision.Html,
                ContentMarkdown = null, UpdatedAt = revision.RevisedAt ?? PublishedAt, FaqSchema = [], Keywords = [], MetaKeywords = null,
                HeroImageUrl = null, HeroImageAlt = null, InfographicImageUrl = null
            };
            store.Upsert(new BlogPost(article, revision.Html, null, null));
            changed++;
        }
        return changed;
    }
}
