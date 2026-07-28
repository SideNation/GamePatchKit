using System;
using System.IO;
using System.Linq;
using System.Threading;
using GamePatchKit.Runtime;
using GamePatchKit.Unity;
using UnityEngine;

public sealed class PatchQuickStart : MonoBehaviour
{
    [SerializeField] private string _baseUrl = "http://127.0.0.1:8080/";
    [SerializeField] private string _packageId = "unity-sample-data";
    [SerializeField] private string _dataVersion = "";
    [SerializeField] private string _manifestHash = "";
    [SerializeField] private string _optionalGroup = "maps";

    private readonly CancellationTokenSource _lifetime = new();

    private async void Start()
    {
        // transport와 storage는 반드시 main thread에서 만든다.
        var storage = new UnityRuntimeStorage();
        var runtime = new PackageRuntime(new UnityWebRequestArtifactTransport(_baseUrl), storage);
        var target = new TargetManifestReference(_packageId, _dataVersion, _manifestHash);

        try
        {
            // required group만 받는다. 이 await가 끝나면 게임을 시작할 수 있다.
            PackageState state = await runtime.InstallOrUpdateAsync(
                target,
                new Progress<PatchProgress>(p => Debug.Log($"[{p.Stage}] {p.CompletedFiles}/{p.TotalFiles}")),
                _lifetime.Token);
            Debug.Log($"required 완료: stateRevision={state.StateRevision}");

            // optional group은 필요한 시점에 따로 받는다.
            state = await runtime.InstallOptionalGroupsAsync(
                _packageId,
                new[] { _optionalGroup },
                cancellationToken: _lifetime.Token);
            Debug.Log($"optional 완료: stateRevision={state.StateRevision}");

            // 게임 데이터는 경로를 조합하지 말고 storage로 읽는다.
            PackageGroupState core = state.Groups.First(group => group.Name == "core");
            using (Stream stream = await storage.OpenInstallationFileAsync(
                       core.InstallationKey, "core/config.json", _lifetime.Token))
            using (var reader = new StreamReader(stream))
            {
                Debug.Log("core/config.json => " + await reader.ReadToEndAsync());
            }
        }
        catch (OperationCanceledException)
        {
            // 앱 종료. 검증된 cache는 남아 다음 실행에서 이어받는다.
        }
        catch (Exception exception)
        {
            // async void라 여기서 잡지 않으면 조용히 사라진다.
            Debug.LogException(exception);
        }
    }

    private void OnDestroy()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
