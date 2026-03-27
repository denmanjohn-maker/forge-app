# AWS ECS Deployment Guide — MTG Deck Forge

This guide walks you through deploying MTG Deck Forge to AWS ECS Fargate from scratch.

---

## Architecture

```
Internet → Application Load Balancer (port 80/443)
              → ECS Fargate Service (port 5000)
                    → MongoDB Atlas (external, managed)
```

The app container talks to **MongoDB Atlas** (free tier available). This is simpler than running MongoDB on ECS because Fargate tasks are ephemeral — a managed database keeps your data safe.

---

## One-Time Setup

### 1. Install prerequisites

```bash
# AWS CLI
brew install awscli           # macOS
# OR: https://docs.aws.amazon.com/cli/latest/userguide/install-cliv2.html

# Configure with your AWS credentials
aws configure
# Prompts: AWS Access Key ID, Secret Access Key, Region (e.g. us-east-1), output format (json)
```

### 2. Set up MongoDB Atlas (free)

1. Go to https://cloud.mongodb.com and create a free account
2. Create a free M0 cluster (choose the same AWS region as your ECS service)
3. Under **Database Access**, create a user with read/write permissions
4. Under **Network Access**, add `0.0.0.0/0` (or scope to your ECS subnet later)
5. Click **Connect → Drivers** and copy your connection string:
   ```
   mongodb+srv://username:password@cluster0.xxxxx.mongodb.net/mtgdeckforge?retryWrites=true&w=majority
   ```

### 3. Store secrets in AWS Secrets Manager

```bash
REGION=us-east-1   # change to your region

# Your Anthropic API key
aws secretsmanager create-secret \
  --region $REGION \
  --name mtg-deckforge/anthropic-api-key \
  --secret-string "sk-ant-api03-YOUR-KEY-HERE"

# Your MongoDB Atlas connection string
aws secretsmanager create-secret \
  --region $REGION \
  --name mtg-deckforge/mongodb-connection-string \
  --secret-string "mongodb+srv://user:pass@cluster0.xxxxx.mongodb.net/mtgdeckforge?retryWrites=true&w=majority"
```

### 4. Create the ECR repository

```bash
aws ecr create-repository \
  --region $REGION \
  --repository-name mtg-deckforge
```

### 5. Create the ECS task execution role

This IAM role lets ECS pull secrets and container images on your behalf.

```bash
# Create the role
aws iam create-role \
  --role-name ecsTaskExecutionRole \
  --assume-role-policy-document '{
    "Version":"2012-10-17",
    "Statement":[{
      "Effect":"Allow",
      "Principal":{"Service":"ecs-tasks.amazonaws.com"},
      "Action":"sts:AssumeRole"
    }]
  }'

# Attach the managed policy for ECR + CloudWatch Logs
aws iam attach-role-policy \
  --role-name ecsTaskExecutionRole \
  --policy-arn arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy

# Allow reading from Secrets Manager
aws iam put-role-policy \
  --role-name ecsTaskExecutionRole \
  --policy-name SecretsManagerRead \
  --policy-document '{
    "Version":"2012-10-17",
    "Statement":[{
      "Effect":"Allow",
      "Action":["secretsmanager:GetSecretValue"],
      "Resource":"arn:aws:secretsmanager:REGION:ACCOUNT_ID:secret:mtg-deckforge/*"
    }]
  }'
```
> Replace `REGION` and `ACCOUNT_ID` in the resource ARN above.

### 6. Create CloudWatch log group

```bash
aws logs create-log-group \
  --region $REGION \
  --log-group-name /ecs/mtg-deckforge
```

### 7. Create the ECS cluster

```bash
aws ecs create-cluster \
  --region $REGION \
  --cluster-name mtg-deckforge-cluster
```

### 8. Create a VPC security group for ECS

```bash
# Get your default VPC id
VPC_ID=$(aws ec2 describe-vpcs --region $REGION \
  --filters "Name=isDefault,Values=true" \
  --query "Vpcs[0].VpcId" --output text)

# Create security group
SG_ID=$(aws ec2 create-security-group \
  --region $REGION \
  --group-name mtg-deckforge-sg \
  --description "MTG Deck Forge ECS tasks" \
  --vpc-id $VPC_ID \
  --query "GroupId" --output text)

# Allow inbound on port 5000 from anywhere (or scope to ALB SG later)
aws ec2 authorize-security-group-ingress \
  --region $REGION \
  --group-id $SG_ID \
  --protocol tcp --port 5000 --cidr 0.0.0.0/0

echo "Security Group: $SG_ID"
```

### 9. Create the ECS service

First push your image (step below), register the task definition, then:

```bash
# Get default subnet IDs
SUBNETS=$(aws ec2 describe-subnets --region $REGION \
  --filters "Name=defaultForAz,Values=true" \
  --query "Subnets[*].SubnetId" --output text | tr '\t' ',')

aws ecs create-service \
  --region $REGION \
  --cluster mtg-deckforge-cluster \
  --service-name mtg-deckforge-service \
  --task-definition mtg-deckforge \
  --desired-count 1 \
  --launch-type FARGATE \
  --network-configuration "awsvpcConfiguration={subnets=[$SUBNETS],securityGroups=[$SG_ID],assignPublicIp=ENABLED}"
```

> With `assignPublicIp=ENABLED` your task gets a public IP directly — no load balancer needed to get started. You can add an ALB later.

---

## Deploying / Updating

Every time you push a code change:

```bash
export AWS_REGION=us-east-1
export AWS_ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)

./deploy/push-to-ecr.sh
```

This builds the Docker image, pushes to ECR, registers a new task definition revision, and forces ECS to roll out the new version with zero downtime.

---

## Finding your app's public IP

```bash
# Get the task ARN
TASK_ARN=$(aws ecs list-tasks \
  --region $REGION \
  --cluster mtg-deckforge-cluster \
  --service-name mtg-deckforge-service \
  --query "taskArns[0]" --output text)

# Get the ENI attached to the task
ENI=$(aws ecs describe-tasks \
  --region $REGION \
  --cluster mtg-deckforge-cluster \
  --tasks $TASK_ARN \
  --query "tasks[0].attachments[0].details[?name=='networkInterfaceId'].value" \
  --output text)

# Get the public IP
aws ec2 describe-network-interfaces \
  --region $REGION \
  --network-interface-ids $ENI \
  --query "NetworkInterfaces[0].Association.PublicIp" \
  --output text
```

Then open `http://PUBLIC_IP:5000` in your browser.

---

## Troubleshooting

| Problem | Fix |
|---|---|
| Task keeps stopping | Check CloudWatch Logs at `/ecs/mtg-deckforge` |
| `CannotPullContainerError` | Make sure the task execution role can pull from ECR |
| `ResourceNotFoundException` for secrets | Verify secret ARN in task-definition.json matches exactly |
| MongoDB connection refused | Check Atlas Network Access — whitelist `0.0.0.0/0` |
| Port not reachable | Check security group allows inbound port 5000 |

View logs:
```bash
aws logs tail /ecs/mtg-deckforge --region $REGION --follow
```
