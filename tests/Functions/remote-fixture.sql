-- Dedicated test projects only. Apply the version-controlled table/GRANT/RLS migration
-- described in docs/cli/sync.md first. This file is never executed by gpk deploy-function.
-- psql: -v game_a_version=42 for project A; -v game_a_version=7 for project B.
begin;
insert into public.gamepatch_pointer (bucket, release_version)
values
    ('gpk-test-game-a', :'game_a_version'::bigint),
    ('gpk-test-game-b', 13),
    ('gpk-test-zero', 0),
    ('gpk-test-large', 9007199254740993),
    ('gpk-test-max', 9223372036854775807)
on conflict (bucket) do update
set release_version = excluded.release_version;
-- Absence must remain distinct from a published zero.
delete from public.gamepatch_pointer where bucket = 'gpk-test-unpublished';
commit;
