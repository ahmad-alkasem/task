FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Shared.Contracts/Shared.Contracts.csproj src/Shared.Contracts/
COPY src/ServiceB.Api/ServiceB.Api.csproj src/ServiceB.Api/
RUN dotnet restore src/ServiceB.Api/ServiceB.Api.csproj

COPY src/ src/
RUN dotnet publish src/ServiceB.Api/ServiceB.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ServiceB.Api.dll"]
