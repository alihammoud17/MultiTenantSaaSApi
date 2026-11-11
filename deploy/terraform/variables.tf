variable "use_floci" {
  description = "Target the local Floci emulator instead of a real AWS account."
  type        = bool
  default     = true
}

variable "floci_endpoint" {
  description = "Floci edge endpoint. Use http://host.docker.internal:4566 when Terraform itself runs in Docker."
  type        = string
  default     = "http://localhost:4566"
}

variable "aws_region" {
  description = "AWS region."
  type        = string
  default     = "us-east-1"
}

variable "environment" {
  description = "Environment label used in names and tags."
  type        = string
  default     = "floci"
}

variable "name_prefix" {
  description = "Prefix for resource names."
  type        = string
  default     = "mtsaas"
}

# --- Network ---

variable "vpc_cidr" {
  description = "CIDR block for the VPC."
  type        = string
  default     = "10.20.0.0/16"
}

variable "public_subnet_cidr" {
  description = "CIDR block for the public subnet."
  type        = string
  default     = "10.20.1.0/24"
}

variable "availability_zone_suffix" {
  description = "AZ letter appended to aws_region for the public subnet."
  type        = string
  default     = "a"
}

variable "http_ingress_cidrs" {
  description = "CIDRs allowed to reach HTTP (port 80, NGINX) on the instance."
  type        = list(string)
  default     = ["0.0.0.0/0"]
}

variable "ssh_ingress_cidrs" {
  description = "CIDRs allowed SSH (port 22). Empty by default. Ansible needs your /32 here on real AWS; Floci publishes SSH on a local Docker port instead."
  type        = list(string)
  default     = []
}

# --- Compute ---

variable "instance_type" {
  description = "EC2 instance type for the single app host (PostgreSQL, Redis, API, BillingService, NGINX). t3.micro's 1 GiB is too small."
  type        = string
  default     = "t3.small"
}

variable "ami_name" {
  description = "Exact AMI name to look up."
  type        = string
  default     = "debian-12-amd64-20230411"
}

variable "ami_owners" {
  description = "AMI owners to search. Floci publishes its Debian image as \"amazon\"; on real AWS the Debian project's owner is 136693071363 and names carry a build suffix."
  type        = list(string)
  default     = ["amazon"]
}

variable "root_volume_size_gb" {
  description = "Root EBS volume size in GiB."
  type        = number
  default     = 20
}

variable "ssh_public_key" {
  description = "OpenSSH public key (contents, not a path) registered as an EC2 key pair for Ansible. Null skips the key pair."
  type        = string
  default     = null
}

variable "ansible_user" {
  description = "Login user Ansible connects as. Floci injects keys for root; Debian's AWS images use admin."
  type        = string
  default     = "root"
}
