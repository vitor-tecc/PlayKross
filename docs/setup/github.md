# GitHub development

Repository: [vitor-tecc/PlayKross](https://github.com/vitor-tecc/PlayKross).

M1 has practical acceptance with known limitations as of 2026-10-16; see the [current roadmap](../ROADMAP.md). Its integration uses [PR #3](https://github.com/vitor-tecc/PlayKross/pull/3) against `main`. Start M2 from the updated `main` after that PR is merged, using a new `codex/` feature branch; retain `feat/milestone-1` as historical context. Startup stutter and unmeasured external benchmarks are deferred, not marked passed. Keep changes in reviewable commits and preserve required checks; practical milestone acceptance does not certify a general production release.

The project owner and contributor is [@vitor-tecc](https://github.com/vitor-tecc). This checkout uses repository-local `user.name` and the account's GitHub-associated no-reply email, so future commits can be attributed without exposing a personal address. Do not override that identity with the earlier unassociated Codex placeholder. Existing published history is preserved; the milestone squash commit and future commits provide account-linked attribution. See [contributors](../../CONTRIBUTORS.md), the [GitHub email reference](https://docs.github.com/en/account-and-profile/reference/email-addresses-reference), and [M2 handoff](../M2-HANDOFF.md).

The Correctness workflow runs Windows .NET tests and WPF compilation, native policy/process tests without OBS integration, and Linux browser unit tests/build. It uses read-only repository permissions and immutable action commits, following the official [checkout](https://github.com/actions/checkout), [setup-dotnet](https://github.com/actions/setup-dotnet) and [setup-node](https://github.com/actions/setup-node) documentation. Dependabot proposes weekly library/action updates; pinned OBS/MediaMTX/Caddy downloads still require deliberate compatibility and real-media testing.

GitHub-hosted CI does not prove hardware encoding, smooth playback, process-audio isolation, internet performance or eight-viewer capacity. Run the local real-media checks separately and keep numeric evidence with its workload/topology. The CI plugin-free native build does not compile the OBS extension or patched capture library. The real-device AudioLoopback tone test is excluded on hosted runners, which may lack an audio endpoint; it remains in local automated verification.

Only source, logo, documentation, manifests and lock files belong in Git. `.tools`, `.runtime`, `.artifacts`, local credentials and generated build outputs stay ignored. See [workspace size](workspace-size.md). No runtime binaries, room invitations or session vaults are uploaded by the workflow.

For a fresh clone, follow [Windows setup](windows.md). Source still uses the existing `JKiller` project/assembly names to avoid unnecessary protocol, profile and build-path changes; the product name is PlayKross.
