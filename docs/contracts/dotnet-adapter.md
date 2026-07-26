# DotNet adapter

## 개요

`GamePatchKit.DotNet`은 `GamePatchKit.Runtime`의 전송·저장소 계약을 일반 .NET 환경에
연결한다. `HttpArtifactTransport`가 `IArtifactTransport`를, `FileSystemRuntimeStorage`가
`IRuntimeStorage`를 구현하며, `DefaultCompressionCodecs`는 기본 zstd codec 구성을
도와준다. 이 프로젝트는 Runtime의 내부 규칙(download plan, manifest·hash 검증, retry,
활성화 규칙)을 복제하지 않고 전송·저장 I/O만 담당한다.

```csharp
using GamePatchKit.DotNet;
using GamePatchKit.Runtime;

public static class RuntimeSetup
{
    public static PackageRuntime Create(HttpClient httpClient, string runtimeRoot)
    {
        return new PackageRuntime(
            new HttpArtifactTransport(httpClient),
            new FileSystemRuntimeStorage(runtimeRoot),
            DefaultCompressionCodecs.Create());
    }
}
```

`httpClient.BaseAddress`는 반드시 `/`로 끝나야 한다. 상대 경로와 결합할 때 마지막
segment가 잘리는 것을 막기 위해서다 (`HttpClient`/`Uri` 상대 결합의 일반적인 규칙).

## filesystem layout

```text
<runtimeRoot>/
└── packages/
    └── <packageId>/
        ├── state/
        │   ├── package-state.json
        │   └── package-state.lock
        ├── cache/
        │   └── <relativePath 그대로>
        ├── installs/
        │   └── <localId>/
        └── staging/
            ├── <operationId>/
            └── scratch-<guid>.tmp
```

PRD가 정의한 고정 layout은 `state`, `manifests`, `cache`, `installs`, `staging` 다섯
디렉터리다. 이 어댑터는 그중 `manifests/`를 만들거나 채우지 않는다. `IRuntimeStorage`의
어떤 멤버도 storage에 manifest byte를 caching하라고 요청하지 않기 때문이다 — Runtime은
`OpenManifestAsync`/`OpenManifestSignatureAsync`로 항상 `IArtifactTransport`에서 다시
읽는다. 없는 디렉터리를 만들어 두는 것은 아무 의미가 없으므로 만들지 않는다.

`cache/`의 상대 경로는 Runtime이 넘겨주는 `relativePath`(= manifest artifact의
content-addressed 경로, 예: `<packageId>/artifacts/files/<hash>/content`)를 그대로
하위 경로로 쓴다. 이 경로는 Core의 content-addressed path 규칙에 따라 이미 자기
`packageId`를 포함하고 있으므로 `cache/<packageId>/artifacts/files/<hash>/content`처럼
`packageId` segment가 한 번 더 나타난다. 이 어댑터는 그 접두사를 벗기려 하지 않는다 —
Core의 구체적인 문자열 형식을 가정하는 대신, 넘어온 값을 불투명한 상대 경로로만 다룬다.

## `PackageState`의 원자적 교체

`ReplacePackageStateAsync`는 같은 `state/` 디렉터리에 `package-state.json.tmp-<guid>`를
새로 쓰고 `FileStream.Flush(flushToDisk: true)`로 디스크에 내린 뒤,
`File.Move(temp, final, overwrite: true)` 한 번으로 교체한다. 이 rename은 Windows와
Unix 모두에서 원자적이므로 읽는 쪽은 항상 이전 전체 byte 또는 새 전체 byte만 본다. 실패
시(`WriteAsync`/`Flush` 예외, 취소 포함) 임시 파일만 지우고 원래 `package-state.json`은
전혀 열지 않으므로 그대로 남는다.

`CreateCacheWriterAsync`도 같은 방식이다: `Content`는 임시 파일이며 `CommitAsync`가
성공한 뒤에만 `File.Move(..., overwrite: true)`로 최종 경로에 놓인다. 이 rename은 그
경로에 이미 있던 손상된 object도 덮어써 복구할 수 있다. `CommitAsync`를 부르지 않고
dispose하면 임시 파일만 지운다.

state·cache·installation을 읽는 모든 곳은 `FileShare.Read | FileShare.Delete`로 연다.
Windows에서 `File.Move(..., overwrite: true)`는 대상 파일에 열린 reader가 있으면 그
reader가 delete를 허용해야 교체가 성공한다 — `FileShare.Read`만 쓰면 다른 instance가
검증차 읽는 동안 정상적인 commit이 sharing violation으로 실패할 수 있다. Unix의
`rename()`은 열린 handle과 무관하게 항상 성공하므로 이 문제가 없다.

