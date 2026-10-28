FROM golang:1.24-bookworm AS build
ARG MINIO_VERSION=RELEASE.2025-10-15T17-29-55Z
ENV CGO_ENABLED=0
RUN go install github.com/minio/minio@${MINIO_VERSION}

FROM golang:1.24-bookworm AS client
ARG MC_VERSION=RELEASE.2025-08-13T08-35-41Z
ENV CGO_ENABLED=0
RUN go install github.com/minio/mc@${MC_VERSION}

FROM debian:bookworm-slim
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir /data \
    && chown 10001:10001 /data
COPY --from=build /go/bin/minio /usr/local/bin/minio
COPY --from=client /go/bin/mc /usr/local/bin/mc
COPY entrypoint.sh initialize.sh verify.sh attachments-policy.json /opt/helpdesk/
USER 10001:10001
EXPOSE 9000 9001
ENTRYPOINT ["/bin/sh", "/opt/helpdesk/entrypoint.sh"]
CMD ["server", "/data", "--console-address", ":9001"]
