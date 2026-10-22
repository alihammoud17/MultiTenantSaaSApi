data "aws_ami" "debian" {
  most_recent = true
  owners      = var.ami_owners

  filter {
    name   = "name"
    values = [var.ami_name]
  }

  filter {
    name   = "architecture"
    values = ["x86_64"]
  }
}

# Instance profile for SSM Session Manager access instead of inbound SSH.
resource "aws_iam_role" "app_instance" {
  name = "${var.name_prefix}-${var.environment}-app-instance"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "ec2.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_role_policy_attachment" "app_instance_ssm" {
  role       = aws_iam_role.app_instance.name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

resource "aws_iam_instance_profile" "app" {
  name = "${var.name_prefix}-${var.environment}-app"
  role = aws_iam_role.app_instance.name
}

# Key pair Ansible connects with. Only the public key is sent to AWS.
resource "aws_key_pair" "ansible" {
  count = var.ssh_public_key == null ? 0 : 1

  key_name   = "${var.name_prefix}-${var.environment}-ansible"
  public_key = var.ssh_public_key
}

resource "aws_instance" "app" {
  ami                         = data.aws_ami.debian.id
  instance_type               = var.instance_type
  subnet_id                   = aws_subnet.public.id
  vpc_security_group_ids      = [aws_security_group.app.id]
  associate_public_ip_address = true
  iam_instance_profile        = aws_iam_instance_profile.app.name
  key_name                    = one(aws_key_pair.ansible[*].key_name)

  # No user data: Ansible configures the host over SSH (deploy/ansible/).

  # T3 defaults to unlimited bursting, which bills surplus CPU credits.
  credit_specification {
    cpu_credits = "standard"
  }

  metadata_options {
    http_endpoint               = "enabled"
    http_tokens                 = "required"
    http_put_response_hop_limit = 1 # Services run natively; no container hop.
  }

  root_block_device {
    volume_type = "gp3"
    volume_size = var.root_volume_size_gb
    encrypted   = true
  }

  tags = {
    Name = "${var.name_prefix}-${var.environment}-app"
  }
}
