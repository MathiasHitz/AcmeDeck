using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace AcmeDeck.Services;

/// <summary>
/// Connects to the local HTTPS binding with the host name as SNI and reports
/// which certificate IIS actually serves. That is the end-to-end check: the
/// renewal history can say "success" while the binding still has the old one.
/// </summary>
public static class TlsProbe
{
    public sealed record ProbeResult(string Host, int Port, CertificateDetails? Certificate, string? Error);

    public static async Task<ProbeResult> ProbeAsync(string ipAddress, int port, string sniHost, TimeSpan timeout)
    {
        var connectTo = ipAddress is "*" or "" ? "127.0.0.1" : ipAddress.Trim('[', ']');
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            using var client = new TcpClient();
            await client.ConnectAsync(connectTo, port, cts.Token);
            await using var ssl = new SslStream(client.GetStream(), false);
            var options = new SslClientAuthenticationOptions
            {
                TargetHost = sniHost,
                // We only want to see the certificate, trust is judged separately
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            };
            await ssl.AuthenticateAsClientAsync(options, cts.Token);
            if (ssl.RemoteCertificate == null)
            {
                return new ProbeResult(sniHost, port, null, "No certificate presented");
            }
            using var cert = new X509Certificate2(ssl.RemoteCertificate);
            return new ProbeResult(sniHost, port, CertificateDetails.From(cert, null), null);
        }
        catch (OperationCanceledException)
        {
            return new ProbeResult(sniHost, port, null, "Timeout");
        }
        catch (Exception ex)
        {
            return new ProbeResult(sniHost, port, null, ex.InnerException?.Message ?? ex.Message);
        }
    }
}
