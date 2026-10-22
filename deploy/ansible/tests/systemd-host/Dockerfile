# A Debian 12 amd64 host with systemd and sshd, standing in for the EC2 instance.
# Floci's instances have no systemd, so the full playbook is verified here instead.
FROM debian:12

ENV container=docker
RUN apt-get update \
 && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
      systemd systemd-sysv dbus openssh-server python3 sudo ca-certificates \
 && rm -rf /var/lib/apt/lists/* \
 && systemctl enable ssh \
 && mkdir -p /root/.ssh && chmod 700 /root/.ssh

STOPSIGNAL SIGRTMIN+3
CMD ["/sbin/init"]
