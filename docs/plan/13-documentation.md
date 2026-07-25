# 13. 문서화·배포 산출물 정리

> PRD 섹션: 배포 산출물, publish와 channel 연동, 외부 host 연동, 서명과 무결성

## 목표

README와 배포 산출물을 정리해 외부 사용자가 문서만으로 CLI, package 설정,
Runtime 통합, publisher 계약을 적용할 수 있게 한다.

## 선행 단계

01~12

## 작업 항목

### README

- [ ] 개요와 설치: .NET tool `gpk`, NuGet 4종(`Core`, `Runtime`,
      `Compression.NativeCompressions`, `DotNet`)
- [ ] `gamepatchkit.yml` 설정: 전체 필드 표, group 설계 규칙 요약, 예시 설정
- [ ] CLI 명령별 사용법, exit code 표, `--json` 출력, `--dry-run`
- [ ] Runtime 통합 가이드: DotNet adapter 사용 예시, 외부 host(Unity)의
      `IArtifactTransport`·`IRuntimeStorage`·codec 주입 구현 가이드,
      iOS IL2CPP 기본 codec 미지원 제약 명시
- [ ] publisher 계약: 업로드 순서(artifact → 검증 → manifest → 재검증 → channel
      교체), cache 정책, rollback
- [ ] 서명 운영: 키 생성·보관, key rotation, 서명 필수 모드

### schema·버전 정책

- [ ] versioned JSON Schema 배포 방식과 manifest 호환 버전 정책 문서화
      (schema와 manifest 호환 버전은 같은 repository release에서 함께 관리)
- [ ] adapter conformance fixture·test suite 사용법 문서화

### 배포 검증

- [ ] NuGet 4종과 `gpk` tool의 패키징 메타데이터 정리 (license, repository URL 등)
- [ ] 문서의 예시 명령·설정·코드가 실제 산출물로 동작하는지 확인

## 산출물

- README, schema 버전 정책 문서, conformance 사용법, 패키징 메타데이터

## 완료 기준

- 신규 사용자가 README만으로 최초 package → publish tree → Runtime 다운로드·활성화
  시나리오를 재현할 수 있다.
- 문서의 명령·설정·API가 구현과 일치한다.
- PRD 배포 산출물 목록이 모두 빌드 가능한 상태로 준비된다.
