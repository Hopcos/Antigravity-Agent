# ==============================================================
# Build Stage
# ==============================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/AntigravityGateway/*.csproj src/AntigravityGateway/
RUN dotnet restore src/AntigravityGateway/AntigravityGateway.csproj

COPY src/AntigravityGateway/ src/AntigravityGateway/
WORKDIR /src/AntigravityGateway
RUN dotnet publish AntigravityGateway.csproj -c Release -o /app/publish /p:UseAppHost=false

# ==============================================================
# Runtime Stage
# ==============================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 5055

ENV ASPNETCORE_URLS=http://+:5055
ENV DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "AntigravityGateway.dll"]
