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
