#!/bin/bash

echo "🐳 Testing Docker Flag Functionality"
echo "=================================="

# Count initial containers
INITIAL_COUNT=$(podman ps | grep cidx | wc -l)
echo "📊 Initial CIDX container count: $INITIAL_COUNT"

# Create a test repository folder to simulate registration
TEST_REPO="manual-docker-test-$(date +%s)"
REPO_PATH="/tmp/$TEST_REPO"

mkdir -p "$REPO_PATH"
cd "$REPO_PATH"

# Initialize a git repo
git init
echo "# Test repo" > README.md
git add README.md
git config user.email "test@example.com"
git config user.name "Test User"
git commit -m "Initial commit"

echo "📝 Created test repository at: $REPO_PATH"

# Test cidx commands manually with --force-docker
echo "🔧 Testing cidx init with --force..."
cidx init --embedding-provider voyage-ai --force

if [ $? -eq 0 ]; then
    echo "✅ cidx init --force succeeded"
    
    echo "🚀 Testing cidx start with --force-docker..."
    cidx start --force-docker
    
    if [ $? -eq 0 ]; then
        echo "✅ cidx start --force-docker succeeded"
        
        # Check container count after start
        AFTER_START_COUNT=$(podman ps | grep cidx | wc -l)
        echo "📊 Containers after start: $AFTER_START_COUNT"
        
        # Wait a moment
        sleep 5
        
        echo "🛑 Testing cidx stop with --force-docker..."
        cidx stop --force-docker
        
        if [ $? -eq 0 ]; then
            echo "✅ cidx stop --force-docker succeeded"
            
            echo "🗑️ Testing cidx uninstall with --force-docker..."
            cidx uninstall --force-docker
            
            if [ $? -eq 0 ]; then
                echo "✅ cidx uninstall --force-docker succeeded"
                
                # Check final container count
                sleep 3
                FINAL_COUNT=$(podman ps | grep cidx | wc -l)
                echo "📊 Final CIDX container count: $FINAL_COUNT"
                
                # Check if any containers with our test name are still running
                TEST_CONTAINERS=$(podman ps | grep "$TEST_REPO" | wc -l)
                echo "🔍 Test repo containers still running: $TEST_CONTAINERS"
                
                if [ $TEST_CONTAINERS -eq 0 ]; then
                    echo "🎉 SUCCESS: cidx uninstall --force-docker properly cleaned up containers!"
                else
                    echo "❌ FAILURE: Test repo containers still running after uninstall"
                    podman ps | grep "$TEST_REPO"
                fi
                
            else
                echo "❌ cidx uninstall --force-docker failed"
            fi
        else
            echo "❌ cidx stop --force-docker failed"
        fi
    else
        echo "❌ cidx start --force-docker failed"
    fi
else
    echo "❌ cidx init --force-docker failed"
fi

# Cleanup
cd /
rm -rf "$REPO_PATH"

echo ""
echo "🏁 Test completed"
echo "📊 Summary:"
echo "   Initial containers: $INITIAL_COUNT"
echo "   After start: ${AFTER_START_COUNT:-'N/A'}"
echo "   Final containers: ${FINAL_COUNT:-'N/A'}"
echo "   Test containers remaining: ${TEST_CONTAINERS:-'N/A'}"