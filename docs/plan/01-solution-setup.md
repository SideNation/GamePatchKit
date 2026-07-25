# 01. solution·프로젝트 구성

> PRD 섹션: 프로젝트 명명, 프로젝트 구조, 프로젝트 책임, 의존 방향

## 목표

PRD의 프로젝트 구조·target framework·의존 방향을 그대로 갖춘, 빌드와 테스트가
통과하는 빈 solution을 만든다. 이후 모든 단계는 이 구조 위에서 진행한다.

## 선행 단계

없음

## 작업 항목

### solution·공통 설정

- [x] `GamePatchKit.sln` 생성
- [x] `Directory.Packages.props`로 중앙 package version 관리 구성
- [x] `NativeCompressions.Zstandard` version을 중앙 설정에 고정한다(자동 업그레이드 금지)
- [x] `schemas/` 디렉터리, `README.md`, `LICENSE` placeholder 생성

### src 프로젝트 생성

| 프로젝트 | target framework |
| --- | --- |
| `GamePatchKit.Core` | `netstandard2.1` |
| `GamePatchKit.Runtime` | `netstandard2.1` |
| `GamePatchKit.Compression.NativeCompressions` | `netstandard2.1` |
| `GamePatchKit.Packager` | `net10.0` |
| `GamePatchKit.Cli` | `net10.0` |
| `GamePatchKit.DotNet` | `net10.0` |

- [x] src 프로젝트 6개 생성
- [x] tests 프로젝트 6개 생성 (Core, Compression.NativeCompressions, Packager, Runtime,
      DotNet, IntegrationTests)

### 의존 방향 연결

- [x] `Packager` → `Core`, `Packager` → `Compression.NativeCompressions`
- [x] `Compression.NativeCompressions` → `Core`
- [x] `Runtime` → `Core`
- [x] `DotNet` → `Runtime`, `Core`, `Compression.NativeCompressions`
- [x] `Cli` → `Packager`
- [x] `Packager`와 `Runtime`은 서로 참조하지 않는다

### 의존 방향 빌드 검증

- [x] 역방향·금지 참조를 검증하는 architecture test 추가
  - `Core`는 다른 GamePatchKit 프로젝트를 참조하지 않는다
  - `Core`·`Runtime`은 `UnityEngine`, `NativeCompressions` assembly를 참조하지 않는다
  - `Runtime`은 compression adapter를 참조하지 않는다

## 산출물

- 빌드 가능한 solution과 12개 프로젝트
- 의존 방향 architecture test

## 완료 기준

- `dotnet build`와 `dotnet test`가 통과한다.
- 프로젝트 참조가 PRD 의존 방향 다이어그램과 일치하고, 금지 참조를 추가하면
  architecture test가 실패한다.
- Core·Runtime assembly가 Unity API reference를 포함하지 않는다(검증 기준 21의 기반).
