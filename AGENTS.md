# Project instructions

## Agent models

- Use GPT-6.1 Sol (`gpt-6.1-sol`) for every agent and subagent created for this project, including nested delegation and review agents.
- Do not use GPT-6 Astra (`gpt-6-astra`). This project preference takes precedence over model-routing suggestions in skills.
- Set the model and reasoning effort explicitly when creating agents. Choose higher reasoning when the task benefits from it.
- Pass this preference to delegated agents. If GPT-6.1 Sol is unavailable, report that limitation instead of silently substituting another model.

## Development context

- Start M2 work with [the current roadmap](docs/ROADMAP.md) and [the M2 handoff](docs/M2-HANDOFF.md). Their revised M2 scope and audio requirements supersede conflicting historical restrictions for new M2 work; historical test results remain evidence only for their recorded revision.
- M2 prioritizes concurrent broadcasters, working audio, and fullscreen bars that hide automatically. Remove per-room participant/publisher quotas; do not add a replacement viewer cap or bandwidth-admission system. Eight-viewer capacity testing and large-group performance gates are outside M2 scope; address observed scale problems when needed.
