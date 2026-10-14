# Dev container runs the compose stack in docker-in-docker

The dev container (`.devcontainer/`) runs a private, privileged Docker daemon inside the container, and the repo's compose stack runs against that daemon. The alternative was to let the devcontainer tooling own `postgres` and `ollama` as compose sidecars on the host daemon, with docker-in-docker kept only for Testcontainers. We chose true docker-in-docker because the dev loop inside the container is then identical to the one on the host. That loop is `docker compose up postgres ollama`, then `dotnet run` against `localhost`. Both compose overlays (`compose.yaml`, `compose.gpu.yml`) work unchanged. Sidecars would have needed two devcontainer configs (cpu and gpu), a dev-service overlay, and hostname overrides for the dev loop.

**Considered and accepted costs.**

- The container is privileged. Code inside it, agents included, can reach the Docker Desktop VM and every container on it. The isolation is a working-tree and process boundary, not a security boundary. Testcontainers needs a daemon either way, so sidecars would not have avoided this.
- Disk doubles. The inner daemon has its own image store, so it pulls its own pgvector and Ollama images and its own embedding model. Volumes persist across rebuilds in the feature's named volume.
- A rebuild restarts the stack. The inner daemon dies with the container; `docker compose up` again after a rebuild.
- GPU passthrough into the inner daemon needs the NVIDIA container toolkit installed in the image and registered with the inner daemon. No official devcontainer feature does this, so the Dockerfile does it by hand. Verified on Docker Desktop for Windows (WSL2 backend) with an RTX 4080: an inner container started with `--gpus all`, and one started through the compose `deploy.resources.reservations.devices` form, both saw the GPU.

Memory does not double by nesting: inner containers are processes in the same VM. The Docker Desktop VM memory cap is the limit, the same as on the host.
