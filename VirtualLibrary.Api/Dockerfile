# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Solution-level MSBuild + package manifests (copy first for layer caching)
COPY Directory.Build.props Directory.Build.targets Directory.Packages.props global.json ./
COPY VirtualLibrary.Shared/VirtualLibrary.Shared.csproj VirtualLibrary.Shared/
COPY VirtualLibrary.Api/VirtualLibrary.Api.csproj VirtualLibrary.Api/

RUN dotnet restore VirtualLibrary.Api/VirtualLibrary.Api.csproj

# Now copy the full source and publish
COPY VirtualLibrary.Shared/ VirtualLibrary.Shared/
COPY VirtualLibrary.Api/    VirtualLibrary.Api/

RUN dotnet publish VirtualLibrary.Api/VirtualLibrary.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "VirtualLibrary.Api.dll"]
