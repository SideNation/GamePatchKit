# 핵심 원칙

1. **karpathy-guidelines** 스킬을 항상 참조한다.
2. **.claude/rules/minimal-implementation 룰을** 항상 참조한다.
3. **재현 가능성**: 모든 변경은 코드로 남긴다. 콘솔에서 수동으로 만든 리소스는 금지한다.

# 작업 기록 저장 규칙

## Agent Memory

- project slug: `gamepatchkit`
- related project slugs: []

다음 경우에 개인 `agent-memory-workflow` 스킬을 호출한다.

- 새 세션의 첫 의미 있는 작업 전
- 조회 시점을 놓쳤다면 알아차린 즉시
- 플러그인·런타임 제약으로 발생한 비직관적 버그를 해결했을 때
- 반복될 가능성이 있는 인프라 고유 제약을 확인했을 때
- 아키텍처나 설계 결정을 내렸을 때

조회·저장·삭제 방법과 관련 project slug의 조회 기준은 `agent-memory-workflow` 스킬을 따른다.

## worklog-workflow 호출 트리거

다음 상황에서 `worklog-workflow` 스킬을 호출한다:

- 작업이 일정 부분 완료됨 (기능 구현·버그 수정 마무리)
- 세션 종료·창 전환·다른 컴퓨터로 인수인계하기 직전
- 사용자가 "작업 일지", "일지 써줘/남겨줘", "인수인계 메모", "오늘 한 것 정리", "worklog", "handover note" 등으로 요청
호출하지 않는다:
- 단순 질문·조회, 동작 변화 없는 확인

# 워크 플로우 규칙

## codex-solo-workflow 호출 트리거

**Claude 에이전트는 이 규칙을 무시한다.**
**Codex를 단독 에이전트로 사용 중**(별도 Claude Code 오케스트레이션 없음)일 때, 다음 상황에서 `codex-solo-workflow` 스킬을 호출한다:

- 방금 코드를 수정했고 자체 검토가 필요함 → standard/adversarial self-review
- 고위험 변경(보안·인증·DB·마이그레이션·결제·동시성·공개 API·큰 리팩터·3개 초과 파일) → adversarial self-review
- 막혔거나 테스트가 반복 실패함 → rescue
- PR을 열거나 마감하기 직전 → PR review
호출하지 않는다:
- 코드 변경이 없고 사용자가 요청하지도 않음

## codex-collab-workflow 호출 트리거

#### **Claude Code로 작업 중**(Codex 플러그인 사용 가능)일 때, 다음 상황에서 `codex-collab-workflow` 스킬을 호출한다:

model은 `gpt-5.6-sol`을 사용한다.

- 방금 코드를 수정했고 2차 검토가 필요함 → standard review
- 고위험 변경(보안·인증·DB·마이그레이션·결제·동시성·공개 API·큰 리팩터·3개 초과 파일) → adversarial review
- 막혔거나 테스트가 반복 실패함 → rescue
- PR을 열거나 마감하기 직전 → PR review

#### 호출하지 않는다:

- 코드 변경이 없고 사용자가 요청하지도 않음

