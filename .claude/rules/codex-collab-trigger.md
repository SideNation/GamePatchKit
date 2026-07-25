# codex-collab-workflow 호출 트리거

**Claude Code로 작업 중**(Codex 플러그인 사용 가능)일 때, 다음 상황에서 `codex-collab-workflow` 스킬을 호출한다:

- 방금 코드를 수정했고 2차 검토가 필요함 → standard review (Claude가 `/codex:rescue` review-only로 수행)
- 고위험 변경(보안·인증·DB·마이그레이션·결제·동시성·공개 API·큰 리팩터·3개 초과 파일) → adversarial review (rescue review-only, 공격적 관점)
- 막혔거나 테스트가 반복 실패함 / 근본 원인 불명확 / 2차 전략 필요 → rescue (수정·진단, write 가능)
- PR을 열거나 마감하기 직전 → PR review

호출하지 않는다:

- 코드 변경이 없고 사용자가 요청하지도 않음

참고: 리뷰는 Claude가 `/codex:rescue`(review-only)로 수행한다. 전용 리뷰어(`/codex:review`·`/codex:adversarial-review`, review-gate·status/result)는 `disable-model-invocation`이라 사용자가 직접 실행한다.
