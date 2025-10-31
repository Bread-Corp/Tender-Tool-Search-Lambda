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

## 📦 Deployment Guide

1. Ensure `appsettings.json` has the correct OpenSearch Endpoint URL.
2. Ensure `serverless.template` has the correct `VpcConfig` (Subnets, Security Group) and `Policies` (Resource ARN).
3. Right-click the project in Visual Studio → **"Publish AWS Serverless Application..."**.
4. Enter a new, unique **Stack Name** (e.g., `tender-tool-search-api-stack`).
5. Select an S3 bucket for deployment.
6. Click **Publish**.
7. Wait for the CloudFormation stack to reach **`CREATE_COMPLETE`**.
8. **CRITICAL:** Follow the steps in the **Troubleshooting & Gotchas** section to map the new Lambda's IAM role inside the OpenSearch Dashboard.

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