#!/bin/bash

# Claude Batch Server Test Runner
# CRITICAL: SERIAL EXECUTION ENFORCED FOR ALL TESTS USING REAL RESOURCES
# 
# Unit tests: Run as suites (fast, no interdependence)
# Integration tests: Run as suites with SERIAL execution and resource cleanup
# E2E tests: Run individually with STRICT SERIAL execution and mandatory delays
#
# ALL tests that use real resources (network, ports, files) are run serially
# to prevent resource conflicts, port collisions, and test interference

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
BOLD='\033[1m'
NC='\033[0m' # No Color

# Configuration
PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" &> /dev/null && pwd)"
TEST_PROJECT="tests/ClaudeServerCLI.IntegrationTests/ClaudeServerCLI.IntegrationTests.csproj"
CORE_TEST_PROJECT="tests/ClaudeBatchServer.Core.Tests/ClaudeBatchServer.Core.Tests.csproj"
API_TEST_PROJECT="tests/ClaudeBatchServer.Api.Tests/ClaudeBatchServer.Api.Tests.csproj"
E2E_TIMEOUT=600  # 10 minutes per E2E test
INTEGRATION_TIMEOUT=180  # 3 minutes per integration test suite
UNIT_TIMEOUT=180  # 1 minute per unit test suite

# Global counters
TOTAL_TESTS=0
PASSED_TESTS=0
FAILED_TESTS=0
SKIPPED_TESTS=0

# Test results storage
FAILED_TEST_LIST=()
PASSED_TEST_LIST=()

log() {
    echo -e "${BLUE}[$(date +'%H:%M:%S')]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[$(date +'%H:%M:%S')] ✅ $1${NC}"
}

log_error() {
    echo -e "${RED}[$(date +'%H:%M:%S')] ❌ $1${NC}"
}

log_warning() {
    echo -e "${YELLOW}[$(date +'%H:%M:%S')] ⚠️  $1${NC}"
}

log_header() {
    echo
    echo -e "${BOLD}${BLUE}================================================${NC}"
    echo -e "${BOLD}${BLUE} $1${NC}"
    echo -e "${BOLD}${BLUE}================================================${NC}"
    echo
}

print_progress() {
    local current=$1
    local total=$2
    local test_name=$3
    local percentage=$((current * 100 / total))
    printf "\r${BLUE}Progress: [%3d%%] %d/%d${NC} - %s" "$percentage" "$current" "$total" "$test_name"
}

# Function to run a single test with timeout and result capture
run_single_test() {
    local project=$1
    local filter=$2
    local test_name=$3
    local timeout_seconds=$4
    
    log "Running: $test_name"
    
    # CRITICAL: Kill any existing test processes to ensure serial execution
    pkill -f "dotnet.*test" 2>/dev/null || true
    pkill -f "dotnet.*testhost" 2>/dev/null || true
    pkill -f "dotnet.*vstest" 2>/dev/null || true
    sleep 2  # Wait for processes to fully terminate
    
    # Create a temporary file for capturing output
    local temp_output=$(mktemp)
    local temp_result=$(mktemp)
    
    # Run the test with timeout - SERIAL EXECUTION ENFORCED
    if timeout "${timeout_seconds}s" dotnet test "$project" --filter "$filter" --verbosity minimal --no-build --logger "console;verbosity=minimal" > "$temp_output" 2>&1; then
        # Test command succeeded, check for actual test results
        if grep -q "Passed!" "$temp_output" || grep -q "Test Run Successful" "$temp_output" || grep -q "Failed:     0, Passed:     1" "$temp_output"; then
            PASSED_TESTS=$((PASSED_TESTS + 1))
            PASSED_TEST_LIST+=("$test_name")
            log_success "$test_name"
            echo "PASS" > "$temp_result"
        else
            FAILED_TESTS=$((FAILED_TESTS + 1))
            FAILED_TEST_LIST+=("$test_name")
            log_error "$test_name"
            echo "FAIL" > "$temp_result"
            # Store detailed failure info
            echo "=== FAILURE: $test_name ===" >> failed_tests_details.log
            cat "$temp_output" >> failed_tests_details.log
            echo "" >> failed_tests_details.log
        fi
    else
        # Test failed or timed out
        FAILED_TESTS=$((FAILED_TESTS + 1))
        FAILED_TEST_LIST+=("$test_name (TIMEOUT)")
        log_error "$test_name (TIMEOUT after ${timeout_seconds}s)"
        echo "TIMEOUT" > "$temp_result"
        # Store timeout info
        echo "=== TIMEOUT: $test_name ===" >> failed_tests_details.log
        echo "Test timed out after ${timeout_seconds} seconds" >> failed_tests_details.log
        cat "$temp_output" >> failed_tests_details.log
        echo "" >> failed_tests_details.log
    fi
    
    TOTAL_TESTS=$((TOTAL_TESTS + 1))
    
    # Cleanup
    rm -f "$temp_output" "$temp_result"
}