## writer lock

`AcquirePackageWriterLockAsync`는 `state/package-state.lock`을 `FileShare.None`으로
열어서 얻는 독점 OS handle을 lock으로 쓴다. 파일이 **존재하는지**가 아니라 그 handle을
**잡을 수 있는지**로 판단하므로, 이미 있는 lock 파일이 다른 프로세스의 잠금을 무시하고
통과시키는 일이 없다. 이미 열려 있으면 `IOException`을 잡고 짧은 대기 후 재시도한다.
handle을 dispose하면 잠금이 풀린다.

`IOException`은 "다른 writer가 잠그고 있다"만 뜻하지 않는다 — 디스크가 가득 찼거나 권한이
없어도 같은 예외가 난다. 그래서 재시도에는 30초 전체 예산이 있다: 그 안에 못 잡으면 마지막
`IOException`을 inner exception으로 담아 새 `IOException`을 던진다. 호출자가
`CancellationToken`을 주지 않아도(`CancellationToken.None`) 무한 대기로 빠지지 않는다.

## opaque `installationKey`

`InstallationExistsAsync`/`GetInstallationFilePathsAsync`/`OpenInstallationFileAsync`
세 멤버는 `packageId`를 받지 않고 opaque `installationKey` 하나만 받는다. 그래서 이
어댑터는 key 자체에 `packageId`를 실어 보낸다: `"<packageId>/<localId>"` (`localId`는
staging operation의 GUID를 그대로 재사용한다). `packageId`는 kebab-case
(`[a-z0-9-]+`), `localId`는 GUID `"N"` 형식이라 둘 다 `/`를 포함할 수 없으므로 첫
`/`로 분리하면 항상 원래 쌍으로 되돌아간다. Runtime은 이 문자열을 해석하지 않고 그대로
저장했다가 돌려줄 뿐이다.

이 key는 `PackageState.json` byte를 통해 round-trip하므로, 손상됐지만 schema상으로는
유효한 state 파일이 이 어댑터가 만들지 않은 key를 건네줄 수 있다. 그래서 분리한 뒤에도
`packageId`는 `KebabCaseId.IsValid`, `localId`는 `Guid.TryParseExact(_, "N", _)`로 다시
검증한다. 첫 `/`의 존재만 확인하면 예를 들어 `"pkg//tmp"`가 `localId == "/tmp"`로
쪼개지고, `Path.Combine`이 이를 Unix에서 절대 경로로 취급해 `installs/` 밖으로 완전히
벗어난다 — 검증을 안 하면 opaque key 하나가 그대로 경로 탈출 통로가 된다.

## staging과 promotion

`CreateStagingAreaAsync`가 만든 `staging/<operationId>/`는 `PromoteAsync`가
`Directory.Move`로 `installs/<operationId>/`로 rename하기 전까지 어떤 reader에도
보이지 않는다. `PromoteAsync` 없이 dispose되면 staging 디렉터리 전체를 지운다 — 실패한
시도가 디스크에 무한히 쌓이는 것을 막기 위해서다. `CreateScratchStreamAsync`는
`staging/` 아래에 `FileOptions.DeleteOnClose`로 임시 파일을 만들어 읽기·쓰기·seek가
모두 되는 stream을 주고, dispose 시 자동으로 지운다.

## path 안전성

`AdapterPath.Resolve`는 `GamePatchKit.Packager`의 `PackagePath.Resolve`와 같은 위협을
방어한다: 결합된 경로가 root를 벗어나지 않는지, 경로의 각 구성 요소에 symlink나 reparse
point가 없는지 확인한다. `cache/`와 `installs/`에 쓰기 전 마지막 방어선이다. 이 두
프로젝트는 서로 참조할 수 없는 관계(`GamePatchKit.DotNet`은 `GamePatchKit.Packager`를
참조하지 않는다)라 로직을 공유하지 못하고 각자 유지한다.

이 검사는 check-then-use 구조라 TOCTOU 여지가 있다: 검사가 끝난 뒤, 실제
`FileStream`을 열거나 `Directory.CreateDirectory`를 부르기 전 사이에 symlink가
끼어들면 write가 root 밖으로 향할 수 있다. Packager의 `PackagePath.Resolve`도 같은
구조라 이는 새로 생긴 약점이 아니라 두 구현이 이미 공유하는 것이다. 신뢰 경계는
`runtimeRoot`를 이 adapter(또는 신뢰하는 단일 프로세스)만 write할 수 있다는 전제다 —
같은 디렉터리에 다른 프로세스가 쓸 수 있다면 이 전제가 깨진다. rooted handle 기반
탐색(`openat`+`O_NOFOLLOW` 계열, Windows의 reparse point 비추적 handle)으로 바꾸는 건
이번 단계 범위 밖으로 남겨 뒀다.

