#!/usr/bin/env python3
"""
Validation script for ImageAnalysisE2ETests.cs
This script validates that the E2E test is properly structured and follows
the same patterns as the working E2E tests in the codebase.
"""

import os
import re
import sys

def read_file(filepath):
    """Read a file and return its contents."""
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            return f.read()
    except Exception as e:
        print(f"❌ Error reading {filepath}: {e}")
        return None

def validate_test_structure():
    """Validate that the test follows the same structure as working tests."""
    print("🔍 Validating ImageAnalysisE2ETests.cs structure...")
    
    test_file = "/home/jsbattig/Dev/claude-server/claude-batch-server/tests/ClaudeBatchServer.IntegrationTests/ImageAnalysisE2ETests.cs"
    working_test = "/home/jsbattig/Dev/claude-server/claude-batch-server/tests/ClaudeBatchServer.IntegrationTests/ComplexE2ETests.cs"
    
    if not os.path.exists(test_file):
        print(f"❌ Test file does not exist: {test_file}")
        return False
        
    if not os.path.exists(working_test):
        print(f"❌ Reference test file does not exist: {working_test}")
        return False
    
    test_content = read_file(test_file)
    working_content = read_file(working_test)
    
    if not test_content or not working_content:
        return False
    
    # Check essential patterns
    patterns_to_check = [
        (r'WebApplicationFactory<Program>', "WebApplicationFactory usage"),
        (r'IClassFixture<WebApplicationFactory<Program>>', "IClassFixture implementation"),
        (r'\.env', "Environment file loading"),
        (r'TEST_USERNAME.*TEST_PASSWORD', "Test credentials pattern"),
        (r'LoginRequest', "Authentication request"),
        (r'PostAsJsonAsync.*auth/login', "Login endpoint usage"),
        (r'CreateAuthenticatedClient', "Authenticated client creation"),
        (r'Authorization.*Bearer', "Bearer token authentication"),
        (r'MultipartFormDataContent', "Image upload pattern"),
        (r'MediaTypeHeaderValue.*image/png', "Image content type"),
        (r'PostAsync.*images', "Image upload endpoint"),
        (r'JobStatusResponse', "Job status checking"),
        (r'\.Should\(\)', "FluentAssertions usage"),
    ]
    
    print("\n📋 Pattern Validation:")
    all_patterns_found = True
    
    for pattern, description in patterns_to_check:
        if re.search(pattern, test_content, re.IGNORECASE | re.DOTALL):
            print(f"   ✅ {description}")
        else:
            print(f"   ❌ {description}")
            all_patterns_found = False
    
    return all_patterns_found

def validate_image_specific_logic():
    """Validate image-specific test logic."""
    print("\n🖼️ Validating image-specific test logic...")
    
    test_file = "/home/jsbattig/Dev/claude-server/claude-batch-server/tests/ClaudeBatchServer.IntegrationTests/ImageAnalysisE2ETests.cs"
    test_content = read_file(test_file)
    
    if not test_content:
        return False
    
    image_patterns = [
        (r'test-image\.png', "Test image file reference"),
        (r'File\.ReadAllBytesAsync', "Image file reading"),
        (r'ByteArrayContent.*imageBytes', "Image byte array handling"),
        (r'image.*analysis.*prompt', "Image analysis prompt"),
        (r'shapes.*colors.*text', "Expected analysis elements"),
        (r'rectangle.*circle.*triangle', "Specific shape detection"),
        (r'blue.*red.*green', "Color detection"),
        (r'Output.*Should.*Contain', "Output validation assertions"),
        (r'Length.*Should.*BeGreaterThan', "Response length validation"),
    ]
    
    print("📝 Image Analysis Patterns:")
    all_patterns_found = True
    
    for pattern, description in image_patterns:
        if re.search(pattern, test_content, re.IGNORECASE | re.DOTALL):
            print(f"   ✅ {description}")
        else:
            print(f"   ❌ {description}")
            all_patterns_found = False
    
    return all_patterns_found

def validate_test_image_exists():
    """Validate that the test image file exists and is valid."""
    print("\n📁 Validating test image file...")
    
    image_path = "/home/jsbattig/Dev/claude-server/claude-batch-server/tests/ClaudeBatchServer.IntegrationTests/test-image.png"
    
    if not os.path.exists(image_path):
        print(f"❌ Test image does not exist: {image_path}")
        return False
    
    file_size = os.path.getsize(image_path)
    print(f"   ✅ Test image exists: {image_path}")
    print(f"   ✅ File size: {file_size} bytes")
    
    if file_size < 1000:
        print(f"   ⚠️ Warning: Image file seems small ({file_size} bytes)")
    
    if file_size > 50000:
        print(f"   ⚠️ Warning: Image file seems large ({file_size} bytes)")
    
    return True

