using System;

namespace Claude4Net.SDK
{
    public static class ProviderEndpointPolicy
    {
        public static Uri ParseAndValidate(string endpoint, string parameterName)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException(
                    $"Endpoint '{endpoint}' is not a valid absolute HTTP/HTTPS URI.",
                    parameterName);
            }

            if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
            {
                throw new ArgumentException(
                    $"Endpoint '{endpoint}' must use HTTPS unless it targets a loopback address.",
                    parameterName);
            }

            return uri;
        }
    }
}
