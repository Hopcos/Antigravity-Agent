# ==============================================================
# Build Stage
# ==============================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/GeminiAgent/*.csproj src/GeminiAgent/
RUN dotnet restore src/GeminiAgent/GeminiAgent.csproj

COPY src/GeminiAgent/ src/GeminiAgent/
WORKDIR /src/GeminiAgent
RUN dotnet publish GeminiAgent.csproj -c Release -o /app/publish /p:UseAppHost=false

# ==============================================================
# Runtime Stage
# ==============================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 5055

ENV ASPNETCORE_URLS=http://+:5055
ENV DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "GeminiAgent.dll"]
