# GitHub development

Repository: [vitor-tecc/PlayKross](https://github.com/vitor-tecc/PlayKross).

The project keeps its current `feat/milestone-1` implementation branch. The milestone is a development prototype with unresolved review findings and manual acceptance gates; pushing it does not declare it ready for release. Keep changes in reviewable commits and use pull requests to merge into `main` after the relevant checks and acceptance criteria are satisfied.

The Correctness workflow runs Windows .NET tests and WPF compilation, native policy/process tests without OBS integration, and Linux browser unit tests/build. It uses read-only repository permissions and immutable action commits, following the official [checkout](https://github.com/actions/checkout), [setup-dotnet](https://github.com/actions/setup-dotnet) and [setup-node](https://github.com/actions/setup-node) documentation. Dependabot proposes weekly library/action updates; pinned OBS/MediaMTX/Caddy downloads still require deliberate compatibility and real-media testing.

GitHub-hosted CI does not prove hardware encoding, smooth playback, process-audio isolation, internet performance or eight-viewer capacity. Run the local real-media checks separately and keep numeric evidence with its workload/topology. The CI plugin-free native build does not compile the OBS extension or patched capture library. The real-device AudioLoopback tone test is excluded on hosted runners, which may lack an audio endpoint; it remains in local automated verification.

Only source, logo, documentation, manifests and lock files belong in Git. `.tools`, `.runtime`, `.artifacts`, local credentials and generated build outputs stay ignored. See [workspace size](workspace-size.md). No runtime binaries, room invitations or session vaults are uploaded by the workflow.

For a fresh clone, follow [Windows setup](windows.md). Source still uses the existing `JKiller` project/assembly names to avoid unnecessary protocol, profile and build-path changes; the product name is PlayKross.
