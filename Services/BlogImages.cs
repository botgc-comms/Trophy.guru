using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Trophy.Catalogue.Services;

public sealed class BlogImages(HttpClient client)
{
    public const int MaxBytes = 12 * 1024 * 1024;

    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
                throw new HttpRequestException("Image host must resolve exclusively to public addresses.");
            // Connect to the validated IP, preventing DNS rebinding between validation and download.
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(addresses[0], context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch { socket.Dispose(); throw; }
        }
    };

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return b[0] is not (0 or 10 or 127) && b[0] < 224 &&
                !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] >= 16 && b[1] <= 31) &&
                !(b[0] == 192 && (b[1] == 168 || b[1] == 0 || b[1] == 2)) &&
                !(b[0] == 100 && b[1] >= 64 && b[1] <= 127) &&
                !(b[0] == 198 && b[1] is 18 or 19 or 51) && !(b[0] == 203 && b[1] == 0 && b[2] == 113);
        // Only global unicast; exclude documentation and transition ranges.
        return b.Length == 16 && (b[0] & 0xe0) == 0x20 &&
            !(b[0] == 0x20 && b[1] == 0x02) &&
            !(b[0] == 0x20 && b[1] == 0x01 && (b[2] < 2 || (b[2] == 0x0d && b[3] == 0xb8)));
    }

    public async Task<string?> DownloadAsync(string? url, string directory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var uri = new Uri(url, UriKind.Absolute);
        for (var redirects = 0; redirects <= 3; redirects++)
        {
            if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
                (IPAddress.TryParse(uri.Host, out var ip) && !IsPublicAddress(ip)))
                throw new HttpRequestException("Images require a public HTTPS URL on port 443.");
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                uri = new Uri(uri, response.Headers.Location ?? throw new HttpRequestException("Missing image redirect location."));
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxBytes) throw new HttpRequestException("Image is too large.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            byte[] bytes;
            try { bytes = await ReadBoundedAsync(stream, MaxBytes, timeout.Token); }
            catch (InvalidDataException exception) { throw new HttpRequestException("Image exceeds the download size limit.", exception); }
            var extension = ImageExtension(bytes);
            var expectedType = extension switch { ".png" => "image/png", ".jpg" => "image/jpeg", ".webp" => "image/webp", _ => "image/gif" };
            if (response.Content.Headers.ContentType?.MediaType != expectedType)
                throw new HttpRequestException("Image content type does not match its file signature.");
            var name = Convert.ToHexStringLower(SHA256.HashData(bytes)) + extension;
            var path = Path.Combine(directory, name);
            if (!File.Exists(path))
            {
                var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    await File.WriteAllBytesAsync(temporary, bytes, timeout.Token);
                    File.Move(temporary, path, overwrite: false);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            return "/blog/images/" + name;
        }
        throw new HttpRequestException("Too many image redirects.");
    }

    public static string ImageExtension(byte[] data)
    {
        if (data.Length >= 8 && data.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})) return ".png";
        if (data.Length >= 3 && data[0] == 255 && data[1] == 216 && data[2] == 255) return ".jpg";
        if (data.Length >= 12 && data.AsSpan(0,4).SequenceEqual("RIFF"u8) && data.AsSpan(8,4).SequenceEqual("WEBP"u8)) return ".webp";
        if (data.Length >= 6 && (data.AsSpan(0,6).SequenceEqual("GIF89a"u8) || data.AsSpan(0,6).SequenceEqual("GIF87a"u8))) return ".gif";
        throw new HttpRequestException("Unsupported image format; use PNG, JPEG, WebP or GIF.");
    }

    public static async Task<byte[]> ReadBoundedAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("Payload exceeds the size limit.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        return output.ToArray();
    }
}

