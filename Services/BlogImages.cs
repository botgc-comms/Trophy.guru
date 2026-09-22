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

    // Read intrinsic dimensions without decoding or executing the downloaded media.
    public static (int Width, int Height)? Dimensions(byte[] bytes)
    {
        var data = bytes.AsSpan();
        int width = 0, height = 0;
        if (data.Length >= 24 && data[..8].SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}))
        {
            width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data.Slice(16,4));
            height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data.Slice(20,4));
        }
        else if (data.Length >= 10 && (data[..6].SequenceEqual("GIF89a"u8) || data[..6].SequenceEqual("GIF87a"u8)))
        {
            width = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6,2));
            height = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8,2));
        }
        else if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8,4).SequenceEqual("WEBP"u8))
        {
            if (data.Length >= 30 && data.Slice(12,4).SequenceEqual("VP8X"u8))
            { width = 1 + data[24] + (data[25] << 8) + (data[26] << 16); height = 1 + data[27] + (data[28] << 8) + (data[29] << 16); }
            else if (data.Length >= 25 && data.Slice(12,4).SequenceEqual("VP8L"u8) && data[20] == 0x2f)
            { width = 1 + data[21] + ((data[22] & 0x3f) << 8); height = 1 + (data[22] >> 6) + (data[23] << 2) + ((data[24] & 0x0f) << 10); }
            else if (data.Length >= 30 && data.Slice(12,4).SequenceEqual("VP8 "u8) && data.Slice(23,3).SequenceEqual(new byte[] {0x9d,0x01,0x2a}))
            { width = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(26,2)) & 0x3fff; height = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(28,2)) & 0x3fff; }
        }
        else if (data.Length >= 4 && data[0] == 0xff && data[1] == 0xd8)
        {
            var i = 2;
            while (i < data.Length)
            {
                if (data[i++] != 0xff) continue;
                while (i < data.Length && data[i] == 0xff) i++;
                if (i >= data.Length) break;
                var marker = data[i++];
                if (marker is 0xda or 0xd9) break;
                if (marker is 0x01 or >= 0xd0 and <= 0xd8) continue;
                if (i + 2 > data.Length) break;
                var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i,2));
                if (length < 2 || i + length > data.Length) break;
                if (marker is >= 0xc0 and <= 0xc3 or >= 0xc5 and <= 0xc7 or >= 0xc9 and <= 0xcb or >= 0xcd and <= 0xcf)
                {
                    if (length < 8) break;
                    height = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i+3,2));
                    width = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i+5,2));
                    break;
                }
                i += length;
            }
        }
        return width > 0 && height > 0 ? (width,height) : null;
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