# Function to run a test suite normally
run_test_suite() {
    local project=$1
    local suite_name=$2
    local timeout_seconds=$3
    local filter=${4:-""}
    
    log "Running test suite: $suite_name"
    
    # CRITICAL: Kill any existing test processes to ensure serial execution
    pkill -f "dotnet.*test" 2>/dev/null || true
    pkill -f "dotnet.*testhost" 2>/dev/null || true
    pkill -f "dotnet.*vstest" 2>/dev/null || true
    sleep 2  # Wait for processes to fully terminate
    
    local temp_output=$(mktemp)
    
    local test_command="dotnet test \"$project\" --verbosity minimal --no-build"
    if [[ -n "$filter" ]]; then
        test_command="$test_command --filter \"$filter\""
    fi
    
    # Run with SERIAL EXECUTION ENFORCED
    if timeout "${timeout_seconds}s" bash -c "$test_command" > "$temp_output" 2>&1; then
        # Parse results from format: "Failed:     0, Passed:     2, Skipped:     0, Total:     2"
        local total=$(grep -o "Total:[[:space:]]*[0-9]*" "$temp_output" | grep -o "[0-9]*" || echo "0")
        local passed=$(grep -o "Passed:[[:space:]]*[0-9]*" "$temp_output" | grep -o "[0-9]*" || echo "0")
        local failed=$(grep -o "Failed:[[:space:]]*[0-9]*" "$temp_output" | grep -o "[0-9]*" || echo "0")
        local skipped=$(grep -o "Skipped:[[:space:]]*[0-9]*" "$temp_output" | grep -o "[0-9]*" || echo "0")
        
        TOTAL_TESTS=$((TOTAL_TESTS + total))
        PASSED_TESTS=$((PASSED_TESTS + passed))
        FAILED_TESTS=$((FAILED_TESTS + failed))
        SKIPPED_TESTS=$((SKIPPED_TESTS + skipped))
        
        if [[ $failed -eq 0 ]]; then
            log_success "$suite_name: $passed/$total tests passed"
            PASSED_TEST_LIST+=("$suite_name ($passed/$total)")
        else
            log_error "$suite_name: $failed tests failed, $passed tests passed"
            FAILED_TEST_LIST+=("$suite_name ($failed failed)")
            # Store failure details
            echo "=== SUITE FAILURE: $suite_name ===" >> failed_tests_details.log
            cat "$temp_output" >> failed_tests_details.log
            echo "" >> failed_tests_details.log
        fi
    else
        log_error "$suite_name: TIMEOUT after ${timeout_seconds}s"
        FAILED_TEST_LIST+=("$suite_name (TIMEOUT)")
        FAILED_TESTS=$((FAILED_TESTS + 1))
        TOTAL_TESTS=$((TOTAL_TESTS + 1))
    fi
    
    rm -f "$temp_output"
}

