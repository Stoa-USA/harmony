#!/bin/bash

# Deploy Harmony.Lambda to AWS Lambda using .NET 10
# This script creates all necessary AWS resources and deploys the Lambda function
# Requirements: AWS CLI configured with appropriate credentials

set -e

REGION="us-east-2"
FUNCTION_NAME="harmony-pairing-api"
ROLE_NAME="harmony-lambda-role"
SCRIPT_DIR="$(cd "$(dirname "$0")" >/dev/null && pwd)"
PROJECT_DIR="$SCRIPT_DIR/../Harmony.Lambda"

echo "======================================"
echo "Deploying Harmony Lambda Function"
echo "Region: $REGION"
echo "Function: $FUNCTION_NAME"
echo "Runtime: dotnet10"
echo "======================================"
echo ""

# Step 1: Create IAM role for Lambda if it doesn't exist
echo "Step 1: Checking IAM role..."
if aws iam get-role --role-name "$ROLE_NAME" --region "$REGION" > /dev/null 2>&1; then
    echo "IAM role '$ROLE_NAME' already exists"
    ROLE_ARN=$(aws iam get-role --role-name "$ROLE_NAME" --query 'Role.Arn' --output text)
else
    echo "Creating IAM role '$ROLE_NAME'..."

    TRUST_POLICY='{
      "Version": "2012-10-17",
      "Statement": [
        {
          "Effect": "Allow",
          "Principal": {
            "Service": "lambda.amazonaws.com"
          },
          "Action": "sts:AssumeRole"
        }
      ]
    }'

    ROLE_ARN=$(aws iam create-role \
        --role-name "$ROLE_NAME" \
        --assume-role-policy-document "$TRUST_POLICY" \
        --query 'Role.Arn' \
        --output text)

    echo "Attaching basic Lambda execution policy..."
    aws iam attach-role-policy \
        --role-name "$ROLE_NAME" \
        --policy-arn "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"

    echo "Waiting 10 seconds for IAM role to propagate..."
    sleep 10
fi

echo "Role ARN: $ROLE_ARN"
echo ""

# Step 2: Build and package the Lambda function
echo "Step 2: Building Lambda function..."
cd "$PROJECT_DIR"
dotnet publish -c Release -r linux-x64 --self-contained false -o publish

echo "Creating deployment package..."
cd publish
zip -r ../lambda-package.zip . > /dev/null
cd ..
PACKAGE_PATH="$PROJECT_DIR/lambda-package.zip"
echo "Package created at: $PACKAGE_PATH"
echo ""

# Step 3: Create or update Lambda function
echo "Step 3: Deploying Lambda function..."
if aws lambda get-function --function-name "$FUNCTION_NAME" --region "$REGION" > /dev/null 2>&1; then
    echo "Function exists, updating code..."
    aws lambda update-function-code \
        --function-name "$FUNCTION_NAME" \
        --zip-file "fileb://$PACKAGE_PATH" \
        --region "$REGION" > /dev/null

    echo "Waiting for code update to complete..."
    aws lambda wait function-updated --function-name "$FUNCTION_NAME" --region "$REGION"

    echo "Updating function configuration..."
    aws lambda update-function-configuration \
        --function-name "$FUNCTION_NAME" \
        --runtime dotnet10 \
        --handler Harmony.Lambda \
        --memory-size 3008 \
        --timeout 40 \
        --region "$REGION" > /dev/null
else
    echo "Creating new Lambda function..."
    aws lambda create-function \
        --function-name "$FUNCTION_NAME" \
        --runtime dotnet10 \
        --role "$ROLE_ARN" \
        --handler Harmony.Lambda \
        --zip-file "fileb://$PACKAGE_PATH" \
        --memory-size 3008 \
        --timeout 40 \
        --region "$REGION" > /dev/null

    echo "Waiting for function to be active..."
    aws lambda wait function-active --function-name "$FUNCTION_NAME" --region "$REGION"
fi

FUNCTION_ARN=$(aws lambda get-function --function-name "$FUNCTION_NAME" --region "$REGION" --query 'Configuration.FunctionArn' --output text)
echo "Function ARN: $FUNCTION_ARN"
echo ""

# Step 4: Create Lambda Function URL
echo "Step 4: Setting up Function URL..."
if aws lambda get-function-url-config --function-name "$FUNCTION_NAME" --region "$REGION" > /dev/null 2>&1; then
    echo "Function URL already exists"
    FUNCTION_URL=$(aws lambda get-function-url-config --function-name "$FUNCTION_NAME" --region "$REGION" --query 'FunctionUrl' --output text)
else
    echo "Creating Function URL..."
    FUNCTION_URL=$(aws lambda create-function-url-config \
        --function-name "$FUNCTION_NAME" \
        --auth-type NONE \
        --region "$REGION" \
        --query 'FunctionUrl' \
        --output text)

    aws lambda add-permission \
        --function-name "$FUNCTION_NAME" \
        --statement-id FunctionURLAllowPublicAccess \
        --action lambda:InvokeFunctionUrl \
        --principal "*" \
        --function-url-auth-type NONE \
        --region "$REGION" > /dev/null 2>&1 || echo "Permission already exists"
fi

echo ""
echo "======================================"
echo "Deployment Complete!"
echo "======================================"
echo ""
echo "Function URL: $FUNCTION_URL"
echo ""
echo "Test the API with:"
echo "curl -X POST \"${FUNCTION_URL}api/pairing/generate\" \\"
echo "  -H \"Content-Type: application/json\" \\"
echo "  -d @example-request.json"
echo ""
echo "Estimated cost: ~$0.20/month for Lambda (with 1M free tier)"
echo "Cold start: ~2-3 seconds, Warm: ~100-200ms"
echo ""
