#!/bin/bash
set -e

# Generate unique node.id from NODE_ID env var or hostname
NODE_ID=${NODE_ID:-$(hostname)}

# Update node.properties with unique node.id
cat > /etc/trino/node.properties <<EOF
node.environment=production
node.id=${NODE_ID}
node.data-dir=/data/trino
EOF

echo "Worker started with NODE_ID: ${NODE_ID}"

# Start Trino
exec /usr/lib/trino/bin/run-trino
