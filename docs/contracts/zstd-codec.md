# zstd codec

## 개요

`GamePatchKit.Compression.NativeCompressions`는 Core의 `ICompressionCodec`을
`NativeCompressions.Zstandard`로 구현한 기본 zstd adapter다. Packager 또는 Runtime
host가 zstd artifact를 처리할 때 이 구현을 주입한다.

## 사용법

`ZstdCompressionCodecFactory.Create()`는 구체 압축 라이브러리 type을 노출하지 않고
`ICompressionCodec`을 반환한다. 호출자는 입력과 출력 `Stream`의 수명을 소유하며,
codec 호출이 끝난 뒤에도 두 stream은 열린 상태로 유지된다.

```csharp
using System.IO;
using System.Threading;
using GamePatchKit.Compression.NativeCompressions;
using GamePatchKit.Core;

ICompressionCodec codec = ZstdCompressionCodecFactory.Create();

await using var source = File.OpenRead("payload.bin");
await using var compressed = File.Create("payload.bin.zst");
await codec.CompressAsync(source, compressed, CancellationToken.None);
```

압축 해제도 동일한 codec instance의 `DecompressAsync`에 압축 입력 stream과 복원 출력
stream을 전달한다.

## 고정 압축 계약

- codec ID: `zstd`
- compression level: `3`
- content size flag: 비활성
- content checksum: 활성
- dictionary ID: 비활성
- compression worker: `0`(zstd 단일-thread mode)
- streaming buffer: 64 KiB

이 값과 중앙 고정된 `NativeCompressions.Zstandard` version이 같으면 동일한 입력은
동일한 zstd byte를 생성한다. 옵션은 호출자가 변경할 수 없으며, 변경이 필요하면 새
artifact 생성 정책으로 별도 version 변경을 검토해야 한다.

## 에러 처리

codec은 upstream zstd 오류, stream 읽기·쓰기 오류와 `OperationCanceledException`을
변환하지 않고 호출자에게 전달한다. Packager와 Runtime은 실패한 출력 stream을
완성된 artifact로 사용하지 않아야 한다.

## 지원 플랫폼과 smoke test

v1 기본 adapter의 검증 대상은 다음 desktop runtime이다.

- Windows x64, arm64
- Linux x64, arm64
- macOS x64, arm64

iOS IL2CPP는 `NativeCompressions.Zstandard` 0.6.1 preview의 기본 지원 대상에서
제외한다. 그 밖의 target은 호환되는 별도 `ICompressionCodec`을 주입하거나 zstd
압축을 사용하지 않는다.

현재 환경에서 native runtime load와 round-trip을 확인하려면 다음 명령을 실행한다.
지원 대상 OS·architecture마다 같은 명령을 수동으로 실행한다.

```shell
dotnet test tests/GamePatchKit.Compression.NativeCompressions.Tests \
  --filter Category=PlatformSmoke
```

## 관련 파일

- [ICompressionCodec.cs](../../src/GamePatchKit.Core/ICompressionCodec.cs)
- [ZstdCompressionCodecFactory.cs](../../src/GamePatchKit.Compression.NativeCompressions/ZstdCompressionCodecFactory.cs)
