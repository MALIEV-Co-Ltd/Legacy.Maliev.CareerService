# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ENV GITHUB_ACTIONS=false
COPY nuget.config .
COPY .dependencies/ .dependencies/
COPY Legacy.Maliev.CareerService.Api/Legacy.Maliev.CareerService.Api.csproj Legacy.Maliev.CareerService.Api/
COPY Legacy.Maliev.CareerService.Application/Legacy.Maliev.CareerService.Application.csproj Legacy.Maliev.CareerService.Application/
COPY Legacy.Maliev.CareerService.Domain/Legacy.Maliev.CareerService.Domain.csproj Legacy.Maliev.CareerService.Domain/
COPY Legacy.Maliev.CareerService.Data/Legacy.Maliev.CareerService.Data.csproj Legacy.Maliev.CareerService.Data/
RUN dotnet restore Legacy.Maliev.CareerService.Api/Legacy.Maliev.CareerService.Api.csproj \
    -p:MalievWorkspaceRoot=/src/.dependencies
COPY . .
RUN dotnet publish Legacy.Maliev.CareerService.Api/Legacy.Maliev.CareerService.Api.csproj \
    --configuration Release --no-restore --output /app /p:UseAppHost=false \
    -p:MalievWorkspaceRoot=/src/.dependencies

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /app .
USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Legacy.Maliev.CareerService.Api.dll"]
