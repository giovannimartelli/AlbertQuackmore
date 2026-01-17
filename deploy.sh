#!/bin/bash
# deploy.sh

# Build e push locale
docker build -t giovannimartelli/AlbertQuackmore .
docker push giovannimartelli/AlbertQuackmore

# Deploy su RPi
ssh manager@ciaobubu.airdns.org -p 64703 << 'EOF'
  cd /home/manager/expensetraker
  docker compose pull
  docker compose up -d --force-recreate
EOF
