#nullable enable
using System;
using System.IO;
using System.Text;
using System.Threading;
using GamePatchKit.Unity;
using UnityEngine;

// PatchClient를 손으로 시험하는 IMGUI 패널이다. Base URL은 씬의 인스펙터에서 설정하고, Play 모드에서 releaseVersion을
// 넣고 Sync를 누른다. 픽스처 버킷은 scripts/serve-fixtures.sh로 띄우고, 실제 Supabase 공개 버킷 URL을 넣어도 된다.
public sealed class PatchClientSample : MonoBehaviour
{
    private const string DefaultBaseUrl = "http://127.0.0.1:8765/";
    private const string RootDirectoryName = "GamePatchKit";
    private const string ManifestFileName = "manifest.json";
    private const int MaxLogLines = 30;
    private const float ReferenceDpi = 120f;
    private const float Margin = 12f;
    private const float TreeHeight = 180f;
    private const float LogHeight = 220f;

    // 게시된 객체 이름을 뒤에 붙일 URL prefix. 씬에서 설정한다.
    [SerializeField] private string _baseUrl = DefaultBaseUrl;

    private readonly StringBuilder _log = new StringBuilder();

    private string _releaseVersion = "0";
    private string _rootPath = string.Empty;
    private string _tree = string.Empty;
    private bool _isRunning;
    private CancellationTokenSource? _cancellation;
    private Vector2 _panelScroll;
    private int _logLineCount;

    private void Awake()
    {
        _rootPath = Path.Combine(Application.persistentDataPath, RootDirectoryName);
        RefreshTree();
    }

    private void OnDestroy()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
    }

    private void OnGUI()
    {
        // 모바일 화면에서도 읽히도록 DPI에 따라 확대한다. 뒤에 그리는 다른 GUI가 이 배율을 물려받지 않도록 복원한다.
        Matrix4x4 previousMatrix = GUI.matrix;
        float scale = Mathf.Max(1f, Screen.dpi / ReferenceDpi);

        try
        {
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float width = Screen.width / scale - Margin * 2f;
        float height = Screen.height / scale - Margin * 2f;
        GUILayout.BeginArea(new Rect(Margin, Margin, width, height));

        // 작은 화면에서 아래쪽 내용이 화면 밖으로 밀리지 않도록 패널 전체를 스크롤한다.
        _panelScroll = GUILayout.BeginScrollView(_panelScroll);

        GUILayout.Label("Base URL (씬에서 설정, 실행 중 수정 가능)");
        _baseUrl = GUILayout.TextField(_baseUrl);
        GUILayout.Label("releaseVersion (게임 서버가 알려준 값)");
        _releaseVersion = GUILayout.TextField(_releaseVersion);
        GUILayout.Label($"rootPath: {_rootPath}");

        GUILayout.BeginHorizontal();
        GUI.enabled = !_isRunning;

        if (GUILayout.Button("Sync"))
        {
            StartSync();
        }

        GUI.enabled = _isRunning;

        if (GUILayout.Button("Cancel"))
        {
            _cancellation?.Cancel();
        }

        GUI.enabled = !_isRunning;

        if (GUILayout.Button("Delete local root"))
        {
            DeleteRoot();
        }

        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.Label("로컬 상태");
        GUILayout.TextArea(_tree, GUILayout.Height(TreeHeight));
        GUILayout.Label("로그");
        GUILayout.TextArea(_log.ToString(), GUILayout.Height(LogHeight));
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        }
        finally
        {
            GUI.matrix = previousMatrix;
        }
    }

    private async void StartSync()
    {
        if (!long.TryParse(_releaseVersion, out long releaseVersion))
        {
            Log("releaseVersion은 정수여야 합니다.");
            return;
        }

        _isRunning = true;
        _cancellation = new CancellationTokenSource();

        try
        {
            var client = new PatchClient(_baseUrl, _rootPath);
            Log($"Sync 시작: releaseVersion={releaseVersion}");
            PatchSyncResult result = await client.SyncAsync(releaseVersion, _cancellation.Token);

            if (result.IsAlreadyUpToDate)
            {
                Log($"이미 최신입니다: releaseVersion={result.ReleaseVersion}");
            }
            else
            {
                string previous = result.PreviousReleaseVersion?.ToString() ?? "없음";
                Log(
                    $"동기화 완료: releaseVersion={result.ReleaseVersion} (이전: {previous}), "
                    + $"downloaded={result.DownloadedCount}, reused={result.ReusedCount}, downloadedBytes={result.DownloadedBytes}, "
                    + $"extracted={result.ExtractedCount}, removed={result.RemovedCount}");
            }
        }
        catch (OperationCanceledException)
        {
            Log("취소됐습니다.");
        }
        catch (PatchClientException exception)
        {
            Log($"실패: {exception.Message}");
        }
        catch (ArgumentException exception)
        {
            Log($"입력 오류: {exception.Message}");
        }
        finally
        {
            _isRunning = false;
            _cancellation?.Dispose();
            _cancellation = null;
            RefreshTree();
        }
    }

    private void DeleteRoot()
    {
        try
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }

            Log("로컬 루트를 지웠습니다.");
        }
        catch (IOException exception)
        {
            Log($"로컬 루트를 지우지 못했습니다: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            Log($"로컬 루트를 지우지 못했습니다: {exception.Message}");
        }

        RefreshTree();
    }

    private void RefreshTree()
    {
        var tree = new StringBuilder();
        string manifestPath = Path.Combine(_rootPath, ManifestFileName);
        tree.AppendLine(File.Exists(manifestPath) ? "manifest.json: 있음" : "manifest.json: 없음");
        string dataPath = Path.Combine(_rootPath, "data");

        if (Directory.Exists(dataPath))
        {
            foreach (string filePath in Directory.GetFiles(dataPath, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(dataPath, filePath).Replace(Path.DirectorySeparatorChar, '/');
                tree.AppendLine($"data/{relativePath} ({new FileInfo(filePath).Length} bytes)");
            }
        }

        _tree = tree.ToString();
    }

    private void Log(string message)
    {
        // 줄 수를 세어 오래된 줄을 지우므로 여러 줄 메시지를 한 줄로 만든다.
        string singleLine = message.Replace('\r', ' ').Replace('\n', ' ');
        _log.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {singleLine}\n");
        _logLineCount++;

        if (_logLineCount > MaxLogLines)
        {
            int lastLineStart = _log.ToString().TrimEnd('\n').LastIndexOf('\n');

            if (lastLineStart >= 0)
            {
                _log.Length = lastLineStart + 1;
                _logLineCount--;
            }
        }

        Debug.Log(message);
    }
}
