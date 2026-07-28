# GamePatchKit.Compression.NativeCompressions

GamePatchKit `ICompressionCodec`의 기본 zstd 구현. `NativeCompressions.Zstandard`를
사용하며 Packager와 Runtime host가 zstd artifact를 다룰 때 주입한다.

- target framework: `netstandard2.1`

## 사용법

```csharp
ICompressionCodec codec = ZstdCompressionCodecFactory.Create();

await using var source = File.OpenRead("payload.bin");
await using var compressed = File.Create("payload.bin.zst");
await codec.CompressAsync(source, compressed, CancellationToken.None);
```

구체 압축 라이브러리 type을 노출하지 않는다. 입력·출력 `Stream`의 수명은 호출자가
소유하고, codec 호출 후에도 두 stream은 열린 상태로 남는다.

## 고정 압축 계약

deterministic package를 위해 아래 값은 호출자가 바꿀 수 없다.

| 항목 | 값 |
| --- | --- |
| codec ID | `zstd` |
| compression level | `3` |
| content size flag | 비활성 |
| content checksum | 활성 |
| dictionary ID | 비활성 |
| compression worker | `0` (단일 thread) |
| streaming buffer | 64 KiB |

`NativeCompressions.Zstandard` version은 중앙에서 정확히 고정하며 자동 업그레이드하지
않는다.

## 지원 platform

Windows·Linux·macOS의 x64·arm64 desktop runtime을 검증 대상으로 한다. iOS IL2CPP는
`NativeCompressions.Zstandard` preview의 기본 지원 대상이 아니므로, 해당 target은
호환 codec을 직접 주입하거나 `compression: none`을 사용한다.

## 문서

- [README](https://github.com/SideNation/GamePatchKit/blob/main/README.md)
- [zstd codec 계약](https://github.com/SideNation/GamePatchKit/blob/main/docs/contracts/zstd-codec.md)

MIT License.
