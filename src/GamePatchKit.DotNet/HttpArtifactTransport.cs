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
                    isNotFound: await IsConfirmedAbsentAsync(response, cancellationToken).ConfigureAwait(false));
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

    // A 404 is absence by definition. A 400 is not - it also covers genuinely malformed requests - but some
    // object stores answer a missing object with 400 and put the real status in the body (Supabase Storage:
    // {"statusCode":"404",...}), so that one status is worth reading before writing the failure off as
    // unconfirmed. Every other status stays unconfirmed, and so does a 400 whose body says anything else.
    private static async Task<bool> IsConfirmedAbsentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return true;
        }

        if (response.StatusCode != HttpStatusCode.BadRequest)
        {
            return false;
        }

        try
        {
            await using Stream body = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            // One byte past the cap so a body that overruns it is recognized as too large rather than
            // silently truncated into something that might parse.
            var buffer = new byte[ArtifactTransportResponseBody.MaximumInspectedBytes + 1];
            int total = 0;
            int read;

            while (total < buffer.Length
                && (read = await body.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
            }

            return total <= ArtifactTransportResponseBody.MaximumInspectedBytes
                && ArtifactTransportResponseBody.ConfirmsNotFound(buffer[..total]);
        }
        catch (Exception exception) when (
            exception is HttpRequestException
            || exception is IOException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // The request has already failed; not being able to read its body only means the failure stays
            // unconfirmed. Classification must never replace the transport exception with a different one.
            return false;
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
