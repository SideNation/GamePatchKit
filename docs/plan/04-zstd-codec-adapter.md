# 04. NativeCompressions zstd codec adapter

> PRD 섹션: 프로젝트 책임 `GamePatchKit.Compression.NativeCompressions`, 제약 / 비고

## 목표

Core의 `ICompressionCodec`을 NuGet `NativeCompressions.Zstandard`로 구현하고
deterministic streaming round-trip을 보장한다.

## 선행 단계

03

## 작업 항목

- [ ] `NativeCompressions.Zstandard`의 고정 version을 `Directory.Packages.props`에서
      확인·유지한다(자동 업그레이드 금지)
- [ ] streaming 압축·해제 구현 (전체 payload를 메모리에 올리지 않는다)
- [ ] compression level과 frame option을 상수로 고정한다
- [ ] deterministic package 생성을 위해 단일 compression worker를 사용한다
- [ ] Runtime에 주입할 zstd codec factory를 제공한다
- [ ] preview package 격리: `NativeCompressions` type을 public API에 노출하지 않고
      adapter 내부에서만 사용한다
- [ ] 선정 version의 native runtime 지원 platform 목록을 고정하고 platform smoke
      test를 만든다
- [ ] smoke test 실행 방식 문서화: 대상 OS 목록과 로컬·수동 실행 절차
      (특정 CI 제품 pipeline은 PRD 범위 밖)

## 산출물

- codec adapter, round-trip·결정성 테스트, platform smoke test

## 완료 기준 (검증 기준 19)

- 고정 option으로 같은 입력은 같은 zstd byte를 생성한다.
- streaming round-trip 후 원본 SHA-256이 복원된다.
- public API에 `NativeCompressions` type이 노출되지 않는다(architecture test).
- 지원 platform 목록에서 smoke test가 통과한다.

## 개발 v2

v1 계획은 보존하며, 아래 내용으로 04단계 구현 결과와 고정 계약을 추가한다.

### 구현 결정

- `NativeCompressions.Zstandard`는 `Directory.Packages.props`의 `[0.6.1]` exact
  version을 사용한다.
- adapter project는 upstream option의 `init` setter를 사용하기 위해 C# 9를
  명시하되 target framework는 `netstandard2.1`을 유지한다.
- `ZstdCompressionCodecFactory.Create()`는 `ICompressionCodec`을 반환하며
  upstream type을 public signature에 노출하지 않는다.
- 압축과 해제는 64 KiB buffer로 stream 간 복사하고 입력·출력 stream을 닫지 않는다.
- 압축 설정을 다음 값으로 고정한다.
  - compression level: `3`
  - content size flag: `false`
  - content checksum: `true`
  - dictionary ID flag: `false`
  - `NbWorkers`: `0`(zstd caller-thread single-thread mode)

`ICompressionCodec`에는 입력 길이 계약이 없으므로 content size를 frame에 기록하지
않는다. source stream의 seek 가능 여부나 read chunk 크기가 달라도 같은 byte를
생성하도록 결정성 테스트를 구성한다.

### 지원 platform과 smoke test

v1 기본 adapter의 검증 대상은 Windows·Linux·macOS의 x64와 arm64다. iOS IL2CPP는
선정한 preview package의 기본 지원 대상에서 제외한다. 그 밖의 target은 별도 호환
codec을 주입하거나 무압축 설정을 사용한다.

각 지원 OS·architecture에서 다음 명령을 수동으로 실행한다. 특정 CI 제품 설정은
04단계 산출물에 포함하지 않는다.

```shell
dotnet test tests/GamePatchKit.Compression.NativeCompressions.Tests \
  --filter Category=PlatformSmoke
```

자세한 호출 계약은 [zstd codec 문서](../contracts/zstd-codec.md)에 기록한다.

### 완료 상태

- [x] 중앙 package의 exact version 유지
- [x] 전체 payload를 메모리에 적재하지 않는 streaming 압축·해제
- [x] compression level·frame option·단일-thread worker 고정
- [x] Runtime 주입용 `ICompressionCodec` factory
- [x] upstream type public API 격리 architecture test
- [x] 결정성·streaming SHA-256 round-trip test
- [x] 지원 platform 목록과 platform smoke test·수동 실행 절차
