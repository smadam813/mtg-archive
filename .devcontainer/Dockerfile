FROM mcr.microsoft.com/devcontainers/dotnet:2-10.0-noble

# Mount points for the named volumes in devcontainer.json. Docker seeds a new
# volume with the image directory's owner, so the user owns them on first use.
RUN mkdir -p /home/vscode/.claude /commandhistory \
    && chown vscode:vscode /home/vscode/.claude /commandhistory

# Without pipefail a failed curl below writes an empty sources list and the
# build fails later at apt-get install with a misleading error.
SHELL ["/bin/bash", "-o", "pipefail", "-c"]

# NVIDIA container toolkit for the inner (docker-in-docker) daemon, so
# containers started inside can request the GPU the host passed through.
# `nvidia-ctk runtime configure` writes /etc/docker/daemon.json, which the
# feature's dockerd reads at start. No official feature installs the toolkit
# (nvidia-cuda installs CUDA libraries only). See ADR 0008.
RUN curl -fsSL https://nvidia.github.io/libnvidia-container/gpgkey \
      | gpg --dearmor -o /usr/share/keyrings/nvidia-container-toolkit-keyring.gpg \
    && curl -fsSL https://nvidia.github.io/libnvidia-container/stable/deb/nvidia-container-toolkit.list \
      | sed 's#deb https://#deb [signed-by=/usr/share/keyrings/nvidia-container-toolkit-keyring.gpg] https://#g' \
      > /etc/apt/sources.list.d/nvidia-container-toolkit.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends nvidia-container-toolkit \
    && rm -rf /var/lib/apt/lists/* \
    && nvidia-ctk runtime configure --runtime=docker
