#!/usr/bin/env bash
set -e

# Start HTTP server in background
echo "Starting HTTP server on port 8080..."
cd output
npx http-server -p 8080 --silent &
SERVER_PID=$!

# Give the server time to start
sleep 3

# Find all HTML files in the output directory
echo "Discovering HTML files..."
cd ..
HTML_FILES=$(find output -name "*.html" -type f | sed 's|output/||')

# Build URLs for pa11y-ci
URLS=""
for file in $HTML_FILES; do
  URLS="$URLS http://localhost:8080/$file"
done

echo "Running accessibility tests on $(echo $URLS | wc -w) pages..."
npx pa11y-ci --config .pa11yci.json $URLS

# Stop the server
echo "Stopping HTTP server..."
kill $SERVER_PID

echo "Accessibility tests completed!"
