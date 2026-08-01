#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GamePatchKit.Runtime;
using UnityEngine;
using UnityEngine.Networking;

namespace GamePatchKit.Unity
{
    public sealed class UnityWebRequestArtifactTransport : IArtifactTransport
    {
        private const int BAD_REQUEST_STATUS_CODE = 400;
        private const int NOT_FOUND_STATUS_CODE = 404;
        private const string DOWNLOAD_DIRECTORY_NAME = "GamePatchKitDownloads";
        private const string MANIFEST_FILE_NAME = "manifest.json";
        private const string SIGNATURE_FILE_NAME = "manifest.sig";

        private readonly Uri _baseUri;
        private readonly string _downloadRoot;
        private readonly SynchronizationContext _unitySynchronizationContext;

        public UnityWebRequestArtifactTransport(string baseUrl)
            : this(
                baseUrl,
                Path.Combine(Application.temporaryCachePath, DOWNLOAD_DIRECTORY_NAME))
        {
        }

        public UnityWebRequestArtifactTransport(string baseUrl, string downloadRoot)
        {
            if (baseUrl == null
                || !baseUrl.EndsWith("/", StringComparison.Ordinal))
            {
                throw new ArgumentException("baseUrl must end with '/'.", nameof(baseUrl));
            }

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? baseUri))
            {
                throw new ArgumentException("baseUrl must be an absolute URI.", nameof(baseUrl));
            }

            if (string.IsNullOrWhiteSpace(downloadRoot))
            {
                throw new ArgumentException("downloadRoot must not be empty.", nameof(downloadRoot));
            }

            _unitySynchronizationContext = SynchronizationContext.Current
                ?? throw new InvalidOperationException(
                    "UnityWebRequestArtifactTransport must be created on the Unity main thread.");
            _baseUri = baseUri;
            _downloadRoot = Path.GetFullPath(downloadRoot);
        }

        public Task<Stream> OpenManifestAsync(
            TargetManifestReference target,
            CancellationToken cancellationToken)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return GetAsync(
                $"{target.PackageId}/manifests/{target.ManifestHash}/{MANIFEST_FILE_NAME}",
                cancellationToken);
        }

        public Task<Stream> OpenManifestSignatureAsync(
            TargetManifestReference target,
            CancellationToken cancellationToken)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return GetAsync(
                $"{target.PackageId}/manifests/{target.ManifestHash}/{SIGNATURE_FILE_NAME}",
                cancellationToken);
        }

        public Task<Stream> OpenArtifactAsync(
            string packageId,
            string relativePath,
            CancellationToken cancellationToken)
        {
            return GetAsync(relativePath, cancellationToken);
        }

        private Task<Stream> GetAsync(
            string relativePath,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new ArgumentException("relativePath must not be empty.", nameof(relativePath));
            }

            if (relativePath[0] == '/' || relativePath.IndexOf('\\') >= 0)
            {
                throw new ArgumentException(
                    "relativePath must be a forward-slash relative path.",
                    nameof(relativePath));
            }

            var completion = new TaskCompletionSource<Stream>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var requestUri = new Uri(_baseUri, relativePath);
            _unitySynchronizationContext.Post(
                _ => StartRequest(requestUri, cancellationToken, completion),
                state: null);
            return completion.Task;
        }

        private void StartRequest(
            Uri requestUri,
            CancellationToken cancellationToken,
            TaskCompletionSource<Stream> completion)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled();
                return;
            }

            Directory.CreateDirectory(_downloadRoot);
            string temporaryPath = Path.Combine(
                _downloadRoot,
                Guid.NewGuid().ToString("N") + ".download");
            UnityWebRequest? request = null;
            CancellationTokenRegistration cancellationRegistration = default;

            try
            {
                request = UnityWebRequest.Get(requestUri);
                request.disposeDownloadHandlerOnDispose = true;
                request.downloadHandler = new DownloadHandlerFile(temporaryPath);
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                UnityWebRequest activeRequest = request;
                cancellationRegistration = cancellationToken.Register(
                    () => _unitySynchronizationContext.Post(
                        _ => activeRequest.Abort(),
                        state: null));
                operation.completed += _ => CompleteRequest(
                    activeRequest,
                    temporaryPath,
                    cancellationRegistration,
                    cancellationToken,
                    completion);
            }
            catch (Exception exception)
            {
                cancellationRegistration.Dispose();
                request?.Dispose();
                UnityAtomicFile.TryDelete(temporaryPath);
                completion.TrySetException(exception);
            }
        }

        private static void CompleteRequest(
            UnityWebRequest request,
            string temporaryPath,
            CancellationTokenRegistration cancellationRegistration,
            CancellationToken cancellationToken,
            TaskCompletionSource<Stream> completion)
        {
            cancellationRegistration.Dispose();

            if (cancellationToken.IsCancellationRequested)
            {
                request.Dispose();
                UnityAtomicFile.TryDelete(temporaryPath);
                completion.TrySetCanceled();
                return;
            }

            UnityWebRequest.Result result = request.result;
            long responseCode = request.responseCode;
            string error = request.error;
            request.Dispose();

            if (result != UnityWebRequest.Result.Success)
            {
                // Classify before deleting: the error body this may need to read lives in that same file.
                bool isNotFound = IsConfirmedAbsent(responseCode, temporaryPath);
                UnityAtomicFile.TryDelete(temporaryPath);
                completion.TrySetException(new ArtifactTransportException(
                    FailureMessage(responseCode, error),
                    IsTransient(result, responseCode),
                    isNotFound: isNotFound));
                return;
            }

            try
            {
                completion.TrySetResult(new TemporaryDownloadStream(temporaryPath));
            }
            catch (Exception exception)
            {
                UnityAtomicFile.TryDelete(temporaryPath);
                completion.TrySetException(exception);
            }
        }

        // A 404 is absence by definition. A 400 is not - it also covers genuinely malformed requests - but some
        // object stores answer a missing object with 400 and put the real status in the body (Supabase Storage:
        // {"statusCode":"404",...}). DownloadHandlerFile writes that body to the same temporary file, so the
        // store's own statement is available without a second request. Every other status stays unconfirmed.
        private static bool IsConfirmedAbsent(long responseCode, string temporaryPath)
        {
            if (responseCode == NOT_FOUND_STATUS_CODE)
            {
                return true;
            }

            if (responseCode != BAD_REQUEST_STATUS_CODE)
            {
                return false;
            }

            try
            {
                var responseFile = new FileInfo(temporaryPath);

                if (!responseFile.Exists
                    || responseFile.Length == 0
                    || responseFile.Length > ArtifactTransportResponseBody.MaximumInspectedBytes)
                {
                    return false;
                }

                return ArtifactTransportResponseBody.ConfirmsNotFound(File.ReadAllBytes(temporaryPath));
            }
            catch (Exception exception) when (
                exception is IOException
                || exception is UnauthorizedAccessException)
            {
                // The request has already failed; not being able to read its body only means the failure
                // stays unconfirmed.
                return false;
            }
        }

        private static string FailureMessage(long responseCode, string error)
        {
            if (responseCode != 0)
            {
                return $"The artifact request returned HTTP {responseCode}.";
            }

            return string.IsNullOrWhiteSpace(error)
                ? "The artifact request failed."
                : $"The artifact request failed: {error}";
        }

        private static bool IsTransient(
            UnityWebRequest.Result result,
            long responseCode)
        {
            if (result == UnityWebRequest.Result.ConnectionError)
            {
                return true;
            }

            return responseCode == 408
                || responseCode == 429
                || responseCode >= 500;
        }
    }
}
