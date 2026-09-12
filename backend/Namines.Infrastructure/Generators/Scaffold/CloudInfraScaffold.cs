using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.Scaffold;

/// <summary>
/// Bulut altyapisi: AWS / Azure Terraform + GitHub Actions dagitim is akislari.
///
/// <see cref="Namines.Infrastructure.Services.ScaffolderService"/>'ten ayrildi
/// (ARCH-003 / B-37): 1.760 satirlik tek dosya, hedef basina bir uretece
/// bolundu -- <c>Generators/Eject/</c> altindaki mevcut desenle ayni.
/// Metot govdeleri DEGISMEDI; <c>ScaffolderSnapshotTests</c> ciktinin bayt
/// bayt ayni kaldigini kanitliyor.
/// </summary>
internal static class CloudInfraScaffold
{
    internal static string GenerateAwsTerraformMain()
    {
        return @"# Terraform config for AWS deployment
provider ""aws"" {
  region = var.aws_region
}

resource ""aws_vpc"" ""main"" {
  cidr_block           = ""10.0.0.0/16""
  enable_dns_hostnames = true
  tags = {
    Name = ""namines-vpc""
  }
}

resource ""aws_subnet"" ""public_a"" {
  vpc_id                  = aws_vpc.main.id
  cidr_block              = ""10.0.1.0/24""
  availability_zone       = ""${var.aws_region}a""
  map_public_ip_on_launch = true
}

resource ""aws_subnet"" ""public_b"" {
  vpc_id                  = aws_vpc.main.id
  cidr_block              = ""10.0.2.0/24""
  availability_zone       = ""${var.aws_region}b""
  map_public_ip_on_launch = true
}

resource ""aws_internet_gateway"" ""gw"" {
  vpc_id = aws_vpc.main.id
}

resource ""aws_route_table"" ""public"" {
  vpc_id = aws_vpc.main.id
  route {
    cidr_block = ""0.0.0.0/0""
    gateway_id = aws_internet_gateway.gw.id
  }
}

resource ""aws_route_table_association"" ""a"" {
  subnet_id      = aws_subnet.public_a.id
  route_table_id = aws_route_table.public.id
}

resource ""aws_route_table_association"" ""b"" {
  subnet_id      = aws_subnet.public_b.id
  route_table_id = aws_route_table.public.id
}

resource ""aws_security_group"" ""api_sg"" {
  name   = ""namines-api-sg""
  vpc_id = aws_vpc.main.id
  ingress {
    from_port   = 80
    to_port     = 80
    protocol    = ""tcp""
    cidr_blocks = [""0.0.0.0/0""]
  }
  egress {
    from_port   = 0
    to_port     = 0
    protocol    = ""-1""
    cidr_blocks = [""0.0.0.0/0""]
  }
}

resource ""aws_ecs_cluster"" ""main"" {
  name = ""namines-ecs-cluster""
}

resource ""aws_db_subnet_group"" ""db_subnets"" {
  name       = ""namines-db-subnet-group""
  subnet_ids = [aws_subnet.public_a.id, aws_subnet.public_b.id]
}

resource ""aws_security_group"" ""db_sg"" {
  name   = ""namines-db-sg""
  vpc_id = aws_vpc.main.id
  ingress {
    from_port       = 5432
    to_port         = 5432
    protocol        = ""tcp""
    security_groups = [aws_security_group.api_sg.id]
  }
}

resource ""aws_db_instance"" ""postgres"" {
  allocated_storage      = 20
  engine                 = ""postgres""
  engine_version         = ""15""
  instance_class         = ""db.t3.micro""
  db_name                = ""naminesdb""
  username               = ""dbadmin""
  password               = var.db_password
  db_subnet_group_name   = aws_db_subnet_group.db_subnets.name
  vpc_security_group_ids = [aws_security_group.db_sg.id]
  skip_final_snapshot    = true
}
";
    }

