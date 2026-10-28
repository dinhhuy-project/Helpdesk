FROM golang:1.24-bookworm AS build
ARG MC_VERSION=RELEASE.2025-08-13T08-35-41Z
ENV CGO_ENABLED=0
RUN go install github.com/minio/mc@${MC_VERSION}

FROM debian:bookworm-slim
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /go/bin/mc /usr/local/bin/mc
USER 10001:10001
ENTRYPOINT ["mc"]
