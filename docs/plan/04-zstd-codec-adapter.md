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

## 산출물

- codec adapter, round-trip·결정성 테스트, platform smoke test

## 완료 기준 (검증 기준 19)

- 고정 option으로 같은 입력은 같은 zstd byte를 생성한다.
- streaming round-trip 후 원본 SHA-256이 복원된다.
- public API에 `NativeCompressions` type이 노출되지 않는다(architecture test).
- 지원 platform 목록에서 smoke test가 통과한다.
