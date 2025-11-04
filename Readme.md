# 🔎 Tender Tool Search API Lambda

[![AWS Lambda](https://img.shields.io/badge/AWS-Lambda-orange.svg)](https://aws.amazon.com/lambda/)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![Amazon API Gateway](https://img.shields.io/badge/AWS-API%20Gateway-yellow.svg)](https://aws.amazon.com/api-gateway/)
[![Amazon OpenSearch](https://img.shields.io/badge/AWS-OpenSearch-blueviolet.svg)](https://aws.amazon.com/opensearch-service/)

This project is the permanent, user-facing search API for the Tender Tool. It provides the single, public-facing `POST /api/search` endpoint that the front-end application will call.

This function is a high-performance, read-only service. It connects directly to our private OpenSearch cluster (which is populated by the `TenderToolSyncLambda`) to execute powerful, paginated full-text searches.

## 📚 Table of Contents

- [✨ Key Features](#-key-features)
- [🧭 Architecture & Data Flow](#-architecture--data-flow)
- [🚀 API Specification (for Front-End)](#-api-specification-for-front-end)
- [🧩 Project Structure](#-project-structure)
- [⚙️ Configuration](#️-configuration)
- [🔒 IAM & Security (Critical)](#-iam--security-critical)
- [📦 Tech Stack](#-tech-stack)
- [🚀 Getting Started](#-getting-started)
- [📦 Deployment Guide](#-deployment-guide)
- [🧰 Troubleshooting & Gotchas](#-troubleshooting--gotchas)

## ✨ Key Features

- **⚡ Fast, Weighted Search**: Utilizes OpenSearch `multi_match` queries to provide relevant results. Search terms are weighted, prioritizing matches in `Title` (3x boost) and `Tags` (2x boost) over other fields.

- **⚙️ Full Server-Side Pagination**: Natively handles pagination. The API accepts `page` and `pageSize` parameters and returns a complete pagination object, including `totalPages` and `totalResults`, making front-end implementation simple.

- **🔗 VPC Native**: Runs securely inside the project's private VPC to access the `tender-tool-search` OpenSearch domain endpoint.

- **🛡️ Secure & Scoped**: Deployed with a "least-privilege" IAM role that **only** allows read-only search operations (`es:ESHttpGet`). It has no permission to write, delete, or modify data.

- **🔄 Resilient**: Gracefully handles empty search queries (by returning a `match_all`) and provides detailed, structured JSON error logs to CloudWatch.

- **🎯 Zero Dependencies**: This Lambda does *not* connect to the RDS database; it is a pure, lightweight search broker.

## 🧭 Architecture & Data Flow

This Lambda is the final piece of the search puzzle. It **only reads** data. The data itself is **written** by the `TenderToolSyncLambda`.

```
┌─────────────────────────────────────────────────────────────────┐
│                    ONE-TIME SYNC (ALREADY COMPLETE)             │
└─────────────────────────────────────────────────────────────────┘

[TenderToolSyncLambda] ────writes all data───► [OpenSearch Cluster]
                                                        │
                                                        │ reads from
┌─────────────────────────────────────────────────────────────────┐ │
│                       LIVE USER QUERIES                         │ │
└─────────────────────────────────────────────────────────────────┘ │
                                                                    │
[Front-End] ──(1) POST /api/search──► [API Gateway] ──(2)──► [TenderToolSearchLambda]
     │                                                              │
     │                                     (3) Query OpenSearch  ◄──┘
     │                                     { "multi_match": ... }
     │                                                              
     │                                     (4) Search Results      
     │                               ┌─────────────────────────────┘
     │                               │                             
     └◄──(5) Returns 200 OK w/ JSON──┘                             
         { "page": 1, "totalPages": 54, ... }
```

## 🚀 API Specification (for Front-End)

This is the official contract for the front-end team.

### 1. API Health Check (Root URL)

You can perform a simple `GET` request to the root URL of the API to confirm that it is deployed and running.

- **Method:** `GET`
- **Endpoint URL:** `[Your-API-Gateway-URL]/Prod/`
- **Expected Response:** A plain text string: `Welcome to the Tender Tool Search Lambda`

### 2. Search Endpoint & Request

This is the main endpoint for all search operations.

- **Method:** `POST`
- **Endpoint URL:** `[Your-API-Gateway-URL]/Prod/api/search`
- **Body (Request):** A JSON object with a query and pagination fields.

**Example Request Body:**

```json
{
  "query": "electrical maintenance",
  "page": 1,
  "pageSize": 10
}
```

- **`query` (string):** The user's search term. If an empty string `""` is sent, it will `match_all` and return all tenders.
- **`page` (int):** The page number to retrieve. Defaults to `1`.
- **`pageSize` (int):** The number of results per page. Defaults to `10`.

### 3. Response Handling

#### ✅ Success (200 OK)

This is returned when the search is successful (even if no results are found).

**Body (Response):**
```json
{
    "page": 1,
    "pageSize": 10,
    "totalResults": 23,
    "totalPages": 3,
    "results": [
        {
            "tenderID": "a90704e8-418d-4d54-8ad7-88b45d8a7d53",
            "title": "Computer Programming, Consultancy And Related Activities",
            "status": "Open",
            "source": "eTenders",
            "description": "For The Provision Of Microsoft Training...",
            "aiSummary": "# Tender Summary...",
            "tags": [
                "IT Skills Development",
                "Microsoft Training",
                "IT & Software"
            ],
            "supportingDocs": [
                {
                    "name": "WCGHSC0323-2025 Advertised Bid.pdf",
                    "url": "https://www.etenders.gov.za/..."
                }
            ],
            "tenderNumber": null,
            "category": null,
            "email": null
        }
    ]
}
```

#### ❌ Error (500 Internal Server Error)

If the OpenSearch query fails, the API will return a 500 status.

**Body (Response):**
```json
{
  "message": "Search query failed: [Detailed error message from OpenSearch]"
}
```

## 🧩 Project Structure

```
TenderToolSearchLambda/
├── Controllers/
│   └── SearchController.cs      # The main API endpoint. Receives request, queries OpenSearch.
├── Models/
│   ├── SearchRequest.cs         # The request payload (query, page, pageSize)
│   ├── PaginatedSearchResponse.cs # The response payload (totalPages, results, etc.)
│   └── TenderSearchDocument.cs  # The "flattened" data model. MUST match the SyncLambda.
├── Properties/
│   └── launchSettings.json
├── appsettings.json             # Config for OpenSearch:Endpoint URL
├── LambdaEntryPoint.cs          #
├── LocalEntryPoint.cs           #
├── Startup.cs                   # Configures DI for JSON logging and OpenSearchClient
└── serverless.template          # CRITICAL: Configures VPC, Subnets, and IAM Role.
```

## ⚙️ Configuration

The function is configured via `appsettings.json`. The OpenSearch endpoint must be set.

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "OpenSearch": {
    "Endpoint": "https://vpc-tender-tool-search-m2hyjgolvayz42ki2zjhq3atly.us-east-1.es.amazonaws.com"
  }
}
```

## 🔒 IAM & Security (Critical)

This Lambda runs in the VPC and requires two layers of permissions to function.

### 1. IAM Policy (The "Front Door")

The `serverless.template` configures the Lambda's IAM execution role with these permissions:

- **`AWSLambdaVPCAccessExecutionRole`**: Allows the Lambda to connect to the VPC and write logs to CloudWatch.
- **Custom `es:ESHttpGet` Policy**: This is the "front door" key. It explicitly allows the Lambda to perform HTTP `GET` operations (which the OpenSearch client uses for `_search`) against our cluster.

```json
"Policies": [
  "AWSLambdaVPCAccessExecutionRole",
  {
    "Version": "2012-10-17",
    "Statement": [
      {
        "Effect": "Allow",
        "Action": [
          "es:ESHttpGet"
        ],
        "Resource": "arn:aws:es:us-east-1:211635102441:domain/tender-tool-search/*"
      }
    ]
  }
]
```

### 2. OpenSearch Internal Permissions (The "Internal Bouncer")

This is a **CRITICAL MANUAL STEP**. The IAM policy only gets the Lambda to the "front door." We must tell OpenSearch's internal security (Fine-Grained Access Control) to let this Lambda *in* and give it *read* permissions.

**This must be done once for the function to work.** (See Troubleshooting section for guide).

## 📦 Tech Stack

- **.NET 8** (LTS)
- **Compute**: AWS Lambda
- **API**: Amazon API Gateway
- **Search Engine**: Amazon OpenSearch Service
- **OpenSearch Client**: OpenSearch .NET Client
- **Networking**: AWS VPC, Private Subnets, Security Groups
- **Logging**: `Microsoft.Extensions.Logging.Console` (for structured JSON logging)

## 🚀 Getting Started

Follow these steps to set up the project for local development.

### Prerequisites

- .NET 8 SDK
- AWS CLI configured with appropriate credentials
- Visual Studio 2022 or VS Code with C# extensions
- Access to the OpenSearch cluster via bastion host

### Local Setup

1. **Clone the repository:**
   ```bash
   git clone <your-repository-url>
   cd TenderToolSearchLambda
   ```

2. **Restore Dependencies:**
   ```bash
   dotnet restore
   ```

3. **Configure Application Settings:**
   Update `appsettings.json` with your OpenSearch endpoint:
   ```json
   {
     "OpenSearch": {
       "Endpoint": "https://vpc-tender-tool-search-[your-domain-id].us-east-1.es.amazonaws.com"
     }
   }
   ```

4. **Run Locally:**
   ```bash
   dotnet run
   ```

## 📦 Deployment

This ASP.NET Core Lambda function can be deployed using three different methods. Choose the one that best fits your workflow and requirements.

### Prerequisites

Before deploying, ensure you have:

- .NET 8 SDK installed
- AWS CLI configured with appropriate credentials
- OpenSearch cluster running and accessible from VPC
- VPC configured with appropriate subnets and security groups
- Required environment variables configured (see Configuration section)

---

### Method 1: AWS Toolkit Deployment

Deploy directly from your IDE using the AWS Toolkit extension.

#### For Visual Studio 2022:

1. **Install AWS Toolkit:**
   - Install the AWS Toolkit for Visual Studio from the Visual Studio Marketplace

2. **Configure AWS Credentials:**
   - Ensure your AWS credentials are configured in Visual Studio
   - Go to View → AWS Explorer and configure your profile

3. **Deploy the Function:**
   - Right-click on the `TenderToolSearchLambda.csproj` project
   - Select "Publish to AWS Lambda..."
   - Choose "ASP.NET Core Web API" as the function blueprint
   - Configure the deployment settings:
     - **Function Name**: `TenderToolSearchLambda`
     - **Runtime**: `.NET 8`
     - **Memory**: `512 MB`
     - **Timeout**: `30 seconds`
     - **Handler**: `TenderToolSearchLambda::TenderToolSearchLambda.LambdaEntryPoint::FunctionHandlerAsync`

4. **Configure VPC Settings:**
   - **VPC**: Select your VPC
   - **Subnets**: `subnet-0f47b68400d516b1e`, `subnet-072a27234084339fc`
   - **Security Groups**: `sg-0dc0af4fcf50676e9`

5. **Configure API Gateway:**
   - The function will automatically create an API Gateway with `/{proxy+}` and `/` routes
   - Note the generated API Gateway URL for testing

#### For VS Code:

1. **Install AWS Toolkit:**
   - Install the AWS Toolkit extension for VS Code

2. **Open Command Palette:**
   - Press `Ctrl+Shift+P` (Windows/Linux) or `Cmd+Shift+P` (Mac)
   - Type "AWS: Deploy SAM Application"

3. **Follow the deployment wizard** to configure and deploy your function

---

### Method 2: SAM Deployment

Deploy using AWS SAM CLI with the provided serverless template.

#### Step 1: Install SAM CLI

```bash
# For Windows (using Chocolatey)
choco install aws-sam-cli

# For macOS (using Homebrew)
brew install aws-sam-cli

# For Linux (using pip)
pip install aws-sam-cli
```

#### Step 2: Install Lambda Tools

```bash
dotnet tool install -g Amazon.Lambda.Tools
```

#### Step 3: Configure Application Settings

Ensure your `appsettings.json` has the correct OpenSearch endpoint:

```json
{
  "OpenSearch": {
    "Endpoint": "https://vpc-tender-tool-search-your-domain-id.us-east-1.es.amazonaws.com"
  }
}
```

#### Step 4: Build and Deploy

```bash
# Build the project
dotnet restore
dotnet build -c Release

# Package the Lambda function (ASP.NET Core style)
dotnet lambda package -c Release -o ./lambda-package.zip TenderToolSearchLambda.csproj

# Deploy using SAM with guided setup
sam deploy --template-file serverless.template \
           --stack-name tender-tool-search-api-stack \
           --capabilities CAPABILITY_IAM \
           --guided
```

#### Alternative: Direct SAM Deploy

For subsequent deployments after initial setup:

```bash
sam deploy --template-file serverless.template \
           --stack-name tender-tool-search-api-stack \
           --capabilities CAPABILITY_IAM \
           --parameter-overrides \
             OpenSearchEndpoint="https://vpc-tender-tool-search-your-domain-id.us-east-1.es.amazonaws.com"
```

#### Important VPC Configuration

The serverless template includes VPC configuration. Ensure your AWS account has:
- VPC with subnets: `subnet-0f47b68400d516b1e`, `subnet-072a27234084339fc`
- Security group: `sg-0dc0af4fcf50676e9`
- Security group configured to allow:
  - Outbound access to OpenSearch cluster on port 443
  - Inbound access from API Gateway (if needed)

---

### Method 3: Workflow Deployment (GitHub Actions)

Deploy automatically using GitHub Actions when pushing to the release branch.

#### Step 1: Set Up Repository Secrets

In your GitHub repository, go to Settings → Secrets and variables → Actions, and add:

```
AWS_ACCESS_KEY_ID: your-aws-access-key-id
AWS_SECRET_ACCESS_KEY: your-aws-secret-access-key
AWS_REGION: us-east-1
```

#### Step 2: Deploy via Release Branch

```bash
# Create and switch to release branch
git checkout -b release

# Make your changes and commit
git add .
git commit -m "Deploy Tender Tool Search API updates"

# Push to trigger deployment
git push origin release
```

#### Step 3: Monitor Deployment

1. Go to your repository's **Actions** tab
2. Monitor the "Deploy .NET Lambda to AWS" workflow
3. Check the deployment logs for any issues

#### Manual Trigger

You can also trigger the deployment manually:

1. Go to the **Actions** tab in your repository
2. Select "Deploy .NET Lambda to AWS"
3. Click "Run workflow"
4. Select the branch and click "Run workflow"

---

### Post-Deployment Verification

After deploying using any method, verify the deployment:

#### 1. Check Lambda Function

```bash
# Verify function exists and configuration
aws lambda get-function --function-name tender-tool-search-api-stack-AspNetCoreFunction-7N6zjzdb9zKH

# Check environment variables and VPC configuration
aws lambda get-function-configuration --function-name tender-tool-search-api-stack-AspNetCoreFunction-7N6zjzdb9zKH
```

#### 2. Test API Gateway Endpoint

```bash
# Get the API Gateway URL from CloudFormation outputs
aws cloudformation describe-stacks --stack-name tender-tool-search-api-stack --query 'Stacks[0].Outputs'

# Test the health check endpoint
curl -X GET https://your-api-gateway-url.execute-api.us-east-1.amazonaws.com/Prod/

# Test the search endpoint
curl -X POST https://your-api-gateway-url.execute-api.us-east-1.amazonaws.com/Prod/api/search \
  -H "Content-Type: application/json" \
  -d '{"query": "test", "page": 1, "pageSize": 10}'
```

#### 3. Verify OpenSearch Connectivity

```bash
# Check CloudWatch logs for any connection issues
aws logs describe-log-groups --log-group-name-prefix "/aws/lambda/tender-tool-search-api-stack"

# View recent logs
aws logs tail "/aws/lambda/tender-tool-search-api-stack-AspNetCoreFunction" --follow
```

---

### Critical Post-Deployment Setup

#### OpenSearch Fine-Grained Access Control Configuration

**IMPORTANT:** After first deployment, you must configure OpenSearch permissions manually:

1. **Find the Lambda IAM Role ARN:**
   ```bash
   aws cloudformation describe-stack-resources --stack-name tender-tool-search-api-stack --query 'StackResources[?LogicalResourceId==`AspNetCoreFunctionRole`].PhysicalResourceId' --output text
   ```

2. **Start Bastion Host:**
   - Go to EC2 console and start the `tender-tool-bastion` instance
   - Note the public IP address

3. **Create SSH Tunnel:**
   ```bash
   ssh -i "path/to/your-key.pem" -N -L 8443:vpc-tender-tool-search-your-domain-id.us-east-1.es.amazonaws.com:443 ec2-user@BASTION_PUBLIC_IP
   ```

4. **Configure OpenSearch Dashboard:**
   - Open browser to `https://localhost:8443/_dashboards`
   - Login as `opensearch_admin`
   - Navigate to Security → Roles → `readall`
   - Click "Mapped users" tab → "Manage mapping"
   - Add the Lambda's IAM Role ARN to "Backend roles"
   - Click "Map" to save

---

### Environment Variables Setup

The function uses `appsettings.json` for configuration. Ensure this file contains:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "OpenSearch": {
    "Endpoint": "https://vpc-tender-tool-search-your-domain-id.us-east-1.es.amazonaws.com"
  }
}
```

> **Security Note**: For production deployments, consider using AWS Parameter Store or Secrets Manager for the OpenSearch endpoint configuration.

---

### Critical VPC and Security Configuration

This Lambda function requires specific VPC configuration to access the OpenSearch cluster:

#### VPC Requirements:
- **Subnets**: Must be in private subnets: `subnet-0f47b68400d516b1e`, `subnet-072a27234084339fc`
- **Security Groups**: Must allow outbound traffic to OpenSearch cluster on port 443

#### Security Group Configuration:

**For Lambda Security Group (sg-0dc0af4fcf50676e9):**
```
Outbound Rules:
- Type: HTTPS, Port: 443, Destination: OpenSearch Security Group
- Type: All Traffic, Port: All, Destination: 0.0.0.0/0 (for API Gateway integration)
```

**For OpenSearch Security Group:**
```
Inbound Rules:
- Type: HTTPS, Port: 443, Source: Lambda Security Group (sg-0dc0af4fcf50676e9)
```

---

### API Gateway Configuration

The deployment automatically creates an API Gateway with the following configuration:

- **Base URL**: `https://{api-id}.execute-api.us-east-1.amazonaws.com/Prod/`
- **Health Check**: `GET /` → Returns "Welcome to the Tender Tool Search Lambda"
- **Search Endpoint**: `POST /api/search` → Accepts search queries

#### Testing the API:

```bash
# Health check
curl https://your-api-gateway-url.execute-api.us-east-1.amazonaws.com/Prod/

# Search request
curl -X POST https://your-api-gateway-url.execute-api.us-east-1.amazonaws.com/Prod/api/search \
  -H "Content-Type: application/json" \
  -d '{
    "query": "electrical maintenance",
    "page": 1,
    "pageSize": 10
  }'
```

---

### Troubleshooting Deployment Issues

**OpenSearch Connection Errors:**
- Verify Lambda is in the same VPC as OpenSearch cluster
- Check security group rules allow Lambda to reach OpenSearch on port 443
- Ensure OpenSearch domain is accessible from the VPC

**IAM Permission Errors:**
- Verify the Lambda execution role has `es:ESHttpGet` and `es:ESHttpPost` permissions
- Check that the OpenSearch resource ARN in the policy matches your domain
- Ensure Fine-Grained Access Control is properly configured in OpenSearch

**API Gateway Errors:**
- Check CloudWatch logs for detailed error messages
- Verify the Lambda function is responding correctly
- Test the Lambda function directly before testing through API Gateway

**VPC Configuration Issues:**
- Ensure subnets exist and are in the correct VPC
- Verify security group exists and has appropriate rules
- Check that the VPC has proper routing for internet access (if needed)

## 🧰 Troubleshooting & Gotchas

<details>
<summary><strong>ERROR: 500 - "no permissions for [indices:data/read/search]"</strong></summary>

**Issue**: You will see this error the first time you call the API after deployment. The Postman response will be a 500 error containing `security_exception: "no permissions for [indices:data/read/search]"` and `403 Forbidden`.

**Reason**: This is the "Internal Bouncer." The Lambda's new IAM role (`tender-tool-search-api-stack-AspNetCoreFunctionRole-...`) is not yet recognized by OpenSearch's internal security.

**The Fix (Manual, One-Time Step):**
You must map this new IAM role to the `readall` permission *inside* OpenSearch.

1. **Find the Role ARN:** Go to **CloudFormation** → `tender-tool-search-api-stack` → **Resources** tab → click the Physical ID for the `AspNetCoreFunction` role and **copy its ARN**.
2. **Start Your Bastion:** Go to EC2 and **"Start"** the `tender-tool-bastion` instance. Copy its new **Public IPv4 address**.
3. **Start SSH Tunnel:** Open your terminal and run:
   ```bash
   ssh -i "C:\path\to\your-key.pem" -N -L 8443:vpc-tender-tool-search-m2hyjgolvayz42ki2zjhq3atly.us-east-1.es.amazonaws.com:443 ec2-user@[YOUR_BASTION_PUBLIC_IP]
   ```
4. **Log in to Dashboard:** Go to **`https://localhost:8443/_dashboards`**. Log in as `opensearch_admin`.
5. **Map the Role:**
   - Go to the "hamburger" menu (☰) → **"Security"** → **"Roles"**.
   - Find and click on the **`readall`** role.
   - Click the **"Mapped users"** tab.
   - Click **"Manage mapping"**.
   - In the **"Backend roles"** section, paste the **Lambda's IAM Role ARN** you copied in step 1.
   - Click **"Map"**.

Your search API will now be fully authorized and will start returning `200 OK` responses.

</details>

<details>
<summary><strong>ERROR: Connection timeout to OpenSearch</strong></summary>

**Issue**: The Lambda times out when trying to connect to OpenSearch.

**Reason**: VPC networking misconfiguration. The Lambda cannot reach the OpenSearch cluster.

**Fix**: Verify that:
- The Lambda is deployed in the same VPC as your OpenSearch cluster
- The Lambda's subnets have route tables pointing to a NAT Gateway (for internet access if needed)
- The Lambda's security group allows outbound traffic to the OpenSearch cluster

</details>

<details>
<summary><strong>ERROR: Empty search results when data exists</strong></summary>

**Issue**: The API returns `totalResults: 0` even though you know data exists in OpenSearch.

**Reason**: The `TenderSearchDocument` model in this Lambda doesn't match the document structure created by the `TenderToolSyncLambda`.

**Fix**: Ensure both projects use identical `TenderSearchDocument.cs` models. Any field name differences will cause mapping issues.

</details>

<details>
<summary><strong>Performance: Slow search responses</strong></summary>

**Issue**: Search queries take several seconds to return results.

**Reason**: The OpenSearch cluster might be under-provisioned or the query needs optimization.

**Fix**: 
- Check the OpenSearch cluster's performance metrics in CloudWatch
- Consider optimizing the `multi_match` query in `SearchController.cs`
- Ensure proper indexing on frequently searched fields

</details>

---

> Built with love, bread, and code by **Bread Corporation** 🦆❤️💻
