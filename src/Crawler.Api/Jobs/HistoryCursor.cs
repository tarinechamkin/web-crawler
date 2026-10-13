using System.Text;

namespace Crawler.Api.Jobs;

/// <summary>
/// Keyset-pagination cursor: "give me jobs older than (CreatedAt, Id)". Unlike OFFSET it costs the
/// same on page 1 and page 1000 (an index seek on (created_at DESC, id DESC)) and does not skip or
/// repeat rows when new jobs arrive between requests. Opaque to clients (base64url).
/// </summary>
public readonly record struct HistoryCursor(DateTimeOffset CreatedAt, Guid Id)
{
    public string Encode()
    {
        var raw = $"{CreatedAt.UtcTicks}:{Id:N}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string? value, out HistoryCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !Guid.TryParseExact(parts[1], "N", out var id))
                return false;
            cursor = new HistoryCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
