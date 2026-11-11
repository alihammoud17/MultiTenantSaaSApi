# Floci (local AWS emulator) is the default target. Set use_floci = false to use the
# standard AWS credential chain against a real account instead.
provider "aws" {
  region = var.aws_region

  # Floci accepts any credentials; these are emulator placeholders, not secrets.
  access_key                  = var.use_floci ? "test" : null
  secret_key                  = var.use_floci ? "test" : null
  skip_credentials_validation = var.use_floci
  skip_metadata_api_check     = var.use_floci
  skip_requesting_account_id  = var.use_floci

  dynamic "endpoints" {
    for_each = var.use_floci ? [var.floci_endpoint] : []

    content {
      ec2 = endpoints.value
      iam = endpoints.value
      sts = endpoints.value
    }
  }

  default_tags {
    tags = {
      Project     = "multitenant-saas-api"
      Environment = var.environment
      ManagedBy   = "terraform"
    }
  }
}