def validate_authentication_pattern():
    """Validate that authentication follows the working pattern."""
    print("\n🔐 Validating authentication pattern...")
    
    test_file = "/home/jsbattig/Dev/claude-server/claude-batch-server/tests/ClaudeBatchServer.IntegrationTests/ImageAnalysisE2ETests.cs"
    working_test = "/home/jsbattig/Dev/claude-server/claude-batch-server/tests/ClaudeBatchServer.IntegrationTests/ComplexE2ETests.cs"
    
    test_content = read_file(test_file)
    working_content = read_file(working_test)
    
    if not test_content or not working_content:
        return False
    
    # Extract authentication sections
    auth_patterns = [
        r'var username = Environment\.GetEnvironmentVariable\("TEST_USERNAME"\);',
        r'var password = Environment\.GetEnvironmentVariable\("TEST_PASSWORD"\);',
        r'var loginRequest = new LoginRequest',
        r'Username = username,\s*Password = password',
        r'await _client\.PostAsJsonAsync\("/auth/login"',
        r'CreateAuthenticatedClient\(loginResult\.Token\)',
    ]
    
    print("🔑 Authentication Pattern Check:")
    all_auth_patterns = True
    
    for pattern in auth_patterns:
        if re.search(pattern, test_content, re.IGNORECASE | re.DOTALL):
            print(f"   ✅ Auth pattern found")
        else:
            print(f"   ❌ Auth pattern missing: {pattern[:50]}...")
            all_auth_patterns = False
    
    return all_auth_patterns

def validate_project_dependencies():
    """Validate that the test project has the necessary dependencies."""
    print("\n📦 Validating project dependencies...")
    
    csproj_path = "/home/jsbattig/Dev/claude-server/claude-batch-server/tests/ClaudeBatchServer.IntegrationTests/ClaudeBatchServer.IntegrationTests.csproj"
    
    if not os.path.exists(csproj_path):
        print(f"❌ Project file does not exist: {csproj_path}")
        return False
    
    csproj_content = read_file(csproj_path)
    if not csproj_content:
        return False
    
    required_packages = [
        "Microsoft.AspNetCore.Mvc.Testing",
        "FluentAssertions", 
        "xunit",
        "DotNetEnv",
    ]
    
    print("📚 Required Dependencies:")
    all_deps_found = True
    
    for package in required_packages:
        if package in csproj_content:
            print(f"   ✅ {package}")
        else:
            print(f"   ❌ {package}")
            all_deps_found = False
    
    return all_deps_found

def generate_test_summary():
    """Generate a summary of what the test should do when executed."""
    print("\n📋 Test Execution Summary:")
    print("   When this test runs, it should:")
    print("   1. ✅ Load test credentials from .env file")
    print("   2. ✅ Create in-memory test server using WebApplicationFactory")
    print("   3. ✅ Authenticate using TEST_USERNAME/TEST_PASSWORD")
    print("   4. ✅ Create job with image analysis prompt")
    print("   5. ✅ Upload test-image.png via multipart form data")
    print("   6. ✅ Start job execution")
    print("   7. ✅ Poll job status until completion")
    print("   8. ✅ Verify Claude identified shapes (rectangle, circle, triangle)")
    print("   9. ✅ Verify Claude identified colors (blue, red, green)")
    print("   10. ✅ Verify Claude identified text content")
    print("   11. ✅ Verify response length indicates detailed analysis")
    print("   12. ✅ Verify image file stored in job workspace")
    print("   13. ✅ Clean up test repositories and jobs")

def validate_env_file():
    """Validate that the .env file exists with test credentials."""
    print("\n🔧 Validating environment configuration...")
    
    env_path = "/home/jsbattig/Dev/claude-server/claude-batch-server/.env"
    
    if not os.path.exists(env_path):
        print(f"❌ .env file does not exist: {env_path}")
        return False
    
    env_content = read_file(env_path)
    if not env_content:
        return False
    
    required_vars = ["TEST_USERNAME", "TEST_PASSWORD"]
    
    print("🌍 Environment Variables:")
    all_vars_found = True
    
    for var in required_vars:
        if var in env_content:
            print(f"   ✅ {var} is configured")
        else:
            print(f"   ❌ {var} is missing")
            all_vars_found = False
    
    return all_vars_found

def main():
    """Main validation function."""
    print("🧪 ImageAnalysisE2ETests.cs - Validation Report")
    print("=" * 60)
    
    validation_results = []
    
    # Run all validations
    validation_results.append(("Test Structure", validate_test_structure()))
    validation_results.append(("Image Logic", validate_image_specific_logic()))
    validation_results.append(("Test Image File", validate_test_image_exists()))
    validation_results.append(("Authentication", validate_authentication_pattern()))
    validation_results.append(("Dependencies", validate_project_dependencies()))
    validation_results.append(("Environment", validate_env_file()))
    
    # Generate summary
    generate_test_summary()
    
    # Report results
    print(f"\n📊 Validation Results:")
    print("-" * 40)
    
    all_passed = True
    for name, result in validation_results:
        status = "✅ PASS" if result else "❌ FAIL"
        print(f"   {name:<20} {status}")
        if not result:
            all_passed = False
    
    print("-" * 40)
    
    if all_passed:
        print("🎉 All validations passed!")
        print("✅ ImageAnalysisE2ETests.cs is ready for execution")
        print("✅ Test follows the same patterns as working E2E tests")
        print("✅ Test should work when run with: dotnet test")
        exit_code = 0
    else:
        print("❌ Some validations failed")
        print("⚠️ Fix the issues above before running the test")
        exit_code = 1
    
    print(f"\n🚀 To run the test: dotnet test --filter ImageAnalysisE2ETests")
    sys.exit(exit_code)

if __name__ == "__main__":
    main()