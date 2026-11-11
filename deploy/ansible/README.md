# Ansible: v1 single-host configuration

Configures the one EC2 host that `deploy/terraform/` creates and deploys the application to it as native systemd services. No Docker runs on the host. This is v1 of [the deployment roadmap](../../docs/deployment-roadmap.md).

| Service | Source | Listens on |
| --- | --- | --- |
| PostgreSQL 16 | PGDG apt repository | `127.0.0.1:5432` |
| Redis 7.0 (password required) | Debian | `127.0.0.1:6379` |
| .NET API (`mtsaas-api.service`) | built on the controller; ASP.NET Core 10 runtime from the Microsoft repository | `127.0.0.1:8080` |
| BillingService (`mtsaas-billing.service`) | built on the controller; Node 22 from NodeSource | `*:3001` (see below) |
| NGINX | Debian | `:80`, the only public port |

BillingService binds every interface because its code takes no bind address. The security group only opens port 80 (and 22 when you ask for it), so 3001 isn't reachable from outside the instance.

## Layout

| Path | Purpose |
| --- | --- |
| `playbooks/site.yml` | Full provision and deploy. Every role is tagged with its name, so `--tags nginx` runs one role. |
| `playbooks/deploy.yml` | Build and ship a new application release only. |
| `playbooks/group_vars/all/main.yml` | Settings shared by every environment: paths, ports, DB name, service users. |
| `inventories/{aws,floci,test}/` | Per-environment settings. `hosts.yml` and `group_vars/all/vault.yml` are generated or created locally and git-ignored. |
| `vault.example.yml` | Template for the secrets file. Its keys map to `deploy/env-examples/*.env.example`. |
| `roles/build` | Runs on the controller: `dotnet publish` (same as `deploy/api.Dockerfile`), an EF Core migrations bundle, and the BillingService TypeScript build. Writes to the git-ignored `build/<release>/`. |
| `roles/{common,postgresql,redis,dotnet_runtime,nodejs,nginx}` | Host packages and configuration. |
| `roles/{api,billing}` | Releases, env files, systemd units, health waits. |
| `scripts/inventory.sh` | Writes `inventories/<floci|aws>/hosts.yml` from the Terraform outputs. |
| `scripts/test-host.sh` | Starts a local Debian 12 container with systemd and writes `inventories/test/`. |

## How a release works

1. The `build` role names the release after the commit (`git rev-parse --short=12 HEAD`). If the build inputs (`Application`, `Domain`, `Infrastructure`, `Presentation`, `BillingService`, the solution) have uncommitted changes, it appends `-dirty-<timestamp>`, so a dirty tree always builds and ships a new release. A clean commit that has already been built is reused, which is what makes a second run report no changes.
2. The `api` and `billing` roles unpack the archives to `/opt/multitenant-saas-api/{api,billing}/releases/<release>/`.
3. The `api` role runs the migrations bundle against PostgreSQL before switching anything. A failed migration leaves the previous release serving.
4. The `current` symlink moves to the new release, systemd restarts the service, and the play waits until `/health` returns 200.

Old releases are kept. Remove them by hand from `releases/` when disk space matters. Roll back by pointing `current` at an earlier release and restarting the service; migrations aren't rolled back.

Runtime secrets live only in `/etc/multitenant-saas-api/{api,billing}.env` (`0600`, root). systemd reads them as root, so the service users can't. BillingService keeps its workflow queue in `/var/lib/mtsaas-billing/`, which survives releases.

## Prerequisites (controller)

```bash
brew install ansible ansible-lint opentofu
ansible-galaxy collection install -r deploy/ansible/requirements.yml
# ansible-lint runs its own Python; give it the collections too:
ansible-galaxy collection install -r deploy/ansible/requirements.yml -p ~/.ansible/collections
```

