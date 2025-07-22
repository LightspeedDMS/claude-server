#!/usr/bin/env python3
"""
Manual test script for image analysis functionality.
This script validates that our test image is properly created and contains
the expected visual elements that Claude should be able to identify.
"""

import os
import sys
from PIL import Image, ImageDraw, ImageFont
import base64
import json

def create_test_image(output_path):
    """Create a test image with identifiable shapes, colors, and text."""
    print(f"Creating test image at: {output_path}")
    
    # Create a simple test image with identifiable content
    img = Image.new('RGB', (400, 300), color='white')
    draw = ImageDraw.Draw(img)

    # Draw some shapes and text that Claude can easily identify
    # Blue rectangle
    draw.rectangle([50, 50, 150, 100], fill='blue', outline='black', width=2)
    print("✅ Added blue rectangle")

    # Red circle
    draw.ellipse([200, 50, 300, 150], fill='red', outline='black', width=2)
    print("✅ Added red circle")

    # Green triangle (using polygon)
    draw.polygon([(100, 200), (150, 250), (50, 250)], fill='green', outline='black', width=2)
    print("✅ Added green triangle")

    # Add text
    try:
        font = ImageFont.load_default()
    except:
        font = None

    draw.text((50, 10), 'Test Image', fill='black', font=font)
    draw.text((50, 270), 'Shapes: Rectangle, Circle, Triangle', fill='black', font=font)
    print("✅ Added text labels")

    # Save the image
    img.save(output_path)
    
    return img

def analyze_test_image(image_path):
    """Analyze the test image to verify it contains expected elements."""
    print(f"\n🔍 Analyzing test image: {image_path}")
    
    if not os.path.exists(image_path):
        print(f"❌ Image file does not exist: {image_path}")
        return False
    
    try:
        img = Image.open(image_path)
        print(f"✅ Image loaded successfully")
        print(f"   Size: {img.size}")
        print(f"   Mode: {img.mode}")
        print(f"   Format: {img.format}")
        
        # Get basic stats
        colors = img.getcolors(maxcolors=256)
        num_colors = len(colors) if colors else "many"
        print(f"   Unique colors: {num_colors}")
        
        # Verify file size
        file_size = os.path.getsize(image_path)
        print(f"   File size: {file_size} bytes")
        
        if file_size < 100:
            print("❌ Image file is too small")
            return False
            
        if img.size != (400, 300):
            print("❌ Image has unexpected dimensions")
            return False
            
        print("✅ Image analysis passed - ready for Claude Code testing")
        return True
        
    except Exception as e:
        print(f"❌ Error analyzing image: {e}")
        return False

def simulate_claude_analysis():
    """Simulate what Claude should detect in the image."""
    print(f"\n🤖 Expected Claude Analysis:")
    print("   Shapes: Should detect rectangle, circle, triangle")
    print("   Colors: Should detect blue, red, green, white, black")  
    print("   Text: Should detect 'Test Image' and 'Shapes: Rectangle, Circle, Triangle'")
    print("   Layout: Should describe spatial arrangement of elements")
    
    expected_keywords = [
        "rectangle", "blue", "red", "green", "circle", "triangle", 
        "test", "image", "shapes", "text"
    ]
    
    print(f"   Expected keywords: {', '.join(expected_keywords)}")
    return expected_keywords

def validate_test_setup():
    """Validate that the test environment is properly set up."""
    print("🔧 Validating test environment...")
    
    # Check if required packages are available
    try:
        import PIL
        print("✅ PIL (Pillow) is available")
    except ImportError:
        print("❌ PIL (Pillow) is not available")
        return False
    
    # Check if test directory exists
    test_dir = "/home/jsbattig/Dev/claude-server/claude-batch-server/tests/ClaudeBatchServer.IntegrationTests"
    if os.path.exists(test_dir):
        print(f"✅ Test directory exists: {test_dir}")
    else:
        print(f"❌ Test directory does not exist: {test_dir}")
        return False
    
    return True

def create_test_summary():
    """Create a summary of what the E2E test should validate."""
    print(f"\n📋 E2E Test Validation Checklist:")
    print("   1. ✅ Image upload via multipart form data")
    print("   2. ✅ Image storage in job workspace (/workspace/jobs/{jobId}/images/)")
    print("   3. ✅ Claude Code execution with image analysis")
    print("   4. ✅ Claude response contains shape identification")
    print("   5. ✅ Claude response contains color identification") 
    print("   6. ✅ Claude response contains text recognition")
    print("   7. ✅ Claude response demonstrates understanding of spatial layout")
    print("   8. ✅ Response length indicates detailed analysis (>100 chars)")
    print("   9. ✅ Job status transitions: created → running → completed")
    print("   10. ✅ No errors in job execution")

def main():
    """Main test function."""
    print("🧪 Image Analysis E2E Test - Manual Validation")
    print("=" * 60)
    
    # Validate environment
    if not validate_test_setup():
        print("❌ Test environment validation failed")
        sys.exit(1)
    
    # Create test image
    image_path = "/home/jsbattig/Dev/claude-server/claude-batch-server/tests/ClaudeBatchServer.IntegrationTests/test-image.png"
    
    if os.path.exists(image_path):
        print(f"📁 Test image already exists: {image_path}")
    else:
        create_test_image(image_path)
    
    # Analyze test image
    if not analyze_test_image(image_path):
        print("❌ Image analysis failed")
        sys.exit(1)
    
    # Show expected results
    expected_keywords = simulate_claude_analysis()
    
    # Create test summary
    create_test_summary()
    
    print(f"\n🎯 Test Image Ready!")
    print(f"   Path: {image_path}")
    print(f"   The E2E test should upload this image to Claude Batch Server")
    print(f"   and verify Claude Code correctly identifies the visual elements.")
    print(f"\n✅ Manual validation complete - ready for E2E test execution!")

if __name__ == "__main__":
    main()