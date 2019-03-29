FROM microsoft/dotnet:2.1-sdk AS builder

ENV APP_DIR=/app
ENV DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true
ENV NUGET_XMLDOC_MODE=skip
RUN mkdir -p $APP_DIR

WORKDIR $APP_DIR
ADD . $APP_DIR
RUN dotnet build -c Release PlugAndTrade.DieScheite.RayGun.Service.sln \
  && dotnet publish -c Release PlugAndTrade.DieScheite.RayGun.Service.sln -o ./out

FROM microsoft/dotnet:2.1-runtime
WORKDIR /app
COPY --from=builder /app/PlugAndTrade.DieScheite.RayGun.Service/out .
ENTRYPOINT ["dotnet", "PlugAndTrade.DieScheite.RayGun.Service.dll"]
