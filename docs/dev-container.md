# Dev container

`.devcontainer/` is an isolated environment for the repo: .NET 10 SDK, a private Docker daemon (docker-in-docker), `gh`, and Claude Code. The dev loop in the README works unchanged inside it, GPU overlay included. Why docker-in-docker and what it costs: `docs/adr/0008-devcontainer-true-dind.md`.

Requirements: Docker Desktop and VS Code with the Dev Containers extension. A host NVIDIA GPU is optional.

1. In VS Code, run **Dev Containers: Clone Repository in Container Volume** and pick this repo. A volume clone builds faster than a bind mount from Windows. Opening the folder from a bind mount also works, but it shares `bin/` and `obj/` with the host. Run `dotnet restore` on whichever side you build next.
2. First open only: run `gh auth login` and `claude` and log in to each. Claude Code config persists in the `mtg-archive-claude-config` volume across rebuilds. The `gh` login persists across restarts, not rebuilds.
3. `docker compose up postgres ollama`, then `dotnet run --project src/MtgArchive`. Ports 8080, 5432, and 11434 forward to the host.

The GPU passes through when the host daemon reports the `nvidia` runtime (`docker info`). Without it the container still starts; only the gpu overlay fails. Docker Desktop caps VM memory at half the host RAM by default. If Ollama, Postgres, and the test suite together run short, raise it: on Windows, `memory=` under `[wsl2]` in `%UserProfile%\.wslconfig`; on macOS, Docker Desktop settings, Resources.

On Windows, a bulk re-embed leaves the page cache from the vector writes in the WSL2 VM, so `vmmem` stays high after the run. Set `autoMemoryReclaim=gradual` under `[experimental]` in `%UserProfile%\.wslconfig`. Then run `wsl --shutdown` once. The VM returns idle cache to Windows.

The container works on Windows and macOS, Apple Silicon included; every image is multi-arch. On macOS there is no GPU passthrough into Docker, so Ollama runs on the CPU there and the gpu overlay does not apply.

The container is privileged, so it is a working-tree boundary, not a security boundary. There is no egress firewall.
