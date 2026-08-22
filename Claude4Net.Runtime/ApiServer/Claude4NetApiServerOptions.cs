using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Claude4Net.Runtime.ApiServer;

public sealed class Claude4NetApiServerOptions
{
    private string _bindAddress = IPAddress.Loopback.ToString();

    public int Port { get; set; } = Claude4NetApiServer.DefaultPort;
    public string? ApiKey { get; set; }

    public string BindAddress
    {
        get => _bindAddress;
        set => _bindAddress = ParseBindAddress(value).ToString();
    }

    public bool AllowRemote { get; set; }
    public string? CertificatePath { get; set; }
    public string? CertificatePasswordEnvironmentVariable { get; set; }
    public long MaxRequestBodyBytes { get; set; } = 1_048_576;
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public int MaxConcurrentRequests { get; set; } = 16;
    public int MaxQueuedRequests { get; set; }

    internal ValidatedClaude4NetApiServerOptions Validate()
    {
        if (Port is < 1 or > 65_535)
            throw new ArgumentOutOfRangeException(nameof(Port), "Port must be between 1 and 65535.");
        if (MaxRequestBodyBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxRequestBodyBytes), "Request body limit must be positive.");
        if (RequestTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RequestTimeout), "Request timeout must be positive.");
        if (MaxConcurrentRequests <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxConcurrentRequests), "Concurrent request limit must be positive.");
        if (MaxQueuedRequests < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxQueuedRequests), "Queued request limit cannot be negative.");
        IPAddress address = ParseBindAddress(_bindAddress);
        if (!IPAddress.IsLoopback(address) && !AllowRemote)
            throw new InvalidOperationException("Binding to a non-loopback address requires AllowRemote.");
        string? apiKey = ApiKey?.Trim();

        string? certificatePath = string.IsNullOrWhiteSpace(CertificatePath)
            ? null
            : Path.GetFullPath(CertificatePath.Trim());
        if (!string.IsNullOrWhiteSpace(CertificatePasswordEnvironmentVariable) && certificatePath == null)
            throw new InvalidOperationException("A certificate password environment variable requires CertificatePath.");
        if (!IPAddress.IsLoopback(address) && certificatePath == null)
            throw new InvalidOperationException("Binding to a non-loopback address requires a certificate with a private key.");
        if (!IPAddress.IsLoopback(address) && !string.IsNullOrWhiteSpace(apiKey) && apiKey.Length < 32)
            throw new ArgumentException("An explicit API key for a non-loopback bind must be at least 32 characters.", nameof(ApiKey));

        X509Certificate2? certificate = null;
        if (certificatePath != null)
        {
            if (!File.Exists(certificatePath))
                throw new FileNotFoundException("The API server certificate file was not found.", certificatePath);

            string? password = null;
            string? passwordEnvironmentVariable = string.IsNullOrWhiteSpace(CertificatePasswordEnvironmentVariable)
                ? null
                : CertificatePasswordEnvironmentVariable.Trim();
            if (passwordEnvironmentVariable != null)
            {
                password = Environment.GetEnvironmentVariable(passwordEnvironmentVariable);
                if (password == null)
                    throw new InvalidOperationException("The certificate password environment variable is not set.");
            }

            certificate = X509CertificateLoader.LoadPkcs12FromFile(
                certificatePath,
                password,
                X509KeyStorageFlags.DefaultKeySet);
            if (!certificate.HasPrivateKey)
            {
                certificate.Dispose();
                throw new InvalidOperationException("The API server certificate must contain a private key.");
            }
        }

        return new ValidatedClaude4NetApiServerOptions(
            Port,
            apiKey,
            address,
            certificate,
            MaxRequestBodyBytes,
            RequestTimeout,
            MaxConcurrentRequests,
            MaxQueuedRequests);
    }

    private static IPAddress ParseBindAddress(string? value)
    {
        string candidate = value?.Trim() ?? string.Empty;
        if (candidate is "*" or "+" or "0.0.0.0" or "::" or "[::]")
            throw new ArgumentException("Wildcard bind addresses are not allowed.", nameof(value));
        if (!IPAddress.TryParse(candidate, out IPAddress? address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any))
        {
            throw new ArgumentException("BindAddress must be a concrete IP address.", nameof(value));
        }

        return address;
    }
}

internal sealed record ValidatedClaude4NetApiServerOptions(
    int Port,
    string? ApiKey,
    IPAddress BindAddress,
    X509Certificate2? Certificate,
    long MaxRequestBodyBytes,
    TimeSpan RequestTimeout,
    int MaxConcurrentRequests,
    int MaxQueuedRequests)
{
    public string Scheme => Certificate == null ? Uri.UriSchemeHttp : Uri.UriSchemeHttps;
}
