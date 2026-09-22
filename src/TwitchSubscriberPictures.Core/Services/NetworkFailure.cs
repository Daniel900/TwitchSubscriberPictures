using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using TwitchLib.Api.Core.Exceptions;

namespace TwitchSubscriberPictures.Core.Services;

/// <summary>
/// Classifies transport-level failures (aborted TLS handshakes, reset sockets,
/// timeouts) so callers can retry them, and renders the full exception chain
/// for the activity log.
/// </summary>
public static class NetworkFailure
{
    public static bool IsTransient(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is TimeoutException or TaskCanceledException)
        {
            return true;
        }

        // Twitch's edge occasionally answers with these before a request succeeds.
        if (exception is BadGatewayException or GatewayTimeoutException or InternalServerErrorException)
        {
            return true;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is IOException
                or SocketException
                or AuthenticationException
                or TimeoutException
                or TaskCanceledException)
            {
                return true;
            }
        }

        return false;
    }

    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var builder = new StringBuilder();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (builder.Length > 0)
            {
                builder.Append(" -> ");
            }

            builder.Append(current.GetType().Name).Append(": ").Append(current.Message);

            if (current is SocketException socketException)
            {
                builder.Append(" (error ").Append(socketException.ErrorCode).Append(')');
            }
        }

        return builder.ToString();
    }
}
