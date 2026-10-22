# Deployment roadmap: single EC2 (Ansible) → multi-region active-passive

- **Date:** 2026-10-17
- **Branch:** `deploy/floci-terraform`
- **Goal:** Write a staged deployment roadmap from one EC2 host to multi-region active-passive, following the dev.to article "How to scale your AWS architecture from EC2 to multi-region deployment". Build v1 now: one Debian 12 EC2 host whose .NET, Node.js, PostgreSQL, Redis and NGINX are configured by Ansible.
- **Status:** `in-progress` (all steps built and tests 1–4 and 6 pass; test 5, the manual real-AWS run, is outstanding)
- **Approval:** approved in plan mode

## Context

The first deployment is one EC2 host (`deploy/terraform/`, already working on Floci). Its user data currently installs Docker and runs `compose.yml`. Ansible will configure .NET, Node.js and PostgreSQL directly on the host for v1. Later versions scale toward multi-region along the article's path. The article has 20 stages; they are grouped into application versions, and a stage is reordered, merged or skipped where this codebase demands it.

### Code facts that shape the roadmap

| Fact (file) | Consequence |
| --- | --- |
| The API runs `OutboundWebhookDeliveryDispatcher` as a hosted service that selects due rows with no claim or lock (`Presentation/Services/OutboundWebhookDeliveryDispatcher.cs:45-56`) | Two or more API instances would **deliver the same webhook twice**. This blocks horizontal scaling until rows are claimed (`FOR UPDATE SKIP LOCKED` or a lease column). |
| The unauthenticated auth limiter is the in-memory ASP.NET fixed-window limiter (`Presentation/Program.cs:121-148`); the authenticated limiter is Redis-backed (`Application/Services/RateLimitService.cs`) | Behind a load balancer, the per-IP auth limit is multiplied by the instance count. Move it to Redis or a WAF rate rule before scaling out. |
| BillingService uses a file-backed JSON queue, a single instance, and in-process timers (`BillingService/src/app.ts`, open decision #8) | Billing stays a **singleton** until the queue is replaced. It can never be active in two regions. |
| Migrations are not applied at startup (no `Migrate()` in `Presentation`) | Migrations become an explicit deploy step (an EF migrations bundle), run once per release, never per instance. |
| Forwarded headers trust loopback by default when nothing is configured (`Presentation/Program.cs:106-118`) | v1 NGINX on the same host needs no `KnownNetworks`. Behind an ALB, trust the ALB subnets. |
| No file uploads, static assets or Data Protection keys | Article stages 12, 13, 17 and 18 (EBS, EFS, S3 assets, S3 to Lambda) are mostly not applicable. S3 serves backups and logs. |
| Health checks cover PostgreSQL only (open decision #11) | Route 53 and ALB health checks will not notice a Redis outage. Decide before relying on failover. |
| Debian 12 ships PostgreSQL 15 and Node 18 | v1 needs the PGDG apt repo (PG 16), NodeSource (Node 22), and the Microsoft repo (ASP.NET Core 10 runtime). Debian's Redis 7.0 is acceptable. |

## Steps

### Roadmap document

- [x] Write the roadmap to `docs/deployment-roadmap.md`. Each version lists the article stages it covers, what changes, its entry criteria, and the app work it needs:
  - **v1 — Single host, Ansible-configured (article stage 1). Built in this branch.**
    - Terraform provides the VPC, public subnet, IGW, security group and a `t3.small` Debian 12 instance.
    - Ansible installs and configures PostgreSQL 16, Redis 7, the ASP.NET Core 10 runtime, Node 22 and NGINX, then deploys the API and BillingService as systemd services. Everything binds to loopback except NGINX on :80.
    - Releases are built on the controller, shipped to versioned release directories, and migrated with an EF bundle.
    - Secrets live in Ansible Vault, outside Git.
    - Backups: a nightly `pg_dump` to local disk, plus an EBS snapshot policy (DLM).
  - **v1.1 — DNS and TLS (article stages 2 and 8, moved early).**
    - Route 53 hosted zone, an Elastic IP, an apex record and a **wildcard record** for tenant subdomains (`TenantResolution__BaseDomain`).
    - TLS through certbot with the Route 53 DNS-01 plugin (wildcard certificate). ACM replaces certbot in v3.
    - Remote Terraform state (S3 with locking).
    - CORS allowlist (decisions #3 and #4).
  - **v2 — Managed data tier (article stages 4, 14 and part of 9; stage 3 skipped).**
    - **RDS PostgreSQL** and **ElastiCache Redis** in private subnets.
    - Migrate with `pg_dump`/`pg_restore` during a maintenance window.
    - Ansible drops the `postgresql` and `redis` roles; only connection strings change.
    - Secrets move to SSM Parameter Store or Secrets Manager (decision #5).
    - Add the CloudWatch agent for logs and metrics.
    - Vertical scaling (article stage 5) is available at any time through `instance_type`.
  - **v3 — Horizontal, multi-AZ (article stages 6, 7, 8, 9, 10, 11, 15 and 19).**
    - *App prerequisites:* webhook dispatcher row claiming; auth limiter backed by Redis; migrations as a single pre-deploy job; forwarded headers trusting the ALB subnets.
    - *Infrastructure:* ALB with ACM in public subnets; app instances in private subnets behind a NAT gateway or VPC endpoints; ASG across two AZs built from a **Packer + Ansible golden AMI**, rolled out with instance refresh and ELB health checks; RDS Multi-AZ and an ElastiCache replica; SSM Session Manager instead of a bastion.
    - *Billing:* stays a single instance (a dedicated ASG of size 1 or a separate host) until decision #8 replaces the queue. The intended replacement is SQS + Lambda + DynamoDB dedup + EventBridge Scheduler, which covers article stage 18.
    - *Stages 12, 13 and 16:* no EFS, because nothing is shared on disk. CloudFront is optional, for an edge WAF and DDoS layer only.
  - **v4 — Multi-region active-passive (article stage 20).**
    - Migrate RDS to **Aurora PostgreSQL Global Database**: the primary region writes, the secondary holds a read replica that can be promoted.
    - The secondary region runs a warm-standby ASG at minimum capacity, its own ElastiCache, ACM certificates, and replicated Secrets Manager secrets.
    - **Route 53 failover routing** with health checks; the wildcard tenant records fail over too.
    - Billing is active only in the primary region; the failover runbook promotes it.
    - Terraform is restructured into modules with per-region stacks.
    - Define RPO and RTO targets, and run a DR drill each quarter.
  - Cross-cutting: **v1.1** CI (build, tests, `ansible-lint`, `tofu plan`); **v2** observability export (decision #9); **v2 / v4** backup and restore drills (decision #10).

### v1 — Terraform (`deploy/terraform/`)

- [x] Remove the user data and `templates/user-data.sh.tftpl`. Swap and the SSM agent move to Ansible.
- [x] Remove the `app_repo_url`, `app_repo_ref` and `swap_size_gb` variables.
- [x] Add the `ssh_public_key` variable and an `aws_key_pair` so Ansible can SSH in. Floci injects the key for `root`; real Debian uses `admin`. Keep `ssh_ingress_cidrs` (empty by default; the operator's `/32` on AWS).
- [x] Add the `ansible_user` output.
- [x] Keep the instance profile (SSM).

### v1 — Ansible (`deploy/ansible/`)

- [x] `ansible.cfg`, `requirements.yml` (`community.postgresql`, `community.general`, `ansible.posix`).
- [x] `inventories/{floci,aws}/` with `group_vars`. Generate `hosts.yml` with `scripts/inventory.sh <floci|aws>`: on Floci from `docker port floci-aws-ec2-<id> 22`, on AWS from `tofu output`. `hosts.yml` is git-ignored.
- [x] `group_vars/all/main.yml` (versions, ports, paths). `vault.yml` (git-ignored, encrypted with ansible-vault) holds the DB and Redis passwords, the JWT secret and the billing shared secret. `vault.example.yml` is committed and mirrors `deploy/env-examples/*`.
- [x] `playbooks/site.yml` (full provision and deploy) and `playbooks/deploy.yml` (build and ship the app only).
- [x] Role `build` (runs on localhost, once): `dotnet publish Presentation/Presentation.csproj -c Release` (same as `deploy/api.Dockerfile`), `dotnet ef migrations bundle --project Infrastructure --startup-project Presentation -r linux-x64 --self-contained`, and `npm ci && npm run build` in `BillingService`.
- [x] Role `common`: apt baseline, a swap file, unattended-upgrades, the SSM agent, and the `mtsaas-api` and `mtsaas-billing` system users.
- [x] Role `postgresql`: PGDG 16, listening on localhost, creates the DB and user.
- [x] Role `redis`: `requirepass`, bound to 127.0.0.1.
- [x] Role `dotnet_runtime`: the Microsoft repo and `aspnetcore-runtime-10.0`.
- [x] Role `nodejs`: NodeSource 22.
- [x] Role `api`: versioned release directory with a `current` symlink, `/etc/multitenant-saas-api/api.env` (0640 root:mtsaas-api) templated from `deploy/env-examples/api.env.example` keys, runs the EF bundle, and a hardened `mtsaas-api.service` on `127.0.0.1:8080`. Waits until `/health` answers.
- [x] Role `billing`: `dist/` plus `package.json`, `mtsaas-billing.service` with `StateDirectory` so workflow state persists under `/var/lib/mtsaas-billing`, and `DOTNET_CALLBACK_BASE_URL=http://127.0.0.1:8080`.
- [x] Role `nginx`: `deploy/nginx/default.conf` ported with upstreams on `127.0.0.1:8080` and `:3001`, keeping the `/api/internal` 403 rule.
- [x] `README.md` with prerequisites (`brew install ansible`) and a run guide.

### v1 — Docs

- [x] New: `docs/deployment-roadmap.md`, `deploy/ansible/README.md`.
- [x] Update `deploy/terraform/README.md`: guide steps 7–9 now point to Ansible, and the SSH and Floci notes are added.
- [x] Update `docs/operations.md`: Terraform section and a new Ansible section.
- [x] Update `docs/decisions.md`: record the v1 topology and the active-passive target under item #1, and leave the other items open.
- [x] Update `docs/CHANGELOG.md` and the `README.md` layout.

## Tests

From the approved plan's verification section:

| # | Covers | Layer | How to run |
| --- | --- | --- | --- |
| 1 | Terraform steps | Infrastructure (Floci) | `tofu fmt -check`, `tofu validate`, `tofu plan`, `tofu apply` on Floci. Confirm the key pair is attached and `ssh -p <port> root@localhost` works. |
| 2 | Ansible steps | Static | `ansible-lint` and `ansible-playbook --syntax-check playbooks/site.yml`. |
| 3 | Ansible roles, end to end | Integration (systemd test host) | Run the full playbook against a privileged **systemd-enabled Debian 12 amd64 container** on Docker. Floci instances have no systemd, so services can't start there. Check `systemctl is-active` for postgresql, redis, mtsaas-api, mtsaas-billing and nginx; `curl` of `/health` and `/billing/health` returns 200, and `/api/internal/...` returns 403; **a second run reports `changed=0`** (idempotency). |
| 4 | Inventory and `common` role on Floci | Smoke (Floci) | `ansible -i inventories/floci -m ping all` and the `common` role against the Floci instance. |
| 5 | Whole v1 | E2E (real AWS, manual) | Apply with `-var-file=aws.tfvars`, run `ansible-playbook -i inventories/aws playbooks/site.yml`, curl the public IP, then `tofu destroy`. |
| 6 | Docs and builds | Static / build | `git diff --check`. .NET and BillingService code is unchanged, but the `build` role exercises `dotnet publish` and `npm run build`. |

## Test results (2026-10-17)

| # | Result |
| --- | --- |
| 1 | `tofu fmt -check`, `validate` pass. `apply` on Floci: key pair created, instance and security group replaced (`Plan: 3 to add, 2 to destroy`); SSH as root on port 2200 works. |
| 2 | `ansible-lint` passes at the `production` profile; `--syntax-check` passes for `site.yml` and `deploy.yml`. |
| 3 | Full converge on the systemd test host: `ok=48 changed=18 failed=0`. All five services active; `/health` and `/billing/health` 200; `/api/internal/...` 403 (any case). Second run `ok=43 changed=0`. |
| 4 | Floci: SSH via `raw` works; `site.yml --tags common` `ok=9 changed=5 failed=0`; `-m ping` then succeeds. |
| 5 | Not run (manual, real AWS). |
| 6 | `git diff --check` clean. The `build` role ran `dotnet publish`, `dotnet ef migrations bundle` and the BillingService `tsc` build. |

## Changes

- **Backups moved from v1 to v1.1** in the roadmap: v1 ships without automated backups, and v1.1 adds the nightly `pg_dump` to S3 and the DLM snapshot policy alongside remote state.
- **The migrations bundle is framework-dependent,** not `--self-contained`: the host already has the .NET runtime, and the bundle is much smaller.
- **Env files are `0600 root:root`,** not `0640 root:mtsaas-api`: systemd reads them as root, so the service users don't need access.
- **BillingService is compiled with `tsc --outDir build/<release>/billing/dist`** instead of `npm run build`, so the developer's `BillingService/dist` isn't touched.
- **BillingService listens on `*:3001`,** not loopback: its code takes no bind address, and v1 makes no application changes. The security group keeps 3001 closed. Documented in the Ansible README.
- **Shared settings live in `playbooks/group_vars/all/main.yml`;** inventories hold only per-environment values. Role defaults hold versions.
- **Added `inventories/test` and `scripts/test-host.sh`** for test 3, with two test-only workarounds: a Redis `PrivateUsers=false` drop-in (nested Docker) and `DOTNET_EnableWriteXorExecute=0` through a new `api_extra_environment` variable (qemu on Apple silicon).
- **IMDSv2 hop limit is now 1,** because nothing runs in containers.
- **`site.yml` installs Python and sudo over `raw`** before gathering facts, because Floci's Debian image has neither, and **refuses placeholder or short secrets.**

## Decisions & trade-offs

- **v1 runs native systemd services configured by Ansible** and replaces the Compose user data. This matches the goal of Ansible configuring .NET, Node.js and PostgreSQL directly. Images aren't built on the host, and later stages swap PostgreSQL to RDS and Redis to ElastiCache by changing a connection string.
- **The multi-region target is active-passive DR.** One primary region takes all writes; Route 53 fails over to a warm standby. This fits a multi-tenant system of record with a single-instance billing worker.
- **TLS moves ahead of the load balancer (v1.1).** The article attaches ACM to the load balancer, but a SaaS with authentication can't wait for one to get HTTPS. certbot covers v1.1, and ACM replaces it in v3.
- **Article stage 3 is skipped.** v2 goes straight to RDS and ElastiCache, because Ansible would only rebuild on a second EC2 what RDS manages.
- **Article stage 14 (caching) moves to v2,** because the app already requires Redis.
- **SSM Session Manager replaces the bastion host (article stage 15),** following the aws-compute guidance.
- **Packer + Ansible golden AMI** for the v3 Auto Scaling group.
- **Migrations run as an explicit EF bundle step,** once per release and never per instance.
- **Billing stays a singleton** until decision #8 replaces the file-backed queue.

## Alternatives considered

- **Ansible drives Docker Compose** (install Docker, deploy `compose.yml`, template env files from Vault). Rejected: .NET, Node.js and PostgreSQL would stay inside containers instead of being configured by Ansible.
- **Active-active multi-region.** Rejected for now: it needs tenant home-region pinning or write-conflict handling, plus a distributed billing queue.
- **Deciding the multi-region shape later.** Rejected in favour of committing to active-passive now.
- **Roadmap document only, or roadmap in the plan only.** Rejected: the branch builds v1 as well.
- **Database on a second EC2 (article stage 3).** Skipped for the reason above.

## Out of scope

- Building v1.1, v2, v3 and v4. They are documented in the roadmap only.
- Application code changes. The v3 prerequisites (dispatcher row claiming, Redis auth limiter) are documented, not built.
- Removing `compose.yml` and `deploy/*.Dockerfile`. They remain the local Ubuntu VM profile.
- Active-active multi-region.
