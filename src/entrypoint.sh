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
    useradd -u "$PUID" -g "$PGID" -s /bin/bash appuser
fi

# Run as the specified user
exec gosu "$PUID" dotnet Yamoh.dll