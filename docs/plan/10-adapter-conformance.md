# 10. adapter conformance fixture·test suite

> PRD 섹션: Runtime adapter contract, 외부 host 연동, 배포 산출물

## 목표

외부 host(Unity 등)가 자신의 `IArtifactTransport`·`IRuntimeStorage` 구현이 contract를
만족하는지 검증할 수 있는 공용 conformance fixture와 test suite를 제공한다.

## 선행 단계

08, 09

## 작업 항목

- [x] 고정 conformance fixture 제작: file·bundle·part·압축 조합, 잘못된
      discriminator·참조·정렬과 손상 artifact를 포함한 unsigned manifest·artifact 세트
      (`ConformanceFixture`가 `FilePackageBuilder`로 실제 release를 만들고, 잘못된
      manifest는 기존 `tests/fixtures/release-manifest/invalid/*.json`을 재사용)
- [x] adapter 구현을 주입받아 실행하는 공용 test suite (abstract 테스트 베이스 또는
      fixture runner 형태) — `ConformanceTestBase` abstract xUnit test base
- [x] 검증 항목: download plan 동일성, 검증·활성화 결과 동일성, 취소·재개 동작,
      `manifestHash` 검증, package writer 직렬화, `PackageState` atomic read·replace,
      opaque `installationKey`, immutable installation 승격 계약
- [x] required-only 최초 설치와 optional `notInstalled`·`ready`·`stale` 전환 fixture
- [x] state 교체 전후 failure injection에서 old/new state 전체 중 하나만 관찰되는지 검증
- [x] 2개 이상 group activation batch에서 준비 순서·단일 group 실패·동시 revision
      충돌을 주입하고 부분 `ready` state가 노출되지 않는지 검증
- [x] in-memory fake adapter와 DotNet adapter를 같은 suite로 실행해 결과를 비교
- [ ] signed fixture와 signature 검증 case는 11단계에서 같은 suite의 extension으로
      추가

## 결정 사항

- [x] 외부 host가 소비할 배포 형태 선정: NuGet test package 또는 소스 fixture 중
      외부 adapter 구현만 주입해 실행하기 쉬운 형태를 선택한다.
      → 소스 fixture로 결정: xUnit 기반 `GamePatchKit.Conformance` 프로젝트를
      소스로 제공하고 `ConformanceTestBase`를 상속해 사용한다. 이 저장소엔 아직
      NuGet 배포 파이프라인이 없어 소스 형태가 가장 단순하며, 실제 NuGet
      패키징 여부는 13단계(문서화·배포 산출물)에서 별도로 정한다.

## 산출물

- conformance fixture와 test suite, 두 adapter의 suite 통과 결과

## 완료 기준

- DotNet과 conformance용 fake adapter가 같은 fixture에서 동일한 download plan과
  활성화 결과를 만든다(검증 기준 14).
- 외부 adapter 구현만 주입하면 suite가 실행되는 형태로 배포할 수 있다.
- adapter 구현 차이로 활성화 결과가 달라지는 경우를 suite가 검출한다.
- fake와 DotNet adapter가 같은 `PackageState` revision·group 상태 전환 결과를 만든다
  (검증 기준 22, 23).
- 여러 group batch를 단일 state revision으로 commit하는 계약을 모든 adapter가
  만족한다(검증 기준 23).
- 모든 adapter가 공용 unsigned manifest golden·negative vector에서 같은 검증 결과를
  만든다(검증 기준 25, unsigned 범위).
