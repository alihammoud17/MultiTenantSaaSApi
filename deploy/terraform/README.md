# Terraform: first deployment (Floci)

Infrastructure for the v1 deployment ([roadmap](../../docs/deployment-roadmap.md)): one EC2 host that [`deploy/ansible/`](../ansible/README.md) then configures with PostgreSQL, Redis, the .NET API, BillingService and NGINX as native systemd services. By default it targets [Floci](https://floci.io), a local AWS emulator. Set `use_floci = false` to apply the same configuration to a real AWS account through the standard credential chain.

| File | Resources |
| --- | --- |
| `network.tf` | VPC, public subnet, internet gateway, public route table, and the app security group |
| `compute.tf` | `t3.small` Debian 12 EC2 instance (`debian-12-amd64-20230411`), SSM instance profile, and the optional EC2 key pair Ansible connects with |

The instance has no user data; Terraform stops at the infrastructure and Ansible does all host configuration. BillingService runs on the same host rather than as a separate serverless resource: it needs a long-running process (retry worker and reconciliation timers), a persisted workflow-state file, a single instance, and loopback access to `/api/internal/*`.

## Defaults worth knowing

- **Instance size:** `t3.small` (2 GiB); Ansible adds a 2 GiB swap file on AWS. A `t3.micro` (1 GiB) is too small for PostgreSQL, Redis, the API and BillingService together.
- **Security group:** HTTP (80) from `http_ingress_cidrs` (default `0.0.0.0/0`, for NGINX). SSH (22) stays closed unless `ssh_ingress_cidrs` is set; Ansible needs your `/32` there on AWS.
- **SSH for Ansible:** set `ssh_public_key` to the contents of a public key and Terraform registers it as an EC2 key pair. `ansible_user` (default `root`, for Floci) becomes an output that `deploy/ansible/scripts/inventory.sh` reads; set it to `admin` on AWS.
- **Instance hardening:** IMDSv2 is required (hop limit 1; nothing runs in containers), the 20 GiB gp3 root volume is encrypted, and CPU credits are `standard` so the T3 doesn't bill surplus credits.
- **Secrets:** Terraform handles none. Ansible writes them to `/etc/multitenant-saas-api/` from Ansible Vault.
- **AMI lookup:** Floci publishes `debian-12-amd64-20230411` under owner `amazon`. On real AWS, set `ami_owners = ["136693071363"]` (Debian) and an `ami_name` that matches a published build, for example `debian-12-amd64-*`.

## Manual provisioning guide

Run every command from the repository root unless a step says otherwise. Steps 1–6 work against Floci or real AWS. Steps 7–9 configure the host with Ansible; on Floci only the SSH and `common` checks apply, because Floci runs the instance as a container without systemd.

### 1. Prerequisites

| Tool | Needed for |
| --- | --- |
| Terraform 1.6+ **or** OpenTofu 1.6+ (a local binary or Docker) | Every step |
| AWS CLI v2 | Verifying resources and reaching the instance |
| [Floci](https://floci.io) on `localhost:4566` | The Floci target |
| AWS credentials for a sandbox account | The real AWS target |
| An SSH key pair for Ansible (`ssh-keygen -t ed25519 -f ~/.ssh/mtsaas-ansible -N ''`) | Steps 7–9 |
| Ansible and the tools in [deploy/ansible/README.md](../ansible/README.md#prerequisites-controller) | Steps 7–9 |

If `registry.terraform.io` can't be reached from your network ("Content not available in your region"), use OpenTofu. It reads these same files and fetches the same `hashicorp/aws` provider from the OpenTofu registry. A Docker wrapper that needs no local install:

```bash
tofu() { docker run --rm -v "$PWD":/repo -w /repo/deploy/terraform \
  -e TF_VAR_floci_endpoint=http://host.docker.internal:4566 \
  ghcr.io/opentofu/opentofu:1.10 "$@"; }
```

The steps below use `terraform`. Substitute `tofu` if you use OpenTofu. With a local binary, run the commands from `deploy/terraform/` instead of the repository root.

### 2. Choose a target

**Floci (default).** Start Floci and confirm it answers:

```bash
curl -s http://localhost:4566/_floci/health | head -c 200
```

The provider uses Floci's placeholder credentials and endpoint. To reach the instance over SSH, pass your public key: add `-var "ssh_public_key=$(cat ~/.ssh/mtsaas-ansible.pub)"` to `plan` and `apply`.

**Real AWS.** Create `deploy/terraform/aws.tfvars`. It is git-ignored; never commit it.

```hcl
use_floci          = false
aws_region         = "us-east-1"
environment        = "sandbox"
ami_owners         = ["136693071363"]      # Debian
ami_name           = "debian-12-amd64-*"    # newest published Debian 12 build
http_ingress_cidrs = ["203.0.113.10/32"]    # your public IP, not 0.0.0.0/0, while testing
ssh_ingress_cidrs  = ["203.0.113.10/32"]    # Ansible connects over SSH
ssh_public_key     = "ssh-ed25519 AAAA... you@laptop"   # contents of ~/.ssh/mtsaas-ansible.pub
ansible_user       = "admin"                # Debian's AWS images
```

Then export credentials for the sandbox account, for example `export AWS_PROFILE=sandbox`. If you use the Docker wrapper, also pass `-v ~/.aws:/root/.aws:ro -e AWS_PROFILE` to `docker run`. Add `-var-file=aws.tfvars` to every `plan`, `apply` and `destroy` below.

A `t3.small` costs about $0.02 per hour on-demand in `us-east-1`. Destroy the stack when you're done (step 10).

### 3. Initialise and check statically

```bash
terraform init
terraform fmt -check -recursive
terraform validate
```

All three must succeed before you continue.

### 4. Review the plan

```bash
terraform plan -out=first.tfplan
```

A fresh state should report **`Plan: 11 to add, 0 to change, 0 to destroy`**: VPC, subnet, internet gateway, route table, route table association, security group, IAM role, role policy attachment, instance profile, key pair and instance. Without `ssh_public_key` it's 10, with no key pair. (The AMI is a data source, so it isn't counted.) Read the security group block: port 80 should allow only `http_ingress_cidrs`, and port 22 should appear only if you set `ssh_ingress_cidrs`.

### 5. Apply

```bash
terraform apply first.tfplan
terraform output
```

Keep the outputs. Later steps use `app_instance_id`, `app_instance_public_ip`, `app_security_group_id`, `vpc_id`, `ansible_user` and `app_health_url`.

### 6. Verify the infrastructure

For Floci, set the CLI first:

```bash
export AWS_ACCESS_KEY_ID=test AWS_SECRET_ACCESS_KEY=test AWS_DEFAULT_REGION=us-east-1
alias awsf='aws --endpoint-url http://localhost:4566'
```

On real AWS, use plain `aws` in place of `awsf`.

```bash
ID=$(terraform output -raw app_instance_id)

# Instance: t3.small, Debian AMI, running, IMDSv2 required
awsf ec2 describe-instances --instance-ids "$ID" \
  --query 'Reservations[0].Instances[0].[InstanceType,ImageId,State.Name,MetadataOptions.HttpTokens]' --output text

# Security group: only TCP 80 from your CIDRs (no 22 unless you asked for it)
awsf ec2 describe-security-groups --group-ids "$(terraform output -raw app_security_group_id)" \
  --query 'SecurityGroups[0].IpPermissions[].[FromPort,ToPort,IpRanges[].CidrIp]' --output text

# Routing: 0.0.0.0/0 goes to the internet gateway
awsf ec2 describe-route-tables --filters "Name=vpc-id,Values=$(terraform output -raw vpc_id)" \
  --query 'RouteTables[].Routes[].[DestinationCidrBlock,GatewayId]' --output text

# Key pair: the Ansible key is attached
awsf ec2 describe-instances --instance-ids "$ID" \
  --query 'Reservations[0].Instances[0].KeyName' --output text
```

Then confirm the stack is idempotent:

```bash
terraform plan -detailed-exitcode   # exit code 0 = no changes; 2 = drift to investigate
```

**Floci limitations (Floci 2.3).** `RunInstances` ignores the requested security groups and always attaches `default`, and it doesn't report the public IP association. As a result, on Floci:

- `describe-instances` shows the instance in `sg-default-us-east-1`.
- Every later plan wants to replace `aws_instance.app` (`associate_public_ip_address` and `vpc_security_group_ids`), so the idempotency check exits `2`.

These are emulator gaps; plain `aws ec2 run-instances` against Floci behaves the same way. Treat the security group *rules* check above as the Floci signal. Check the instance's group attachment and idempotency (exit `0`) on real AWS.

### 7. Generate the Ansible inventory

```bash
cd deploy/ansible
scripts/inventory.sh floci ~/.ssh/mtsaas-ansible   # or: aws
```

The script reads the Terraform outputs (`TF_BIN=terraform` if you don't use `tofu`) and writes the git-ignored `inventories/<target>/hosts.yml`. On Floci it finds the instance's SSH port with `docker port floci-aws-ec2-<id> 22`; Floci starts sshd about a minute after launch. On AWS it uses the public IP and `ansible_user`.

To log in by hand:

```bash
# Floci (root, local port)
ssh -i ~/.ssh/mtsaas-ansible -p "$(docker port "floci-aws-ec2-$(tofu -chdir=../terraform output -raw app_instance_id)" 22 | head -n1 | awk -F: '{print $NF}')" root@localhost
# AWS
ssh -i ~/.ssh/mtsaas-ansible admin@"$(tofu -chdir=../terraform output -raw app_instance_public_ip)"
```

### 8. Configure the host

Create the secrets file first; see [Secrets](../ansible/README.md#secrets).

**Floci:** only the SSH and `common` checks apply.

```bash
ansible -i inventories/floci app -m raw -a 'cat /etc/debian_version'
ansible-playbook -i inventories/floci playbooks/site.yml --tags common
```

**Real AWS:**

```bash
ansible-playbook -i inventories/aws playbooks/site.yml --ask-vault-pass
```

The first run installs PostgreSQL, Redis, the .NET runtime, Node.js and NGINX, builds the release on your machine, applies migrations, and starts `mtsaas-api` and `mtsaas-billing`. Run it again: it should report `changed=0`.

### 9. Smoke-test from your machine (real AWS)

```bash
IP=$(terraform output -raw app_instance_public_ip)
curl -i "http://$IP/health"                       # 200 from the API
curl -i "http://$IP/billing/health"               # 200 from BillingService
curl -i "http://$IP/api/internal/billing/subscription-events" -X POST   # 403 from NGINX
```

Ship later code with `ansible-playbook -i inventories/aws playbooks/deploy.yml --ask-vault-pass`.

To run the full playbook without AWS, use the local systemd test host in [deploy/ansible/README.md](../ansible/README.md#run-the-full-playbook-locally-systemd-test-host).

### 10. Tear down

```bash
terraform destroy          # add -var-file=aws.tfvars for real AWS
```

On real AWS this also deletes the root volume, along with the PostgreSQL data and the BillingService workflow state on it. State is local (`terraform.tfstate`, git-ignored), and no remote backend is configured. Don't delete the state file while resources still exist.

### Troubleshooting

| Symptom | Likely cause | What to do |
| --- | --- | --- |
| `init`: "does not offer a Terraform provider registry" | `registry.terraform.io` is blocked from your network | Use OpenTofu (step 1) |
| Floci calls fail with connection refused from Docker | `localhost` inside the container isn't your machine | Keep `TF_VAR_floci_endpoint=http://host.docker.internal:4566` |
| `plan`: "Your query returned no results" for `aws_ami` | AMI owner or name doesn't match the target | Floci: owner `amazon`, name `debian-12-amd64-20230411`. Real AWS: the values in step 2 |
| Real AWS calls fail with `UnrecognizedClientException` | `use_floci` is still `true`, or no credentials were exported | Pass `-var-file=aws.tfvars` and check `aws sts get-caller-identity` |
| `inventory.sh floci`: no port printed | sshd isn't up yet, or the instance was replaced | Wait a minute after `apply`; re-run the script after every apply |
| SSH `Permission denied (publickey)` | The instance was launched without your key | Apply with `ssh_public_key` set (it replaces the instance), then regenerate the inventory |
| SSH times out on AWS | Port 22 is closed to your IP | Set `ssh_ingress_cidrs` to your current `/32` |
| `ansible -m ping` fails with "python3 was not found" on Floci | Floci's image has no Python | Run `site.yml` once; its first task installs Python |
| Ansible fails on "Refuse placeholder or missing secrets" | `vault.yml` is missing or still has `CHANGE_ME` values | See [Secrets](../ansible/README.md#secrets) |
| `/health` returns 502 from NGINX | The API is still starting or crashed | `sudo journalctl -u mtsaas-api -n 100` on the host |

## Not covered

- TLS, DNS, WAF, and a managed secret store. These are open items in `docs/decisions.md`.
- A remote state backend and a CI-driven release process.
- Backups for PostgreSQL and the BillingService workflow state, which live on the instance's root volume (roadmap v1.1).
