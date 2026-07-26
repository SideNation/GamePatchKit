using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GamePatchKit.Runtime;

namespace GamePatchKit.DotNet;

// HttpClient-backed IArtifactTransport. HttpClient is injected so a host controls connection reuse, base
// address, default headers and TLS/proxy configuration; this class only turns a target/relativePath into a
// request and turns the response into a stream or an ArtifactTransportException. It never retries - that is
// Runtime's job once IsTransient tells it whether retrying could help.
//
// URLs mirror the packager's immutable publish tree, resolved against HttpClient.BaseAddress (which must end
// with '/' for relative combination to keep the base path):
//   "<packageId>/manifests/<manifestHash>/manifest.json" and "...manifest.sig"
// Artifact relativePath is used as-is: it already carries its own "<packageId>/artifacts/..." prefix from
// Core's content-addressed path scheme, so packageId is not prepended a second time for that one.
public sealed class HttpArtifactTransport : IArtifactTransport
{
    private const string ManifestFileName = "manifest.json";
    private const string SignatureFileName = "manifest.sig";

    private readonly HttpClient _httpClient;

    public HttpArtifactTransport(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public Task<Stream> OpenManifestAsync(TargetManifestReference target, CancellationToken cancellationToken)
    {
        return GetAsync($"{target.PackageId}/manifests/{target.ManifestHash}/{ManifestFileName}", cancellationToken);
    }

    public Task<Stream> OpenManifestSignatureAsync(TargetManifestReference target, CancellationToken cancellationToken)
    {
        return GetAsync($"{target.PackageId}/manifests/{target.ManifestHash}/{SignatureFileName}", cancellationToken);
    }

    public Task<Stream> OpenArtifactAsync(string packageId, string relativePath, CancellationToken cancellationToken)
    {
        return GetAsync(relativePath, cancellationToken);
    }

    private async Task<Stream> GetAsync(string relativePath, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
            response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new ArtifactTransportException("The artifact request failed.", isTransient: true, exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout surfaces as an OperationCanceledException even though the caller's token was
            // never cancelled - that is a transport failure, not a cancellation, and Runtime only skips retry
            // for the latter.
            throw new ArtifactTransportException("The artifact request timed out.", isTransient: true, exception);
        }

        try
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new ArtifactTransportException(
                    $"The artifact request returned HTTP {(int)response.StatusCode}.",
                    IsTransientStatusCode(response.StatusCode),
                    isNotFound: response.StatusCode == HttpStatusCode.NotFound);
            }

            Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseBodyStream(response, body);
        }
        catch (ArtifactTransportException)
        {
            response.Dispose();
            throw;
        }
        catch (HttpRequestException exception)
        {
            // Reaching this point means headers already arrived successfully - a failure opening the body
            // stream itself is just as retryable as one that happens before headers do.
            response.Dispose();
            throw new ArtifactTransportException("The artifact response body could not be opened.", isTransient: true, exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            response.Dispose();
            throw new ArtifactTransportException("The artifact response body timed out while opening.", isTransient: true, exception);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
    {
        int code = (int)statusCode;
        return code == (int)HttpStatusCode.RequestTimeout
            || code == (int)HttpStatusCode.TooManyRequests
            || code >= 500;
    }
}
