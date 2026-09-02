using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Trophy.Catalogue.Domain;

namespace Trophy.Catalogue.Services;

public sealed class OpenAiEngravingReader(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<OpenAiEngravingReader> logger)
{
    private readonly string apiKey = configuration["OPENAI_API_KEY"] ?? string.Empty;
    private readonly string model = configuration["OPENAI_MODEL"] ?? "gpt-5.6-terra";
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public bool IsAvailable => !string.IsNullOrWhiteSpace(apiKey);
    public string Model => model;

    public async Task<AiExtraction> ReadAsync(
        TrophyRecord trophy,
        IReadOnlyList<(EvidenceImage Evidence, string Path)> evidenceFiles,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            throw new OpenAiUnavailableException("The AI reader is not configured yet. The image has been saved and can be analysed after OPENAI_API_KEY is added.");
        if (evidenceFiles.Count == 0)
            throw new InvalidOperationException("At least one evidence image is required.");

        var currentWinners = trophy.Winners
            .OrderBy(winner => winner.Year)
            .Select(winner => new { winner.Year, winner.Name, winner.ReviewState, winner.Source, winner.Notes });
        var prompt = $$"""
            Build the chronological winners list for the golf trophy "{{trophy.Name}}" (catalogue {{trophy.Id}}) from every attached image.

            The images may overlap, repeat the same engraved bands, contain glare or reflections, or include high-contrast paper rubbings. Compare all images with one another. Treat repeated views as corroboration, not separate winners. Read only names and years that are genuinely visible. A team or pair engraved for one year belongs in one winner string, joined exactly as the engraving indicates. Never invent a missing name, initial, surname, or year.

            Existing working list:
            {{JsonSerializer.Serialize(currentWinners, jsonOptions)}}

            Return one consolidated entry per visible year in ascending order. Include existing entries when the images support them, correct an existing unconfirmed reading when a clearer image supports the correction, and preserve uncertainty in the notes. Confidence is from 0 to 1. Use below 0.75 when any meaningful character or digit is uncertain. Observations should briefly describe unreadable areas, conflicts between views, and likely missing bands; do not put winner entries in observations.
            """;

        var content = new List<object>
        {
            new Dictionary<string, object?> { ["type"] = "input_text", ["text"] = prompt }
        };
        foreach (var (evidence, path) in evidenceFiles)
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            content.Add(new Dictionary<string, object?>
            {
                ["type"] = "input_image",
                ["image_url"] = $"data:{evidence.ContentType};base64,{Convert.ToBase64String(bytes)}",
                ["detail"] = "auto"
            });
        }

        var winnerSchema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new Dictionary<string, object?>
            {
                ["year"] = new Dictionary<string, object?> { ["type"] = "integer", ["minimum"] = 1800, ["maximum"] = 2200 },
                ["winner"] = new Dictionary<string, object?> { ["type"] = "string" },
                ["confidence"] = new Dictionary<string, object?> { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1 },
                ["notes"] = new Dictionary<string, object?> { ["type"] = "string" }
            },
            ["required"] = new[] { "year", "winner", "confidence", "notes" }
        };
        var outputSchema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new Dictionary<string, object?>
            {
                ["entries"] = new Dictionary<string, object?> { ["type"] = "array", ["items"] = winnerSchema },
                ["observations"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object?> { ["type"] = "string" }
                }
            },
            ["required"] = new[] { "entries", "observations" }
        };
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["store"] = false,
            ["reasoning"] = new Dictionary<string, object?> { ["effort"] = "medium" },
            ["input"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["content"] = content
                }
            },
            ["text"] = new Dictionary<string, object?>
            {
                ["format"] = new Dictionary<string, object?>
                {
                    ["type"] = "json_schema",
                    ["name"] = "trophy_engraving_reading",
                    ["strict"] = true,
                    ["schema"] = outputSchema
                }
            },
            ["max_output_tokens"] = 8000
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, jsonOptions), Encoding.UTF8, "application/json");

        var client = httpClientFactory.CreateClient(nameof(OpenAiEngravingReader));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("OpenAI engraving analysis failed with {StatusCode}: {Response}", (int)response.StatusCode, responseBody);
            throw new OpenAiUnavailableException($"The AI reader could not analyse these images ({(int)response.StatusCode}). They are safely stored; try again shortly.");
        }

        var outputText = ExtractOutputText(responseBody);
        try
        {
            return JsonSerializer.Deserialize<AiExtraction>(outputText, jsonOptions) ?? new AiExtraction();
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "OpenAI returned an unreadable engraving analysis payload: {OutputText}", outputText);
            throw new OpenAiUnavailableException("The AI reader returned an unexpected result. The images are safely stored; try the analysis again.");
        }
    }

    private static string ExtractOutputText(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        if (document.RootElement.TryGetProperty("output_text", out var directText) && directText.ValueKind == JsonValueKind.String)
            return directText.GetString() ?? "{}";

        if (!document.RootElement.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            throw new OpenAiUnavailableException("The AI reader returned no text result.");

        var pieces = new List<string>();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var contents) || contents.ValueKind != JsonValueKind.Array) continue;
            foreach (var content in contents.EnumerateArray())
            {
                if (content.TryGetProperty("type", out var type) && type.GetString() == "output_text" &&
                    content.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    pieces.Add(text.GetString() ?? string.Empty);
            }
        }
        if (pieces.Count == 0) throw new OpenAiUnavailableException("The AI reader returned no text result.");
        return string.Concat(pieces);
    }
}

public sealed class OpenAiUnavailableException(string message) : Exception(message);
