output "vpc_id" {
  value = aws_vpc.main.id
}

output "public_subnet_id" {
  value = aws_subnet.public.id
}

output "internet_gateway_id" {
  value = aws_internet_gateway.main.id
}

output "app_security_group_id" {
  value = aws_security_group.app.id
}

output "app_instance_id" {
  value = aws_instance.app.id
}

output "app_instance_ami_id" {
  value = data.aws_ami.debian.id
}

output "app_instance_public_ip" {
  value = aws_instance.app.public_ip
}

output "app_health_url" {
  description = "NGINX health route once Ansible has configured the host."
  value       = "http://${aws_instance.app.public_ip}/health"
}

output "ansible_user" {
  description = "SSH user for the Ansible inventory (deploy/ansible/scripts/inventory.sh)."
  value       = var.ansible_user
}