# Function to discover and run E2E tests individually
run_e2e_tests_individually() {
    log_header "RUNNING E2E TESTS INDIVIDUALLY"
    log "Discovering E2E tests..."
    
    # Build first to ensure we have latest
    log "Building test project..."
    dotnet build "$TEST_PROJECT" --configuration Debug --verbosity quiet
    
    # Clear any previous failure log
    > failed_tests_details.log
    
    # Discover all tests
    local temp_list=$(mktemp)
    dotnet test "$TEST_PROJECT" --list-tests --verbosity quiet > "$temp_list" 2>/dev/null || true
    
    # Extract E2E test method names from the actual test discovery output
    local e2e_tests=()
    while IFS= read -r line; do
        # Skip header lines and empty lines
        if [[ -z "$line" ]] || [[ "$line" == *"Test run for"* ]] || [[ "$line" == *"VSTest version"* ]] || [[ "$line" == *"The following Tests are available:"* ]]; then
            continue
        fi
        
        # Clean up the line and extract test name
        local test_name=$(echo "$line" | sed 's/^[[:space:]]*//' | sed 's/[[:space:]]*$//')
        
        # Only include tests that contain "E2E" or are authentication-related E2E tests
        if [[ "$test_name" == *"E2E"* ]] || [[ "$test_name" =~ ^(LoginCommand|LogoutCommand|WhoamiCommand|MultipleProfiles|AuthenticatedCommands|TokenExpiration) ]]; then
            if [[ -n "$test_name" ]]; then
                e2e_tests+=("$test_name")
            fi
        fi
    done < "$temp_list"
    
    rm -f "$temp_list"
    
    local total_e2e=${#e2e_tests[@]}
    
    if [[ $total_e2e -eq 0 ]]; then
        log_warning "No E2E tests discovered"
        return
    fi
    
    log "Found $total_e2e E2E tests to run individually"
    echo
    
    local current_test=0
    for test_name in "${e2e_tests[@]}"; do
        current_test=$((current_test + 1))
        print_progress $current_test $total_e2e "$test_name"
        
        # Create filter for this specific test
        local filter="DisplayName~\"${test_name}\""
        
        # Run the individual test
        run_single_test "$TEST_PROJECT" "$filter" "$test_name" $E2E_TIMEOUT
        
        # MANDATORY pause between E2E tests for complete resource cleanup
        log "Waiting for complete resource cleanup..."
        sleep 5
    done
    
    echo # New line after progress
}

# Function to run integration tests as suites but with better timeout handling
run_integration_tests() {
    log_header "RUNNING INTEGRATION TESTS"
    
    # Build first
    log "Building test project..."
    dotnet build "$TEST_PROJECT" --configuration Debug --verbosity quiet
    
    # Run different integration test categories
    local integration_suites=(
        "ApiClient Tests:DisplayName~ApiClient"
        "AuthService Tests:DisplayName~AuthService"
        "ConfigService Tests:DisplayName~ConfigService"
        "CLI Performance Tests:DisplayName~CLI"
        "FileUpload Performance Tests:DisplayName~FileUpload"
    )
    
    for suite_info in "${integration_suites[@]}"; do
        IFS=':' read -r suite_name filter <<< "$suite_info"
        run_test_suite "$TEST_PROJECT" "$suite_name" $INTEGRATION_TIMEOUT "$filter"
        log "Waiting for complete resource cleanup between integration suites..."
        sleep 5  # MANDATORY pause between integration suites for resource cleanup
    done
}

# Function to run unit tests normally (they're fast and don't have interdependence issues)
run_unit_tests() {
    log_header "RUNNING UNIT TESTS"
    
    # Check which unit test projects exist
    local unit_projects=()
    
    if [[ -f "$CORE_TEST_PROJECT" ]]; then
        unit_projects+=("$CORE_TEST_PROJECT:Core Unit Tests")
    fi
    
    if [[ -f "$API_TEST_PROJECT" ]]; then
        unit_projects+=("$API_TEST_PROJECT:API Unit Tests")
    fi
    
    if [[ ${#unit_projects[@]} -eq 0 ]]; then
        log_warning "No unit test projects found"
        return
    fi
    
    for project_info in "${unit_projects[@]}"; do
        IFS=':' read -r project_path suite_name <<< "$project_info"
        log "Building $suite_name..."
        dotnet build "$project_path" --configuration Debug --verbosity quiet
        run_test_suite "$project_path" "$suite_name" $UNIT_TIMEOUT
    done
}

# Function to print final summary
print_summary() {
    echo
    log_header "TEST EXECUTION SUMMARY"
    
    echo -e "${BOLD}Total Tests Run: $TOTAL_TESTS${NC}"
    echo -e "${GREEN}✅ Passed: $PASSED_TESTS${NC}"
    echo -e "${RED}❌ Failed: $FAILED_TESTS${NC}"
    if [[ $SKIPPED_TESTS -gt 0 ]]; then
        echo -e "${YELLOW}⏭️  Skipped: $SKIPPED_TESTS${NC}"
    fi
    
    local success_rate=0
    if [[ $TOTAL_TESTS -gt 0 ]]; then
        success_rate=$((PASSED_TESTS * 100 / TOTAL_TESTS))
    fi
    echo -e "${BOLD}Success Rate: ${success_rate}%${NC}"
    
    if [[ ${#PASSED_TEST_LIST[@]} -gt 0 ]]; then
        echo
        echo -e "${GREEN}${BOLD}✅ PASSED TESTS:${NC}"
        for test in "${PASSED_TEST_LIST[@]}"; do
            echo -e "${GREEN}  ✅ $test${NC}"
        done
    fi
    
    if [[ ${#FAILED_TEST_LIST[@]} -gt 0 ]]; then
        echo
        echo -e "${RED}${BOLD}❌ FAILED TESTS:${NC}"
        for test in "${FAILED_TEST_LIST[@]}"; do
            echo -e "${RED}  ❌ $test${NC}"
        done
        echo
        echo -e "${YELLOW}Detailed failure information saved to: failed_tests_details.log${NC}"
    fi
    
    echo
    if [[ $FAILED_TESTS -eq 0 ]]; then
        echo -e "${GREEN}${BOLD}🎉 ALL TESTS PASSED! 🎉${NC}"
        exit 0
    else
        echo -e "${RED}${BOLD}💥 $FAILED_TESTS TESTS FAILED 💥${NC}"
        exit 1
    fi
}

# Function to ensure clean start by killing any existing test processes
cleanup_existing_tests() {
    log "Ensuring clean test environment - killing any existing test processes..."
    pkill -f "dotnet.*test" 2>/dev/null || true
    pkill -f "dotnet.*testhost" 2>/dev/null || true
    pkill -f "dotnet.*vstest" 2>/dev/null || true
    pkill -f "timeout.*dotnet" 2>/dev/null || true
    sleep 3  # Wait for complete cleanup
    log "Test environment cleaned"
}

# Main execution function
main() {
    local test_type=${1:-"all"}
    
    cd "$PROJECT_ROOT"
    
    log_header "CLAUDE BATCH SERVER TEST RUNNER"
    log "Test type: $test_type"
    log "Project root: $PROJECT_ROOT"
    
    # CRITICAL: Clean environment first - SERIAL EXECUTION ENFORCED
    cleanup_existing_tests
    
    # Clear previous failure log
    > failed_tests_details.log
    
    case $test_type in
        "unit")
            run_unit_tests
            ;;
        "integration")
            run_integration_tests
            ;;
        "e2e")
            run_e2e_tests_individually
            ;;
        "all")
            run_unit_tests
            run_integration_tests
            run_e2e_tests_individually
            ;;
        *)
            log_error "Invalid test type: $test_type"
            echo "Usage: $0 [unit|integration|e2e|all]"
            exit 1
            ;;
    esac
    
    print_summary
}

# Execute main function with all arguments
main "$@"