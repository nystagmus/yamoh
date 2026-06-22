#!/bin/bash
# filepath: src/entrypoint.sh

set -e

# Set defaults if not provided
PUID=${PUID:-99}
PGID=${PGID:-100}

# Create group if needed
if ! getent group "$PGID" >/dev/null; then
    groupadd -g "$PGID" appgroup
fi

# Create user if needed
if ! id -u "$PUID" >/dev/null 2>&1; then
    useradd -u "$PUID" -g "$PGID" -M -s /bin/bash appuser
fi

echo "Starting Yamoh as UID=${PUID} GID=${PGID}"

# Run as the specified user, explicitly setting both UID and GID
exec gosu "${PUID}:${PGID}" dotnet Yamoh.dll