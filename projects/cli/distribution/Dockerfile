FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled-extra@sha256:4bf18f918ddae188e11fc4a496e36eae78c43c927720b162bcd8a567e9bebc30

ARG VERSION
ARG REVISION

LABEL org.opencontainers.image.title="Luna" \
      org.opencontainers.image.description="LunaPack command-line interface" \
      org.opencontainers.image.version="$VERSION" \
      org.opencontainers.image.revision="$REVISION" \
      org.opencontainers.image.source="https://github.com/lunarisdigitalsolutions/lunapack" \
      org.opencontainers.image.licenses="MIT"

WORKDIR /workspace
COPY --chmod=0555 luna /usr/local/bin/luna

USER app
ENTRYPOINT ["/usr/local/bin/luna"]
