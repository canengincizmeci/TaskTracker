ARG GOSU_BUILD_IMAGE=golang:1.27.4-alpine3.24
ARG POSTGRES_BASE_IMAGE=postgres:16.16-trixie

FROM ${GOSU_BUILD_IMAGE} AS gosu-build
# Build the same gosu 1.20 source as the official PostgreSQL image with patched Go.
ENV GOTOOLCHAIN=local
RUN CGO_ENABLED=0 GOBIN=/out go install -trimpath github.com/tianon/gosu@29b939aa5bd4dc588af765b8248f6e39150d2565

FROM ${POSTGRES_BASE_IMAGE} AS postgres
COPY --from=gosu-build --chmod=0755 /out/gosu /usr/local/bin/gosu
RUN gosu --version && gosu nobody true

