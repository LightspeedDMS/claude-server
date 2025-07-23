#!/usr/bin/env python3

import os
import requests
import json

# Test authentication flow to debug JWT issues
api_base = "http://localhost:5000"

def test_auth():
    # Step 1: Login and get token
    login_data = {
        "username": "jsbattig",
        "password": "test123"
    }
    
    print("1. Testing login...")
    login_response = requests.post(f"{api_base}/auth/login", json=login_data)
    print(f"Login Status: {login_response.status_code}")
    print(f"Login Response: {login_response.text}")
    
    if login_response.status_code != 200:
        print("LOGIN FAILED!")
        return
    
    login_result = login_response.json()
    token = login_result.get('token')
    
    if not token:
        print("NO TOKEN RECEIVED!")
        return
    
    print(f"Token received: {token[:50]}...")
    
    # Step 2: Test authenticated request
    headers = {"Authorization": f"Bearer {token}"}
    
    print("2. Testing authenticated request...")
    repo_response = requests.get(f"{api_base}/repositories", headers=headers)
    print(f"Repositories Status: {repo_response.status_code}")
    print(f"Repositories Response: {repo_response.text}")
    
    if repo_response.status_code == 200:
        print("✅ Authentication working!")
    else:
        print("❌ Authentication failed!")

if __name__ == "__main__":
    # Set environment variables
    os.environ["TEST_USERNAME"] = "jsbattig"
    os.environ["TEST_PASSWORD"] = "test123"
    
    try:
        test_auth()
    except Exception as e:
        print(f"Error: {e}")