# Deployment roadmap

This roadmap takes the system from one EC2 host to a multi-region, active-passive deployment. It follows the stages in [How to scale your AWS architecture from EC2 to multi-region deployment](https://dev.to/aws-builders/how-to-scale-your-aws-architecture-from-ec2-to-multi-region-deployment-5ag7). The article's 20 stages are grouped into versions here. Where this codebase needs a stage reordered, merged or skipped, the version says so and why.

Only **v1 is built**: `deploy/terraform/` and `deploy/ansible/`. Later versions are plans. Nothing here changes the open items in [decisions.md](decisions.md) unless the version says it closes one.

## Code facts that shape the roadmap

The article assumes a stateless app that can be copied behind a load balancer. These facts about the current code decide where that assumption breaks. Each one is an entry criterion for a later version. Remove a row when the code changes.

| Fact (file) | Consequence |
| --- | --- |
| `OutboundWebhookDeliveryDispatcher` selects due rows without a claim or lock (`Presentation/Services/OutboundWebhookDeliveryDispatcher.cs`) | Two or more API instances would deliver the same webhook twice. Blocks horizontal scaling until rows are claimed (`FOR UPDATE SKIP LOCKED` or a lease column). |
| The unauthenticated auth limiter is ASP.NET's in-memory fixed-window limiter (`Presentation/Program.cs`); the authenticated limiter uses Redis (`Application/Services/RateLimitService.cs`) | Behind a load balancer, the per-IP auth limit multiplies by the instance count. Move it to Redis or a WAF rate rule before scaling out. |
| BillingService uses a file-backed JSON queue, one instance and in-process timers (`BillingService/src/app.ts`, open decision #8) | Billing stays a singleton until the queue is replaced. It can never be active in two regions. |
| Migrations aren't applied at startup (no `Migrate()` in `Presentation`) | Migrations are an explicit deploy step, run once per release, never per instance. v1 ships an EF migrations bundle. |
| Forwarded headers trust loopback when nothing is configured (`Presentation/Program.cs`) | v1's NGINX on the same host needs no `KnownNetworks`. Behind an ALB, trust the ALB subnets. |
| No file uploads, static assets or Data Protection keys | Article stages 12, 13, 17 and 18 (EBS, EFS, S3 assets, S3-triggered Lambda) mostly don't apply. S3 is for backups and logs. |
| Health checks cover PostgreSQL only (open decision #11) | ALB and Route 53 health checks won't notice a Redis outage. Decide before relying on failover. |
| Debian 12 ships PostgreSQL 15 and Node 18 | v1 adds the PGDG (PostgreSQL 16), NodeSource (Node 22) and Microsoft (ASP.NET Core 10) apt repositories. Debian's Redis 7.0 is acceptable. |

## v1: single host, configured by Ansible (article stage 1). Built.

- **Infrastructure (`deploy/terraform/`):** VPC, one public subnet, internet gateway, security group (80 open, 22 only from `ssh_ingress_cidrs`), and a `t3.small` Debian 12 instance with IMDSv2, an encrypted gp3 root volume and an SSM instance profile. The instance has no user data; an optional `ssh_public_key` becomes the EC2 key pair Ansible uses. Defaults target the Floci emulator.
- **Configuration (`deploy/ansible/`):** PostgreSQL 16, Redis 7, the ASP.NET Core 10 runtime, Node 22 and NGINX installed natively. The API and BillingService run as hardened systemd services under their own users. Everything but NGINX binds to loopback (BillingService excepted; see the Ansible README).
- **Releases:** built on the controller, shipped to versioned release directories behind a `current` symlink, and migrated with an EF Core migrations bundle before the switch.
- **Secrets:** Ansible Vault on the controller, written to `/etc/multitenant-saas-api/*.env` (`0600`, root). Nothing secret is in Git or in user data.
- **Not yet:** TLS, DNS, backups, remote Terraform state, CI. Back up with `pg_dump` by hand until v1.1.

The Docker Compose profile (`compose.yml`, `deploy/*.Dockerfile`) stays as the local VM profile.

## v1.1: DNS, TLS and basics (article stages 2 and 8, moved early)

- Route 53 hosted zone, an Elastic IP, an apex record and a wildcard record for tenant subdomains (`TenantResolution__BaseDomain`).
- TLS through certbot with the Route 53 DNS-01 plugin, which can issue the wildcard certificate. The article puts ACM on a load balancer in stage 8, but a SaaS with authentication can't wait for a load balancer to get HTTPS. ACM replaces certbot in v3.
- Remote Terraform state in S3 with locking.
- Nightly `pg_dump` to S3 and an EBS snapshot policy (DLM).
- CORS allowlist: set `api_cors_allowed_origins` once the browser origins are known (decisions #3 and #4). Until then the API, running as `Production`, allows no cross-origin browser calls.
- CI: build, tests, `ansible-lint`, `tofu plan`.

## v2: managed data tier (article stages 4, 14 and part of 9; stage 3 skipped)

- Move straight to RDS PostgreSQL and ElastiCache Redis in private subnets. The article's interim "database on a second EC2" (stage 3) is skipped: Ansible would only rebuild by hand what RDS manages.
- Migrate with `pg_dump`/`pg_restore` in a maintenance window.
- Ansible drops the `postgresql` and `redis` roles; only connection strings change.
- Secrets move to SSM Parameter Store or Secrets Manager (decision #5).
- CloudWatch agent for logs and metrics; observability export (decision #9).
- Vertical scaling (stage 5) is available at any point through `instance_type`.
- First backup and restore drill (decision #10).

## v3: horizontal, multi-AZ (article stages 6, 7, 8, 9, 10, 11, 15 and 19)

App prerequisites, from the code facts above:

- Claim webhook rows in the dispatcher.
- Back the auth limiter with Redis (or a WAF rate rule).
- Run migrations as a single pre-deploy job.
- Trust the ALB subnets in forwarded headers.

Infrastructure:

- ALB with ACM in public subnets; app instances in private subnets behind a NAT gateway or VPC endpoints.
- Auto Scaling group across two AZs, built from a Packer + Ansible golden AMI, rolled out with instance refresh and ELB health checks.
- RDS Multi-AZ and an ElastiCache replica.
- SSM Session Manager instead of a bastion host (replaces stage 15).

Billing stays a single instance (an ASG of size 1 or a separate host) until decision #8 replaces the queue. The intended replacement, SQS + Lambda + DynamoDB dedup + EventBridge Scheduler, is where article stage 18 lands.

Stages 12, 13 and 16: no EFS, because nothing is shared on disk. CloudFront is optional, for an edge WAF and DDoS layer only.

## v4: multi-region active-passive (article stage 20)

- Aurora PostgreSQL Global Database: the primary region writes; the secondary holds a replica that can be promoted.
- The secondary region runs a warm-standby ASG at minimum size, its own ElastiCache (regional rate-limit counters are acceptable), ACM certificates, and replicated Secrets Manager secrets.
- Route 53 failover routing with health checks; the wildcard tenant records fail over too.
- Billing runs only in the primary region; the failover runbook promotes it.
- Terraform restructured into modules with a stack per region.
- RPO and RTO targets defined, and a DR drill each quarter.

Active-active is out of scope. It needs tenant home-region pinning or write-conflict handling, plus a distributed billing queue.
