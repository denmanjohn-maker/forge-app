using System.Net;

namespace MtgForge.Api.Observability;

/// <summary>
/// Middleware that restricts specific paths (/metrics, /logging) to requests
/// originating from private/Docker network IPs only.
/// External requests receive 403 Forbidden.
/// </summary>
public sealed class InternalOnlyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HashSet<string> _restrictedPaths;

    public InternalOnlyMiddleware(RequestDelegate next, IEnumerable<string> restrictedPaths)
    {
        _next = next;
        _restrictedPaths = new HashSet<string>(restrictedPaths, StringComparer.OrdinalIgnoreCase);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_restrictedPaths.Contains(context.Request.Path.Value ?? ""))
        {
            var remoteIp = context.Connection.RemoteIpAddress;
            if (remoteIp is not null && !IsPrivateOrLoopback(remoteIp))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Forbidden: internal access only");
                return;
            }
        }

        await _next(context);
    }

    private static bool IsPrivateOrLoopback(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;

        // Map IPv6-mapped IPv4 back to IPv4
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        var bytes = ip.GetAddressBytes();
        if (bytes.Length == 4)
        {
            // 10.0.0.0/8
            if (bytes[0] == 10) return true;
            // 172.16.0.0/12 (Docker default bridge network)
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            // 127.0.0.0/8
            if (bytes[0] == 127) return true;
        }

        // IPv6 link-local (fe80::/10)
        if (bytes.Length == 16 && bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80) return true;

        return false;
    }
}

public static class InternalOnlyMiddlewareExtensions
{
    public static IApplicationBuilder UseInternalOnly(this IApplicationBuilder app, params string[] paths)
    {
        return app.UseMiddleware<InternalOnlyMiddleware>(paths.AsEnumerable());
    }
}