## HTTP 요청과 실패 분류

`HttpArtifactTransport`는 재시도하지 않는다 — `ArtifactTransportException.IsTransient`만
정확히 보고하고, 재시도 여부와 횟수는 전부 Runtime의 몫이다.

manifest·signature 경로는 Packager의 publish tree와 같은 규칙으로 이 어댑터가 직접
만든다 (`GamePatchKit.Packager.PackageLayout`을 참조할 수 없으므로 같은 문자열을 각자
갖는다):

```text
<packageId>/manifests/<manifestHash>/manifest.json
<packageId>/manifests/<manifestHash>/manifest.sig
```

artifact 경로는 Runtime이 넘겨주는 `relativePath`를 그대로 쓴다 — 이미 자기
`packageId/artifacts/...` 접두사를 포함하고 있으므로 다시 붙이지 않는다.

| 상황 | `IsTransient` |
| --- | --- |
| HTTP 408, 429, 5xx | `true` |
| 그 외 실패 상태 코드(404, 401, 403, 410 등) | `false` |
| `HttpRequestException`(연결 실패 등) | `true` |
| 호출자 `CancellationToken`이 취소됨 | 감싸지 않고 그대로 전달 |
| `HttpClient.Timeout`처럼 호출자 token은 안 취소됐는데 발생한 `OperationCanceledException` | `true`로 감싸서 전달 |

마지막 두 줄은 흔한 함정을 구분한다: `HttpClient.Timeout`은 호출자의 token을 취소하지
않고도 `OperationCanceledException`을 던진다. `cancellationToken.IsCancellationRequested`로
둘을 구분하지 않으면 진짜 취소까지 재시도 대상으로 잘못 분류되거나, timeout이 취소로
오인돼 Runtime이 재시도를 건너뛰게 된다.

응답 stream을 담은 `HttpResponseBodyStream`은 dispose될 때 내부 `HttpResponseMessage`도
함께 dispose한다. Runtime이 반환 stream을 dispose할 책임을 지므로(`IArtifactTransport`
계약), 이 연결 해제도 같이 따라오게 하기 위해서다 — 그러지 않으면 대량의 artifact를
순회하는 동안 연결이 계속 열린 채로 남을 수 있다.

위 분류는 응답 header를 받는 시점까지만 적용되는 게 아니다 — `HttpResponseBodyStream`의
`Read`/`ReadAsync`도 같은 규칙으로 body 도중 실패를 감싼다. 큰 artifact를 내려받는
도중 연결이 끊기는 건 header 단계 실패만큼이나 흔하고 재시도 가능한 상황인데, 감싸지
않으면 원본 `HttpRequestException`이 그대로 빠져나가 Runtime의 재시도 판단
(`ArtifactTransportException.IsTransient`)을 완전히 건너뛴다.

## 시작 시 필수 package 검증

ASP.NET·worker가 시작 전 필수 package를 검증하고 준비되지 않으면 기동을 실패시키는
것은, 이미 있는 `InstallOrUpdateAsync`를 시작 경로에서 그대로 호출하고 예외를 그대로
전파시키는 것과 같다. 별도 wrapper 없이 이 하나로 충분하다:

```csharp
PackageRuntime runtime = RuntimeSetup.Create(httpClient, runtimeRoot);
// RuntimeException·OperationCanceledException이 그대로 던져지면 앱이 시작되지 않는다.
await runtime.InstallOrUpdateAsync(target, cancellationToken: startupCancellationToken);

var app = builder.Build();
app.Run();
```

## 관련 파일

- [HttpArtifactTransport.cs](../../src/GamePatchKit.DotNet/HttpArtifactTransport.cs)
- [HttpResponseBodyStream.cs](../../src/GamePatchKit.DotNet/HttpResponseBodyStream.cs)
- [FileSystemRuntimeStorage.cs](../../src/GamePatchKit.DotNet/FileSystemRuntimeStorage.cs)
- [FileSystemCacheWriter.cs](../../src/GamePatchKit.DotNet/FileSystemCacheWriter.cs)
- [FileSystemStagingArea.cs](../../src/GamePatchKit.DotNet/FileSystemStagingArea.cs)
- [AdapterLayout.cs](../../src/GamePatchKit.DotNet/AdapterLayout.cs)
- [AdapterPath.cs](../../src/GamePatchKit.DotNet/AdapterPath.cs)
- [DefaultCompressionCodecs.cs](../../src/GamePatchKit.DotNet/DefaultCompressionCodecs.cs)