    internal static string GenerateAwsTerraformVariables()
    {
        return @"variable ""aws_region"" {
  type    = string
  default = ""eu-central-1""
}

variable ""db_password"" {
  type      = string
  sensitive = true
}
";
    }

    internal static string GenerateAwsTerraformOutputs()
    {
        return @"output ""db_endpoint"" {
  value = aws_db_instance.postgres.endpoint
}

output ""ecs_cluster_name"" {
  value = aws_ecs_cluster.main.name
}
";
    }

    internal static string GenerateAwsGithubWorkflow()
    {
        return @"name: Zero-to-Cloud CI/CD Deploy (AWS)

on:
  push:
    branches: [ ""main"" ]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v3

    - name: Configure AWS Credentials
      uses: aws-actions/configure-aws-credentials@v1
      with:
        aws-access-key-id: ${{ secrets.AWS_ACCESS_KEY_ID }}
        aws-secret-access-key: ${{ secrets.AWS_SECRET_ACCESS_KEY }}
        aws-region: eu-central-1

    - name: Docker Build and Push
      run: |
        docker build -t naminesproject-api:latest -f backend/NaminesProject.API/Dockerfile .
        # ecr push commands go here...

    - name: Setup Terraform
      uses: hashicorp/setup-terraform@v2

    - name: Terraform Apply
      run: |
        cd infrastructure/terraform
        terraform init
        terraform apply -auto-approve -var=""db_password=${{ secrets.DB_PASSWORD }}""
";
    }

    internal static string GenerateAzureTerraformMain()
    {
        return @"# Terraform config for Azure deployment
provider ""azurerm"" {
  features {}
}

resource ""azurerm_resource_group"" ""rg"" {
  name     = ""rg-namines-prod""
  location = var.location
}

resource ""azurerm_service_plan"" ""plan"" {
  name                = ""asp-namines""
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  os_type             = ""Linux""
  sku_name            = ""B1""
}

resource ""azurerm_linux_web_app"" ""app"" {
  name                = ""app-namines-api""
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  service_plan_id     = azurerm_service_plan.plan.id
  site_config {
    always_on = false
    application_stack {
      docker_image_name   = ""naminesproject-api:latest""
      docker_registry_url = ""https://index.docker.io/v1""
    }
  }
}

resource ""azurerm_mssql_server"" ""sqlserver"" {
  name                         = ""sql-namines-prod""
  resource_group_name          = azurerm_resource_group.rg.name
  location                     = azurerm_resource_group.rg.location
  version                      = ""12.0""
  administrator_login          = ""sqladmin""
  administrator_login_password = var.sql_admin_password
}

resource ""azurerm_mssql_database"" ""sqldb"" {
  name        = ""naminesdb""
  server_id   = azurerm_mssql_server.sqlserver.id
  collation   = ""SQL_Latin1_General_CP1_CI_AS""
  max_size_gb = 2
  sku_name    = ""Basic""
}
";
    }

    internal static string GenerateAzureTerraformVariables()
    {
        return @"variable ""location"" {
  type    = string
  default = ""westeurope""
}

variable ""sql_admin_password"" {
  type      = string
  sensitive = true
}
";
    }

    internal static string GenerateAzureTerraformOutputs()
    {
        return @"output ""app_url"" {
  value = azurerm_linux_web_app.app.default_hostname
}

output ""sql_server_fqdn"" {
  value = azurerm_mssql_server.sqlserver.fully_qualified_domain_name
}
";
    }

    internal static string GenerateAzureGithubWorkflow()
    {
        return @"name: Zero-to-Cloud CI/CD Deploy (Azure)

on:
  push:
    branches: [ ""main"" ]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v3

    - name: Log in to Azure
      uses: azure/login@v1
      with:
        creds: ${{ secrets.AZURE_CREDENTIALS }}

    - name: Setup Terraform
      uses: hashicorp/setup-terraform@v2

    - name: Terraform Apply
      run: |
        cd infrastructure/terraform
        terraform init
        terraform apply -auto-approve -var=""sql_admin_password=${{ secrets.SQL_ADMIN_PASSWORD }}""
";
    }
}
