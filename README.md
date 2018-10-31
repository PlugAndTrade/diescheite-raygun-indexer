# PlugAndTrade.DieScheite.RayGun.Service

Consumes `diescheite` messages and publishes to [Raygun](https://raygun.com)

## Configuration
### Required parameters

```
export RABBITMQ_QUEUE_NAME=diescheite.raygun
export RABBITMQ_HOST=localhost
export RABBITMQ_CONNECTIONNAME=Dieschiete.Raygun.Consumer
export RAYGUN_API_KEY=<api_key>
```

## Getting started
```
dotnet build
dotnet run --project PlugAndTrade.DieScheite.RayGun.Service
```
## Deploy

* The secret `raygun-secret` is expected to exist before the actual deployment.

### 1. Set secrets

```
kubectl create secret generic raygun-secret \
  --from-literal=apiKey=<api_key> \
  --dry-run \
  -o=yaml | kubectl apply -f -
```

### 2. Create deployment

```
kubectl apply -f deploy/deployment.yaml -n monitoring
```
