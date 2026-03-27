#!/usr/bin/env bash
# ============================================================
# push-to-ecr.sh  — Build the MTG Deck Forge image and push
#                   it to Amazon ECR, then deploy to ECS.
#
# Prerequisites:
#   - AWS CLI installed and configured (aws configure)
#   - Docker installed and running
#   - The ECR repository already created (see SETUP below)
#
# Usage:
#   chmod +x deploy/push-to-ecr.sh
#   AWS_REGION=us-east-1 AWS_ACCOUNT_ID=123456789012 ./deploy/push-to-ecr.sh
# ============================================================

set -euo pipefail

# ── Config ────────────────────────────────────────────────
AWS_REGION="${AWS_REGION:?Set AWS_REGION, e.g. us-east-1}"
AWS_ACCOUNT_ID="${AWS_ACCOUNT_ID:?Set AWS_ACCOUNT_ID (12-digit number)}"
ECR_REPO="mtg-deckforge"
IMAGE_TAG="${IMAGE_TAG:-latest}"
ECS_CLUSTER="${ECS_CLUSTER:-mtg-deckforge-cluster}"
ECS_SERVICE="${ECS_SERVICE:-mtg-deckforge-service}"

ECR_URI="${AWS_ACCOUNT_ID}.dkr.ecr.${AWS_REGION}.amazonaws.com/${ECR_REPO}"

echo "==> Logging in to ECR..."
aws ecr get-login-password --region "$AWS_REGION" \
  | docker login --username AWS --password-stdin "${AWS_ACCOUNT_ID}.dkr.ecr.${AWS_REGION}.amazonaws.com"

echo "==> Building Docker image..."
# Run from repo root (one level up from deploy/)
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "${SCRIPT_DIR}/.."
docker build -t "${ECR_REPO}:${IMAGE_TAG}" .

echo "==> Tagging image..."
docker tag "${ECR_REPO}:${IMAGE_TAG}" "${ECR_URI}:${IMAGE_TAG}"

echo "==> Pushing to ECR: ${ECR_URI}:${IMAGE_TAG}"
docker push "${ECR_URI}:${IMAGE_TAG}"

echo "==> Registering new task definition..."
# Replace placeholders in template with actual values
TASK_DEF=$(sed \
  -e "s|ACCOUNT_ID|${AWS_ACCOUNT_ID}|g" \
  -e "s|REGION|${AWS_REGION}|g" \
  "${SCRIPT_DIR}/task-definition.json")

NEW_TASK_DEF_ARN=$(echo "$TASK_DEF" \
  | aws ecs register-task-definition \
      --region "$AWS_REGION" \
      --cli-input-json file:///dev/stdin \
      --query "taskDefinition.taskDefinitionArn" \
      --output text)
echo "    Registered: ${NEW_TASK_DEF_ARN}"

echo "==> Updating ECS service to use new task definition..."
aws ecs update-service \
  --region "$AWS_REGION" \
  --cluster "$ECS_CLUSTER" \
  --service "$ECS_SERVICE" \
  --task-definition "$NEW_TASK_DEF_ARN" \
  --force-new-deployment \
  --query "service.serviceName" \
  --output text

echo ""
echo "✓ Done! ECS will pull the new image and replace running tasks."
echo "  Monitor progress:"
echo "  aws ecs describe-services --cluster $ECS_CLUSTER --services $ECS_SERVICE --region $AWS_REGION"
