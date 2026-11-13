#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"

# B1 Linux autorizado: precio publico consultado de USD 0.018/h en Central US.
export SUBSCRIPTION_ID="d9cbd201-81d6-4884-840f-3f29b7fd7ad7"
export RESOURCE_GROUP="rg-jira-dev-centralus"
export LOCATION="centralus"
export PLAN_NAME="plan-jira-b1-centralus"
export APP_NAME="jira-g09-d9cbd201-centralus"
export SKU="B1"

bash ./deploy.sh