The `build` role also needs the .NET 10 SDK, `dotnet-ef` (`dotnet tool install --global dotnet-ef`, or set `DOTNET_EF_BIN`), Node 22 and `git`.

Run every `ansible-*` command below from `deploy/ansible/`.

## Secrets

```bash
cp vault.example.yml inventories/aws/group_vars/all/vault.yml
# replace every value, e.g. with: openssl rand -base64 48
ansible-vault encrypt inventories/aws/group_vars/all/vault.yml
```

The file is git-ignored. Pass `--ask-vault-pass` (or set `ANSIBLE_VAULT_PASSWORD_FILE`) to every playbook run. `site.yml` refuses to run if a secret is missing, too short, or still contains `CHANGE_ME`.

## Run against real AWS

1. Create a key pair for Ansible and apply Terraform with it and your IP. See [the Terraform guide](../terraform/README.md#manual-provisioning-guide).

   ```bash
   ssh-keygen -t ed25519 -f ~/.ssh/mtsaas-ansible -N ''
   # in deploy/terraform/aws.tfvars:
   #   ssh_public_key    = "<contents of ~/.ssh/mtsaas-ansible.pub>"
   #   ssh_ingress_cidrs = ["<your public IP>/32"]
   #   ansible_user      = "admin"
   ```

2. Generate the inventory, then provision:

   ```bash
   scripts/inventory.sh aws ~/.ssh/mtsaas-ansible
   ansible -i inventories/aws app -m ping
   ansible-playbook -i inventories/aws playbooks/site.yml --ask-vault-pass
   ```

3. Smoke-test from your machine:

   ```bash
   IP=$(tofu -chdir=../terraform output -raw app_instance_public_ip)
   curl -i "http://$IP/health"           # 200 from the API
   curl -i "http://$IP/billing/health"   # 200 from BillingService
   curl -i -X POST "http://$IP/api/internal/billing/subscription-events"   # 403 from NGINX
   ```

4. Ship later code changes with `ansible-playbook -i inventories/aws playbooks/deploy.yml --ask-vault-pass`.

## Run against Floci

Floci runs each instance as a Debian container with `tail -f /dev/null` as PID 1, so there is no systemd and the services can't start. Use Floci to check connectivity and the `common` role only:

```bash
scripts/inventory.sh floci ~/.ssh/mtsaas-floci   # key registered through ssh_public_key
cp vault.example.yml inventories/floci/group_vars/all/vault.yml   # any values of the required length
ansible -i inventories/floci app -m raw -a 'cat /etc/debian_version'   # SSH works
ansible-playbook -i inventories/floci playbooks/site.yml --tags common
ansible -i inventories/floci app -m ping                                # Python is there now
```

Floci's Debian image has no Python, so `-m ping` fails until `site.yml` has run once; its first task installs Python over `raw`. Floci starts sshd about a minute after the instance launches.

## Run the full playbook locally (systemd test host)

```bash
scripts/test-host.sh up          # Debian 12 amd64 + systemd, SSH on 2222, HTTP on 8088
ansible-playbook -i inventories/test playbooks/site.yml
curl -i http://localhost:8088/health
ansible-playbook -i inventories/test playbooks/site.yml   # second run: changed=0
scripts/test-host.sh down
```

`test-host.sh` generates throwaway secrets in the git-ignored `inventories/test/group_vars/all/vault.yml`. It applies two workarounds that only the container needs:

- Nested Docker can't create the user namespace Debian's Redis unit asks for (`PrivateUsers=true`), so the script installs a drop-in that turns it off.
- On Apple silicon the amd64 container runs under qemu, where .NET's W^X memory mapping crashes the runtime. `inventories/test` sets `DOTNET_EnableWriteXorExecute=0` through `api_extra_environment`.

Under emulation an apt task can also segfault now and then; re-run the playbook.

## Validation

```bash
ansible-lint                                    # production profile
ansible-playbook -i inventories/test playbooks/site.yml --syntax-check
```
