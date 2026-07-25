# 10. adapter conformance fixture·test suite

> PRD 섹션: Runtime adapter contract, 외부 host 연동, 배포 산출물

## 목표

외부 host(Unity 등)가 자신의 `IArtifactTransport`·`IRuntimeStorage` 구현이 contract를
만족하는지 검증할 수 있는 공용 conformance fixture와 test suite를 제공한다.

## 선행 단계

08, 09

## 작업 항목

- [ ] 고정 conformance fixture 제작: file·bundle·part·압축·서명 조합과 손상 케이스를
      포함한 대표 manifest·artifact 세트
- [ ] adapter 구현을 주입받아 실행하는 공용 test suite (abstract 테스트 베이스 또는
      fixture runner 형태)
- [ ] 검증 항목: download plan 동일성, 검증·활성화 결과 동일성, 취소·재개 동작,
      current pointer 원자적 교체 계약
- [ ] in-memory fake adapter와 DotNet adapter를 같은 suite로 실행해 결과를 비교
- [ ] 외부 host가 소비할 배포 형태 결정 (NuGet test package 또는 소스 fixture)

## 산출물

- conformance fixture와 test suite, 두 adapter의 suite 통과 결과

## 완료 기준

- DotNet과 conformance용 fake adapter가 같은 fixture에서 동일한 download plan과
  활성화 결과를 만든다(검증 기준 14).
- 외부 adapter 구현만 주입하면 suite가 실행되는 형태로 배포할 수 있다.
- adapter 구현 차이로 활성화 결과가 달라지는 경우를 suite가 검출한다.
