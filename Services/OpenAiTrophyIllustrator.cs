using System.Net.Http.Headers;
using System.Text.Json;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

public sealed class OpenAiTrophyIllustrator(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<OpenAiTrophyIllustrator> logger)
{
    private readonly string apiKey = configuration["OPENAI_API_KEY"] ?? string.Empty;
    private readonly string model = configuration["OPENAI_IMAGE_MODEL"] ?? "gpt-image-2";
    private readonly string imageSize = configuration["OPENAI_IMAGE_SIZE"] ?? "1024x1024";
    private readonly string imageQuality = configuration["OPENAI_IMAGE_QUALITY"] ?? "high";
    private readonly string promptTemplate = configuration["TROPHY_ILLUSTRATION_PROMPT"] ?? DefaultPrompt;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(apiKey);
    public string Model => model;

    public async Task<byte[]> GenerateAsync(
        string trophyName,
        IReadOnlyList<(EvidenceImage Evidence, string Path)> references,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable) throw new OpenAiUnavailableException("Add OPENAI_API_KEY to enable trophy illustration generation.");
        var photographs = references
            .Where(reference => reference.Evidence.Kind == EvidenceKinds.Photo)
            .Take(4)
            .ToList();
        if (photographs.Count == 0)
            throw new OpenAiUnavailableException("Add at least one trophy reference photograph before generating an illustration. Engraving evidence is stored and processed separately.");

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(model), "model");
        form.Add(new StringContent(promptTemplate.Replace("{{trophy_name}}", trophyName, StringComparison.OrdinalIgnoreCase)), "prompt");
        form.Add(new StringContent(imageQuality), "quality");
        form.Add(new StringContent(imageSize), "size");
        form.Add(new StringContent("transparent"), "background");
        form.Add(new StringContent("png"), "output_format");
        foreach (var (evidence, path) in photographs)
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(evidence.ContentType);
            form.Add(content, "image[]", evidence.OriginalName);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/images/edits");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = form;
        var client = httpClientFactory.CreateClient(nameof(OpenAiTrophyIllustrator));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("OpenAI trophy illustration failed with {StatusCode}: {Response}", (int)response.StatusCode, body);
            throw new OpenAiUnavailableException($"The illustration could not be generated ({(int)response.StatusCode}). Your photographs are safely stored.");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var encoded = document.RootElement.GetProperty("data")[0].GetProperty("b64_json").GetString();
            if (string.IsNullOrWhiteSpace(encoded)) throw new OpenAiUnavailableException("The image service returned no illustration.");
            return Convert.FromBase64String(encoded);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException or IndexOutOfRangeException)
        {
            logger.LogWarning(exception, "OpenAI returned an unreadable illustration response.");
            throw new OpenAiUnavailableException("The image service returned an unreadable illustration. Your photographs are safely stored.");
        }
    }

    private const string DefaultPrompt = """
        Recreate the supplied photographs of “{{trophy_name}}” as one clean, highly accurate vector-style illustration. Treat the supplied trophy photographs collectively as the sole source of truth for the trophy's identity, structure, materials, ornamentation and proportions. Reconcile the different views without combining contradictory details or inventing hidden features.

        Preserve the recognisable shape, proportions, silhouette and structural design of the original trophy. Render the trophy as a strict straight-on frontal elevation at shelf height. Use an orthographic-looking presentation with no downward-looking, overhead or three-quarter camera angle. Correct perspective from the source photographs where necessary so the finished trophy is suitable for placement directly onto a horizontal shelf. The centre line of the trophy must be vertical. Vertical edges must remain upright and parallel. Horizontal edges must remain level and symmetrical.

        Do not show the top face of the lowest base or bottom plinth. Do not render the lowest base as an upward-facing ellipse or disc. The lowest base must terminate in one broad, flat, perfectly horizontal bottom contact edge. The full width of the lowest footprint must sit on the same horizontal baseline. Do not show any underside, feet, raised centre, recessed bottom, cast shadow or visible space beneath the lowest base. The lowest visible opaque pixels of the trophy must belong to the flat bottom contact edge of the base. The trophy must look physically capable of resting directly on a glass shelf without hovering.

        Do not redesign, crop, rotate, stretch, simplify or add or remove structural elements. Keep the complete trophy fully visible. Use smooth metallic shading with controlled soft gradients. Light the trophy with one warm display spotlight positioned directly above its centre. Use consistent top-down illumination across the entire trophy. Keep the lighting subtle and suitable for a premium wooden trophy cabinet. Upper-facing details may be brighter and lower areas may be slightly darker. Use clean generic metallic highlights caused only by the overhead display light.

        Do not reflect rooms, windows, people, furniture, scenery or surrounding objects. Do not add wooden reflections or a reflected image of the cabinet. Do not add photographic texture, noise, grain, scratches or environmental reflections. Preserve fine engraved, embossed, decorative and structural detail visible in the source. Use clean, crisp edges with subtle outline definition. Preserve the original metal colour of the trophy. Retain the original base colour and materials, rendered as a clean illustration. The result must be a newly rendered vector-style illustration rather than a filtered or traced photograph. Apply one consistent illustration treatment across the entire trophy.

        Do not generate readable text, lettering, engraving, dates or names on the trophy or base. Preserve engraved areas only as subtle non-readable marks. Do not add a cast shadow, contact shadow, glow, vignette, floor, table, shelf, wall, scenery, props or surrounding objects.

        Isolate the trophy on a fully transparent canvas. Every background pixel must be transparent. Do not add colour spill, fringe, highlights or a halo around the trophy. Keep the boundary between the trophy and the transparent background crisp and clean. The spaces inside handles, loops, cut-outs and pierced decorative elements must remain open and transparent. Any opening that passes completely through the original trophy must remain empty. Do not fill holes, cut-outs, handle interiors or decorative openings with metal, black material or shading. Preserve all negative space and internal voids exactly as shown in the source photographs.

        Use minimal transparent margin around the complete trophy while keeping every part visible. Leave only a very small, even side and top margin, and no visible gap below the flat bottom contact edge.
        """;
}
