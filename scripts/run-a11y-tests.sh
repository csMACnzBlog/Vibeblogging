#!/usr/bin/env bash
set -euo pipefail

SERVER_PID=""

cleanup() {
    local rc=$?
    echo "EXIT trap: cleaning up background processes..."
    if [ -n "$SERVER_PID" ]; then
        echo "Stopping HTTP server process group (PID: $SERVER_PID)..."
        # Kill the entire setsid process group (covers npx + http-server children)
        kill -- -"$SERVER_PID" 2>/dev/null || kill "$SERVER_PID" 2>/dev/null || true
        wait "$SERVER_PID" 2>/dev/null || true
    fi
    exit $rc
}
trap cleanup EXIT

echo "Starting HTTP server on port 8080..."
cd output
setsid npx http-server -p 8080 --silent &
SERVER_PID=$!
echo "HTTP server started (PID: $SERVER_PID)"

# Wait for the server to be ready (up to 5 seconds)
echo "Waiting for server to be ready..."
READY=0
for i in {1..10}; do
    if nc -z localhost 8080 2>/dev/null; then
        echo "Server is listening on port 8080"
        READY=1
        break
    fi
    sleep 0.5
done

if [ "$READY" -eq 0 ]; then
    echo "Error: HTTP server did not become ready on port 8080 after 5 seconds" >&2
    exit 1
fi

# Find all HTML files in the output directory
echo "Discovering HTML files..."
cd ..
HTML_FILES=$(find output -name "*.html" -type f | sed 's|output/||')

# Build URLs for pa11y-ci using an array to handle paths correctly
URLS=()
for file in $HTML_FILES; do
    URLS+=("http://localhost:8080/$file")
done

echo "Running accessibility tests on ${#URLS[@]} pages..."
npx pa11y-ci --config .pa11yci.json "${URLS[@]}"

echo "Accessibility tests completed!"
